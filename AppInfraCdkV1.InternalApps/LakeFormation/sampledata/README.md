# FHIR Sample Data Generation for Lake Formation

This directory contains scripts and tools for generating synthetic FHIR R4 data using Synthea and testing the complete data pipeline: **Synthea → S3 → HealthLake → Lake Formation → Athena**.

## Overview

The sample data pipeline validates the end-to-end FHIR data flow through our Lake Formation infrastructure:

1. **Generate** synthetic FHIR R4 data using Synthea
2. **Upload** to S3 with proper tenant isolation and Lake Formation tags  
3. **Import** into HealthLake datastore
4. **Query** via Athena through Lake Formation permissions
5. **Validate** tenant isolation and data access controls

## Prerequisites

### System Requirements
- **Java 11+** (for building Synthea)
- **Git** (for cloning Synthea repository)  
- **AWS CLI** (configured with appropriate credentials)
- **jq** (for JSON parsing in scripts)

### AWS Setup
- Lake Formation stack deployed (`dev-lf-*` stacks)
- S3 buckets created (`thirdopinion-raw-development-us-east-2`)
- HealthLake datastore provisioned
- Proper IAM permissions for S3, HealthLake, and Lake Formation

### AWS Credentials
```bash
# Configure AWS CLI with SSO
aws configure sso

# Or use specific profile
export AWS_PROFILE=to-dev-admin

# Verify access
aws sts get-caller-identity
```

## Quick Start

### 1. Build Synthea
```bash
./build-synthea.sh
```

This will:
- Clone the Synthea repository from GitHub
- Build with Gradle (downloads dependencies automatically)
- Create convenience symlinks and wrapper scripts
- Verify the build works correctly

### 2. Generate FHIR Data
```bash
# Generate 100 patients (default)
./generate-fhir-data.sh

# Generate 500 patients in California
./generate-fhir-data.sh -p 500 -s California -c "Los Angeles"

# Clean previous data and generate 1000 patients
./generate-fhir-data.sh --clean -p 1000
```

Generated data will be organized by FHIR resource type in `generated-data/organized/`.

### 3. Upload to S3
```bash
# Upload to development environment (default)
./upload-to-s3.sh

# Preview upload without executing
./upload-to-s3.sh --dry-run

# Upload to production with specific tenant
./upload-to-s3.sh -e production -t "20000000-0000-0000-0000-000000000002"

# Use different AWS profile
./upload-to-s3.sh -p my-aws-profile
```

### 4. Import into HealthLake
Follow the instructions in `healthlake-import-instructions.md` (generated after upload).

### 5. Query with Athena
Use the sample queries in `test-athena-queries.sql` to validate the data pipeline.

## Files and Scripts

### Core Scripts

| Script | Purpose | Prerequisites |
|--------|---------|--------------|
| `build-synthea.sh` | Clone and build Synthea from source | Java 11+, Git |
| `generate-fhir-data.sh` | Generate synthetic FHIR R4 data | Synthea built |
| `upload-to-s3.sh` | Upload data to Lake Formation S3 bucket | AWS CLI, Lake Formation deployed |

### Configuration Files

| File | Purpose |
|------|---------|
| `../lakeformation-config.json` | Lake Formation environment configuration |
| `test-athena-queries.sql` | Sample Athena queries for validation |
| `README.md` | This documentation file |

### Generated Files

| File/Directory | Purpose |
|----------------|---------|
| `synthea/` | Cloned Synthea repository |
| `synthea.jar` | Symlink to built Synthea JAR |
| `run-synthea.sh` | Convenience script for running Synthea |
| `generated-data/` | Generated FHIR data organized by resource type |
| `healthlake-import-instructions.md` | Import instructions (created after upload) |

## Data Structure

### Generated FHIR Data
```
generated-data/organized/
├── data-summary.json          # Summary of generated data
├── Patient/                   # Patient resources
│   ├── patient-001.json
│   └── ...
├── Encounter/                 # Encounter resources
├── Observation/               # Observation resources
├── Condition/                 # Condition resources
├── Medication/                # Medication resources
├── MedicationRequest/         # Medication request resources
├── Procedure/                 # Procedure resources
├── Immunization/              # Immunization resources
└── ...
```

### S3 Structure
```
s3://thirdopinion-raw-development-us-east-2/
└── tenants/
    └── 10000000-0000-0000-0000-000000000001/
        └── fhir/
            ├── raw/                    # HealthLake exports
            └── ingestion/              # Import staging area
                └── 20240817_143022_synthea/
                    ├── metadata.json
                    ├── data-summary.json
                    ├── Patient/
                    ├── Encounter/
                    └── ...
```

