# HealthLake ETL Processor

A C# ETL processor for transforming AWS HealthLake FHIR exports into partitioned Parquet files with multi-tenant support.

## Features

- Processes NDJSON files exported from AWS HealthLake
- Extracts tenant IDs from FHIR security labels
- Partitions data by tenant GUID in S3
- Converts FHIR resources to Parquet format for efficient querying
- Creates/updates AWS Glue catalog tables
- Supports both Lambda and standalone console execution

## Multi-Tenant Architecture

The processor extracts tenant information from FHIR security labels with the following format:

```json
{
  "security": [
    {
      "system": "http://thirdopinion.io/identity/claims/tenant",
      "code": "11111111-1111-1111-1111-111111111111"
    }
  ]
}
```

Data is partitioned in S3 by tenant:
```
s3://curated-bucket/
  └── patient/
      ├── tenantGuid=11111111-1111-1111-1111-111111111111/
      │   └── _export_timestamp=20240113-120000/
      │       └── data.parquet
      └── tenantGuid=22222222-2222-2222-2222-222222222222/
          └── _export_timestamp=20240113-120000/
              └── data.parquet
```

## Usage

### As Lambda Function

Deploy as an AWS Lambda function and trigger via:
- S3 events when HealthLake exports complete
- EventBridge scheduled rules
- Direct invocation

Environment variables:
- `RAW_BUCKET`: S3 bucket containing raw HealthLake exports
- `CURATED_BUCKET`: S3 bucket for processed Parquet files
- `TENANT_PARTITION_KEY`: Partition key name (default: "tenantGuid")
- `ENABLE_MULTI_TENANT`: Enable multi-tenant processing (default: "true")

### As Console Application

Run as a standalone console application or AWS Glue job:

```bash
dotnet run -- \
  --export-path s3://raw-bucket/healthlake-exports/20240113-120000/ \
  --raw-bucket raw-bucket \
  --curated-bucket curated-bucket \
  --multi-tenant true \
  --tenant-key tenantGuid
```

### As AWS Glue Job

Build and upload to S3:

```bash
# Build the application
dotnet publish -c Release -r linux-x64 --self-contained

# Upload to S3
aws s3 cp bin/Release/net8.0/linux-x64/publish/ \
  s3://glue-scripts-bucket/healthlake-etl/ \
  --recursive

# Create Glue job
aws glue create-job \
  --name healthlake-etl \
  --role arn:aws:iam::account:role/GlueServiceRole \
  --command Name=glueetl,ScriptLocation=s3://glue-scripts-bucket/healthlake-etl/healthlake-etl \
  --default-arguments '{
    "--export-path": "s3://raw-bucket/exports/",
    "--raw-bucket": "raw-bucket",
    "--curated-bucket": "curated-bucket"
  }'
```

## Command Line Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| --export-path | -e | Yes | S3 path containing HealthLake export files |
| --raw-bucket | -r | Yes | S3 bucket for raw data |
| --curated-bucket | -c | Yes | S3 bucket for curated data |
| --tenant-key | -t | No | Partition key for tenant data (default: tenantGuid) |
| --multi-tenant | -m | No | Enable multi-tenant processing (default: true) |
| --timestamp | -s | No | Export timestamp (defaults to current time) |
| --job-id | -j | No | HealthLake export job ID for tracking |

## Building

```bash
# Build for Lambda
dotnet build

# Build for standalone execution
dotnet publish -c Release

# Build for AWS Glue (Linux)
dotnet publish -c Release -r linux-x64 --self-contained
```

## Testing

```bash
# Run unit tests
dotnet test

# Run locally with sample data
dotnet run -- \
  --export-path ./sample-data/ \
  --raw-bucket test-raw \
  --curated-bucket test-curated
```

## Supported FHIR Resource Types

- Patient
- Observation
- Condition
- MedicationRequest
- Procedure
- DiagnosticReport
- Encounter
- AllergyIntolerance
- Immunization
- CarePlan
- Goal
- MedicationStatement

## Output Format

Data is written as Snappy-compressed Parquet files with the following schema:

### Common Fields (all resources)
- `id`: Resource ID
- `resourceType`: FHIR resource type
- `meta_versionId`: Version identifier
- `meta_lastUpdated`: Last update timestamp
- `tenantGuid`: Tenant identifier (partition key)
- `_export_timestamp`: Export timestamp (partition key)
- `_processing_date`: Processing date

### Resource-Specific Fields
Each resource type has additional fields based on the FHIR specification. Complex nested structures are serialized as JSON strings.

## Glue Catalog Integration

The processor automatically creates/updates AWS Glue catalog tables with:
- Partitioning by tenant and export timestamp
- Projection enabled for efficient queries
- Parquet SerDe configuration
- Compression settings

## Security Considerations

- All data is partitioned by tenant at the storage level
- S3 server-side encryption (AES256) is enabled
- IAM roles should follow least-privilege principles
- Sensitive PHI data should use KMS encryption

## Dependencies

- .NET 8.0
- AWS SDK for .NET (S3, Glue)
- Parquet.Net
- Newtonsoft.Json
- CommandLineParser

## License

Copyright (c) Third Opinion. All rights reserved.