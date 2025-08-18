#!/bin/bash

# upload-to-s3.sh
# Script to upload generated FHIR data to Lake Formation S3 bucket
# Must be run after generate-fhir-data.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="$SCRIPT_DIR/generated-data/organized"
CONFIG_FILE="$SCRIPT_DIR/../lakeformation-config.json"

# Default configuration
DEFAULT_ENVIRONMENT="development"
DEFAULT_TENANT_ID="10000000-0000-0000-0000-000000000001"
DEFAULT_AWS_PROFILE="to-dev-admin"

echo "📤 Uploading FHIR Data to Lake Formation S3"
echo "============================================"

# Check prerequisites
check_prerequisites() {
    echo "🔍 Checking prerequisites..."
    
    # Check AWS CLI
    if ! command -v aws &> /dev/null; then
        echo "❌ AWS CLI is not installed. Please install AWS CLI."
        exit 1
    fi
    
    # Check jq for JSON parsing
    if ! command -v jq &> /dev/null; then
        echo "❌ jq is not installed. Please install jq for JSON parsing."
        exit 1
    fi
    
    # Check if data exists
    if [ ! -d "$DATA_DIR" ]; then
        echo "❌ Generated data not found at $DATA_DIR"
        echo "   Please run './generate-fhir-data.sh' first."
        exit 1
    fi
    
    # Check config file
    if [ ! -f "$CONFIG_FILE" ]; then
        echo "❌ Lake Formation config not found at $CONFIG_FILE"
        exit 1
    fi
    
    echo "✅ Prerequisites satisfied"
}

# Get configuration from JSON file
get_config() {
    local environment="$1"
    
    echo "📋 Loading configuration for environment: $environment"
    
    # Extract configuration using jq
    BUCKET_PREFIX=$(jq -r ".environments.${environment}.bucketConfig.bucketPrefix" "$CONFIG_FILE")
    REGION=$(jq -r ".environments.${environment}.region" "$CONFIG_FILE")
    ACCOUNT_ID=$(jq -r ".environments.${environment}.accountId" "$CONFIG_FILE")
    
    if [ "$BUCKET_PREFIX" = "null" ] || [ "$REGION" = "null" ] || [ "$ACCOUNT_ID" = "null" ]; then
        echo "❌ Invalid configuration for environment: $environment"
        exit 1
    fi
    
    # Construct bucket name based on Lake Formation naming convention
    BUCKET_NAME="${BUCKET_PREFIX}-raw-${environment}-${REGION}"
    
    echo "✅ Configuration loaded:"
    echo "   Bucket: $BUCKET_NAME"
    echo "   Region: $REGION"
    echo "   Account: $ACCOUNT_ID"
}

# Verify AWS credentials and bucket access
verify_aws_access() {
    local aws_profile="$1"
    
    echo "🔐 Verifying AWS access..."
    
    # Set AWS profile if provided
    if [ -n "$aws_profile" ]; then
        export AWS_PROFILE="$aws_profile"
        echo "   Using AWS profile: $aws_profile"
    fi
    
    # Test AWS credentials
    local current_account
    current_account=$(aws sts get-caller-identity --query "Account" --output text 2>/dev/null || echo "")
    
    if [ "$current_account" != "$ACCOUNT_ID" ]; then
        echo "❌ AWS credentials mismatch or not configured"
        echo "   Expected account: $ACCOUNT_ID"
        echo "   Current account: ${current_account:-'not available'}"
        echo ""
        echo "   Please ensure:"
        echo "   1. AWS CLI is configured with correct credentials"
        echo "   2. Using correct AWS profile: $aws_profile"
        echo "   3. Run 'aws sso login' if using SSO"
        exit 1
    fi
    
    # Test bucket access
    if ! aws s3 ls "s3://$BUCKET_NAME/" >/dev/null 2>&1; then
        echo "❌ Cannot access bucket: $BUCKET_NAME"
        echo "   Please ensure:"
        echo "   1. Lake Formation stack is deployed"
        echo "   2. You have proper S3 permissions"
        echo "   3. Bucket exists in account $ACCOUNT_ID"
        exit 1
    fi
    
    echo "✅ AWS access verified"
}