## Configuration

### Environment Configuration
The scripts use `../lakeformation-config.json` to determine:
- S3 bucket names and structure
- Tenant IDs for isolation
- AWS account and region settings
- Lake Formation configuration

### Default Settings

| Setting | Development | Production |
|---------|-------------|------------|
| Tenant ID | `10000000-0000-0000-0000-000000000001` | `20000000-0000-0000-0000-000000000002` |
| AWS Profile | `to-dev-admin` | `to-prd-admin` |
| Bucket | `thirdopinion-raw-development-us-east-2` | `thirdopinion-raw-production-us-east-2` |
| Population | 100 patients | User specified |

## Troubleshooting

### Common Issues

#### Java Build Issues
```bash
# Check Java version
java -version

# Clean and rebuild
./build-synthea.sh --clean
```

#### AWS Access Issues
```bash
# Verify credentials
aws sts get-caller-identity

# Check bucket access
aws s3 ls s3://thirdopinion-raw-development-us-east-2/

# Login to SSO if needed
aws sso login --profile to-dev-admin
```

#### Data Generation Issues
```bash
# Clean and regenerate
./generate-fhir-data.sh --clean -p 50

# Check logs for errors
tail -f generated-data/synthea.log
```

#### Upload Issues
```bash
# Test with dry run first
./upload-to-s3.sh --dry-run

# Check permissions
aws s3 ls s3://thirdopinion-raw-development-us-east-2/tenants/
```

### Getting Help

Each script has built-in help:
```bash
./build-synthea.sh --help
./generate-fhir-data.sh --help
./upload-to-s3.sh --help
```

## Data Validation

### Generated Data Quality
- Check `generated-data/organized/data-summary.json` for statistics
- Verify FHIR resource types and counts
- Review patient demographics and geographic distribution

### Upload Validation
- Verify S3 objects with proper metadata and tags
- Check tenant isolation in bucket structure
- Confirm Lake Formation resource registration

### HealthLake Import Validation
- Monitor import job status and completion
- Verify data appears in HealthLake console
- Check for import errors or warnings

### Athena Query Validation
- Test basic connectivity and table access
- Verify tenant-specific database creation
- Validate data quality and referential integrity
- Confirm Lake Formation permission enforcement

## Advanced Usage

### Custom Data Generation
```bash
# Generate specific populations
./generate-fhir-data.sh -p 1000 -s "New York" -c "New York City"

# Multiple runs for different demographics
./generate-fhir-data.sh -p 200 -s Massachusetts -c Boston
./generate-fhir-data.sh -p 300 -s California -c "San Francisco"
```

### Batch Operations
```bash
# Generate and upload in one workflow
./generate-fhir-data.sh -p 500 && ./upload-to-s3.sh

# Upload to multiple environments
./upload-to-s3.sh -e development
./upload-to-s3.sh -e production -t "20000000-0000-0000-0000-000000000002"
```

### Custom Tenant Testing
```bash
# Test with different tenant IDs
./upload-to-s3.sh -t "30000000-0000-0000-0000-000000000003"

# Verify tenant isolation in Athena
# (Use appropriate database name in queries)
```

## Integration with CI/CD

### GitHub Actions Integration
The scripts can be integrated into CI/CD pipelines for automated testing:

```yaml
- name: Generate and test FHIR data
  run: |
    cd AppInfraCdkV1.InternalApps/LakeFormation/sampledata
    ./build-synthea.sh
    ./generate-fhir-data.sh -p 50
    ./upload-to-s3.sh --dry-run
```

### Testing Pipeline
1. **Unit Tests**: Validate script functionality
2. **Integration Tests**: Test full pipeline with small dataset
3. **Performance Tests**: Validate with larger datasets
4. **Security Tests**: Verify tenant isolation and access controls

## Contributing

When modifying the scripts:
1. Test with `--dry-run` or small datasets first
2. Update this README with any new features
3. Maintain backward compatibility with existing configurations
4. Add appropriate error handling and validation

## Related Documentation

- [Lake Formation Setup](../README.md)
- [HealthLake Configuration](../README-IdentityCenter-Integration.md)
- [Synthea Documentation](https://github.com/synthetichealth/synthea)
- [FHIR R4 Specification](https://hl7.org/fhir/R4/)
- [AWS HealthLake Documentation](https://docs.aws.amazon.com/healthlake/)
- [Lake Formation User Guide](https://docs.aws.amazon.com/lake-formation/)