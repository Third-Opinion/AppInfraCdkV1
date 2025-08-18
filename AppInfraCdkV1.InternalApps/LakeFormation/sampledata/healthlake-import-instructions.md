# HealthLake Import Instructions

## Upload Summary
- **Bucket**: `thirdopinion-raw-development-us-east-2`
- **Tenant ID**: `10000000-0000-0000-0000-000000000001`
- **Upload Path**: `tenants/10000000-0000-0000-0000-000000000001/fhir/ingestion/`
- **Data Format**: FHIR R4 JSON
- **Upload Date**: 2025-08-18 00:41:37 UTC

## Next Steps

### 1. Start HealthLake Import Job

```bash
# Get the latest upload folder
LATEST_UPLOAD=$(aws s3 ls s3://thirdopinion-raw-development-us-east-2/tenants/10000000-0000-0000-0000-000000000001/fhir/ingestion/ | tail -1 | awk '{print $2}' | sed 's|/||')

# Start import job (replace DATASTORE_ID with actual HealthLake datastore ID)
aws healthlake start-fhir-import-job \
    --input-data-config S3Uri="s3://thirdopinion-raw-development-us-east-2/tenants/10000000-0000-0000-0000-000000000001/fhir/ingestion/$LATEST_UPLOAD/" \
    --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID" \
    --data-access-role-arn "arn:aws:iam::615299752206:role/HealthLakeServiceRole" \
    --job-name "synthea-import-$(date +%Y%m%d-%H%M%S)"
```

### 2. Monitor Import Job

```bash
# List import jobs
aws healthlake list-fhir-import-jobs --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID"

# Check specific job status
aws healthlake describe-fhir-import-job \
    --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID" \
    --job-id "JOB_ID_FROM_PREVIOUS_COMMAND"
```

### 3. Query Data via Athena

Once imported, you can query the data through Athena using Lake Formation:

```sql
-- List databases
SHOW DATABASES;

-- Use tenant-specific database
USE fhir_raw_10000000_0000_0000_0000_000000000001_development;

-- List tables
SHOW TABLES;

-- Query patients
SELECT * FROM patient LIMIT 10;

-- Query encounters
SELECT 
    patient_id,
    encounter_class,
    encounter_type,
    start_date,
    end_date
FROM encounter 
WHERE start_date >= '2023-01-01'
LIMIT 100;
```

## Bucket Structure

```
s3://thirdopinion-raw-development-us-east-2/
├── tenants/
│   └── 10000000-0000-0000-0000-000000000001/
│       └── fhir/
│           ├── raw/           # Raw FHIR exports from HealthLake
│           └── ingestion/     # Incoming data for import
│               └── ${upload_id}/
│                   ├── metadata.json
│                   ├── data-summary.json
│                   ├── Patient/
│                   ├── Encounter/
│                   ├── Observation/
│                   └── ...
```

## Lake Formation Configuration

The data is automatically tagged and organized for Lake Formation:
- **Tenant ID**: `10000000-0000-0000-0000-000000000001`
- **Data Source**: `Synthea`
- **FHIR Version**: `R4`
- **Environment**: `development`