# Create tenant folder structure
create_folder_structure() {
    local tenant_id="$1"
    
    echo "📁 Creating folder structure for tenant: $tenant_id"
    
    # Define folder structure based on Lake Formation patterns
    TENANT_PREFIX="tenants/${tenant_id}"
    FHIR_PREFIX="${TENANT_PREFIX}/fhir"
    RAW_PREFIX="${FHIR_PREFIX}/raw"
    INGESTION_PREFIX="${FHIR_PREFIX}/ingestion"
    
    echo "   Tenant prefix: $TENANT_PREFIX"
    echo "   FHIR prefix: $FHIR_PREFIX"
    echo "   Raw data prefix: $RAW_PREFIX"
    echo "   Ingestion prefix: $INGESTION_PREFIX"
}

# Upload data to S3 with proper structure and tags
upload_data() {
    local tenant_id="$1"
    local dry_run="$2"
    
    echo "📤 Uploading FHIR data..."
    
    # Create timestamp for this upload
    local upload_timestamp=$(date -u +"%Y%m%d_%H%M%S")
    local upload_id="${upload_timestamp}_synthea"
    
    # Set upload path for raw ingestion data
    local upload_path="${INGESTION_PREFIX}/${upload_id}"
    
    if [ "$dry_run" = true ]; then
        echo "🔍 DRY RUN - Would upload to: s3://$BUCKET_NAME/$upload_path/"
        echo "📊 Files to upload:"
        find "$DATA_DIR" -name "*.json" | head -10 | while read file; do
            local relative_path=${file#$DATA_DIR/}
            echo "   $relative_path"
        done
        local total_files=$(find "$DATA_DIR" -name "*.json" | wc -l)
        echo "   ... and $(($total_files - 10)) more files (total: $total_files)"
        return 0
    fi
    
    # Create upload metadata
    local metadata_file="/tmp/upload_metadata_${upload_id}.json"
    cat > "$metadata_file" << EOF
{
  "upload_id": "$upload_id",
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "tenant_id": "$tenant_id",
  "data_source": "synthea",
  "fhir_version": "R4",
  "environment": "$ENVIRONMENT",
  "bucket": "$BUCKET_NAME",
  "s3_prefix": "$upload_path",
  "generated_by": "$(whoami)@$(hostname)",
  "upload_statistics": {
    "total_files": $(find "$DATA_DIR" -name "*.json" | wc -l),
    "resource_types": $(find "$DATA_DIR" -maxdepth 1 -type d ! -path "$DATA_DIR" | wc -l)
  }
}
EOF
    
    echo "📁 Upload details:"
    echo "   Upload ID: $upload_id"
    echo "   S3 Path: s3://$BUCKET_NAME/$upload_path/"
    local json_files=$(find "$DATA_DIR" -name "*.json" 2>/dev/null | wc -l)
    local ndjson_files=$(find "$DATA_DIR" -name "*.ndjson" 2>/dev/null | wc -l)
    echo "   Total JSON files: $json_files"
    echo "   Total NDJSON files: $ndjson_files"
    
    # Upload metadata first
    echo "📋 Uploading metadata..."
    aws s3 cp "$metadata_file" "s3://$BUCKET_NAME/$upload_path/metadata.json" \
        --metadata "tenant-id=$tenant_id,data-source=synthea,fhir-version=R4,upload-id=$upload_id"
    
    # Upload FHIR data (NDJSON files and/or resource directories)
    echo "📤 Uploading FHIR resources..."
    
    # Upload NDJSON files directly if they exist
    if [ $ndjson_files -gt 0 ]; then
        echo "   Uploading NDJSON files: $ndjson_files files"
        for ndjson_file in "$DATA_DIR"/*.ndjson; do
            if [ -f "$ndjson_file" ]; then
                local filename=$(basename "$ndjson_file")
                echo "     Uploading $filename"
                aws s3 cp "$ndjson_file" "s3://$BUCKET_NAME/$upload_path/$filename" \
                    --metadata "tenant-id=$tenant_id,data-source=synthea,fhir-version=R4,file-type=ndjson"
            fi
        done
    fi
    
    # Upload resource directories if they exist
    for resource_dir in "$DATA_DIR"/*; do
        if [ -d "$resource_dir" ]; then
            local resource_type=$(basename "$resource_dir")
            local file_count=$(find "$resource_dir" -name "*.json" 2>/dev/null | wc -l)
            
            if [ $file_count -gt 0 ]; then
                echo "   Uploading $resource_type directory: $file_count files"
                
                aws s3 sync "$resource_dir" "s3://$BUCKET_NAME/$upload_path/$resource_type/" \
                    --metadata "tenant-id=$tenant_id,resource-type=$resource_type,data-source=synthea,fhir-version=R4" \
                    --include "*.json" \
                    --delete
            fi
        fi
    done
    
    # Upload summary file if it exists
    if [ -f "$DATA_DIR/data-summary.json" ]; then
        echo "📋 Uploading data summary..."
        aws s3 cp "$DATA_DIR/data-summary.json" "s3://$BUCKET_NAME/$upload_path/data-summary.json" \
            --metadata "tenant-id=$tenant_id,data-source=synthea,file-type=summary"
    fi
    
    # Clean up temporary metadata file
    rm -f "$metadata_file"
    
    echo "✅ Upload completed successfully"
    echo "   Upload ID: $upload_id"
    echo "   S3 Location: s3://$BUCKET_NAME/$upload_path/"
}

# Verify upload and show summary
verify_upload() {
    local tenant_id="$1"
    
    echo "🔍 Verifying upload..."
    
    # List uploaded files
    local file_count=$(aws s3 ls "s3://$BUCKET_NAME/$TENANT_PREFIX/" --recursive | wc -l)
    
    echo "✅ Upload verification:"
    echo "   Bucket: $BUCKET_NAME"
    echo "   Tenant folder: $TENANT_PREFIX"
    echo "   Total files in tenant folder: $file_count"
    
    # Show recent uploads
    echo "📁 Recent uploads for tenant $tenant_id:"
    aws s3 ls "s3://$BUCKET_NAME/$INGESTION_PREFIX/" | tail -5 | while read line; do
        echo "   $line"
    done
}

# Create HealthLake import instructions
create_import_instructions() {
    local tenant_id="$1"
    
    local instructions_file="$SCRIPT_DIR/healthlake-import-instructions.md"
    
    cat > "$instructions_file" << EOF
# HealthLake Import Instructions

## Upload Summary
- **Bucket**: \`$BUCKET_NAME\`
- **Tenant ID**: \`$tenant_id\`
- **Upload Path**: \`$INGESTION_PREFIX/\`
- **Data Format**: FHIR R4 JSON
- **Upload Date**: $(date -u +"%Y-%m-%d %H:%M:%S UTC")

## Next Steps

### 1. Start HealthLake Import Job

\`\`\`bash
# Get the latest upload folder
LATEST_UPLOAD=\$(aws s3 ls s3://$BUCKET_NAME/$INGESTION_PREFIX/ | tail -1 | awk '{print \$2}' | sed 's|/||')

# Start import job (replace DATASTORE_ID with actual HealthLake datastore ID)
aws healthlake start-fhir-import-job \\
    --input-data-config S3Uri="s3://$BUCKET_NAME/$INGESTION_PREFIX/\$LATEST_UPLOAD/" \\
    --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID" \\
    --data-access-role-arn "arn:aws:iam::$ACCOUNT_ID:role/HealthLakeServiceRole" \\
    --job-name "synthea-import-\$(date +%Y%m%d-%H%M%S)"
\`\`\`

### 2. Monitor Import Job

\`\`\`bash
# List import jobs
aws healthlake list-fhir-import-jobs --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID"

# Check specific job status
aws healthlake describe-fhir-import-job \\
    --datastore-id "YOUR_HEALTHLAKE_DATASTORE_ID" \\
    --job-id "JOB_ID_FROM_PREVIOUS_COMMAND"
\`\`\`

### 3. Query Data via Athena

Once imported, you can query the data through Athena using Lake Formation:

\`\`\`sql
-- List databases
SHOW DATABASES;

-- Use tenant-specific database
USE fhir_raw_${tenant_id//-/_}_$ENVIRONMENT;

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
\`\`\`

## Bucket Structure

\`\`\`
s3://$BUCKET_NAME/
├── tenants/
│   └── $tenant_id/
│       └── fhir/
│           ├── raw/           # Raw FHIR exports from HealthLake
│           └── ingestion/     # Incoming data for import
│               └── \${upload_id}/
│                   ├── metadata.json
│                   ├── data-summary.json
│                   ├── Patient/
│                   ├── Encounter/
│                   ├── Observation/
│                   └── ...
\`\`\`

## Lake Formation Configuration

The data is automatically tagged and organized for Lake Formation:
- **Tenant ID**: \`$tenant_id\`
- **Data Source**: \`Synthea\`
- **FHIR Version**: \`R4\`
- **Environment**: \`$ENVIRONMENT\`

EOF
    
    echo "📋 Import instructions created: $instructions_file"
}

# Display usage information
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Upload generated FHIR data to Lake Formation S3 bucket

Options:
  -e, --environment ENV    Environment (development/production, default: $DEFAULT_ENVIRONMENT)
  -t, --tenant-id ID       Tenant ID (default: $DEFAULT_TENANT_ID)
  -p, --profile PROFILE    AWS profile to use (default: $DEFAULT_AWS_PROFILE)
  --dry-run               Show what would be uploaded without actually uploading
  -h, --help              Show this help message

Examples:
  $0                                      # Upload to development with default tenant
  $0 -e production -t "20000000-0000-0000-0000-000000000002"  # Upload to production
  $0 --dry-run                           # Preview upload without executing
  $0 -p my-aws-profile                   # Use specific AWS profile

Prerequisites:
  1. Run ./generate-fhir-data.sh to generate sample data
  2. Ensure Lake Formation stack is deployed
  3. Configure AWS credentials with proper permissions
  4. Install jq for JSON parsing
EOF
}

# Parse command line arguments
parse_arguments() {
    ENVIRONMENT="$DEFAULT_ENVIRONMENT"
    TENANT_ID="$DEFAULT_TENANT_ID"
    AWS_PROFILE="$DEFAULT_AWS_PROFILE"
    DRY_RUN=false
    
    while [[ $# -gt 0 ]]; do
        case $1 in
            -e|--environment)
                ENVIRONMENT="$2"
                shift 2
                ;;
            -t|--tenant-id)
                TENANT_ID="$2"
                shift 2
                ;;
            -p|--profile)
                AWS_PROFILE="$2"
                shift 2
                ;;
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            *)
                echo "❌ Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
}

# Main execution
main() {
    echo "Starting FHIR data upload..."
    echo "Configuration:"
    echo "  Environment: $ENVIRONMENT"
    echo "  Tenant ID: $TENANT_ID"
    echo "  AWS Profile: $AWS_PROFILE"
    echo "  Dry Run: $DRY_RUN"
    echo ""
    
    check_prerequisites
    get_config "$ENVIRONMENT"
    verify_aws_access "$AWS_PROFILE"
    create_folder_structure "$TENANT_ID"
    upload_data "$TENANT_ID" "$DRY_RUN"
    
    if [ "$DRY_RUN" = false ]; then
        verify_upload "$TENANT_ID"
        create_import_instructions "$TENANT_ID"
        
        echo ""
        echo "🎉 FHIR data upload completed successfully!"
        echo ""
        echo "📁 Data Location: s3://$BUCKET_NAME/$TENANT_PREFIX/"
        echo "📋 Import Instructions: $SCRIPT_DIR/healthlake-import-instructions.md"
        echo ""
        echo "Next steps:"
        echo "1. Review import instructions in healthlake-import-instructions.md"
        echo "2. Start HealthLake import job using AWS CLI"
        echo "3. Query imported data via Athena through Lake Formation"
    else
        echo ""
        echo "🔍 Dry run completed. Use without --dry-run to perform actual upload."
    fi
}

# Execute main function with parsed arguments
parse_arguments "$@"
main