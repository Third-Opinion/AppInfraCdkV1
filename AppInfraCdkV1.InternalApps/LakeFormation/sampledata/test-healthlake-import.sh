#!/bin/bash

# test-healthlake-import.sh
# Script to test HealthLake import job from S3 with generated FHIR data
# Must be run after upload-to-s3.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="$SCRIPT_DIR/../lakeformation-config.json"

# Default configuration
DEFAULT_ENVIRONMENT="development"
DEFAULT_TENANT_ID="10000000-0000-0000-0000-000000000001"
DEFAULT_AWS_PROFILE="to-dev-admin"

echo "🏥 Testing HealthLake Import Job"
echo "==============================="

# Check prerequisites
check_prerequisites() {
    echo "🔍 Checking prerequisites..."
    
    # Check AWS CLI
    if ! command -v aws &> /dev/null; then
        echo "❌ AWS CLI is not installed."
        exit 1
    fi
    
    # Check jq
    if ! command -v jq &> /dev/null; then
        echo "❌ jq is not installed."
        exit 1
    fi
    
    # Check config file
    if [ ! -f "$CONFIG_FILE" ]; then
        echo "❌ Lake Formation config not found at $CONFIG_FILE"
        exit 1
    fi
    
    echo "✅ Prerequisites satisfied"
}

# Get configuration
get_config() {
    local environment="$1"
    local tenant_id="$2"
    
    echo "📋 Loading configuration..."
    
    # Extract configuration using jq
    BUCKET_PREFIX=$(jq -r ".environments.${environment}.bucketConfig.bucketPrefix" "$CONFIG_FILE")
    REGION=$(jq -r ".environments.${environment}.region" "$CONFIG_FILE")
    ACCOUNT_ID=$(jq -r ".environments.${environment}.accountId" "$CONFIG_FILE")
    
    # Get HealthLake datastore info for this tenant
    DATASTORE_ID=$(jq -r ".environments.${environment}.healthLake[] | select(.tenantId == \"$tenant_id\") | .datastoreId" "$CONFIG_FILE")
    
    if [ "$BUCKET_PREFIX" = "null" ] || [ "$REGION" = "null" ] || [ "$ACCOUNT_ID" = "null" ]; then
        echo "❌ Invalid configuration for environment: $environment"
        exit 1
    fi
    
    if [ "$DATASTORE_ID" = "null" ] || [ -z "$DATASTORE_ID" ]; then
        echo "❌ No HealthLake datastore found for tenant: $tenant_id"
        exit 1
    fi
    
    # Construct bucket name
    BUCKET_NAME="${BUCKET_PREFIX}-raw-${environment}-${REGION}"
    
    echo "✅ Configuration loaded:"
    echo "   Bucket: $BUCKET_NAME"
    echo "   Region: $REGION"
    echo "   Account: $ACCOUNT_ID"
    echo "   Datastore ID: $DATASTORE_ID"
    echo "   Tenant ID: $tenant_id"
}

# Find latest upload for tenant
find_latest_upload() {
    local tenant_id="$1"
    
    echo "🔍 Finding latest upload for tenant: $tenant_id"
    
    local tenant_prefix="tenants/${tenant_id}/fhir/ingestion"
    
    # List uploads and get the latest one
    LATEST_UPLOAD=$(aws s3 ls "s3://$BUCKET_NAME/$tenant_prefix/" | \
        grep -E "[0-9]{8}_[0-9]{6}_synthea" | \
        sort -k2 | \
        tail -1 | \
        awk '{print $2}' | \
        sed 's|/||')
    
    if [ -z "$LATEST_UPLOAD" ]; then
        echo "❌ No Synthea uploads found for tenant: $tenant_id"
        echo "   Please run './upload-to-s3.sh' first"
        exit 1
    fi
    
    S3_INPUT_URI="s3://$BUCKET_NAME/$tenant_prefix/$LATEST_UPLOAD/"
    
    echo "✅ Latest upload found:"
    echo "   Upload ID: $LATEST_UPLOAD"
    echo "   S3 URI: $S3_INPUT_URI"
}

# Verify HealthLake datastore exists and is active
verify_datastore() {
    echo "🏥 Verifying HealthLake datastore..."
    
    local datastore_info
    datastore_info=$(aws healthlake describe-fhir-datastore \
        --datastore-id "$DATASTORE_ID" \
        --region "$REGION" 2>/dev/null || echo "")
    
    if [ -z "$datastore_info" ]; then
        echo "❌ HealthLake datastore not found: $DATASTORE_ID"
        echo "   Please ensure HealthLake stack is deployed"
        exit 1
    fi
    
    local status
    status=$(echo "$datastore_info" | jq -r '.DatastoreProperties.DatastoreStatus')
    
    if [ "$status" != "ACTIVE" ]; then
        echo "❌ HealthLake datastore is not active: $status"
        echo "   Please wait for datastore to become active"
        exit 1
    fi
    
    local datastore_name
    datastore_name=$(echo "$datastore_info" | jq -r '.DatastoreProperties.DatastoreName')
    
    echo "✅ HealthLake datastore verified:"
    echo "   ID: $DATASTORE_ID"
    echo "   Name: $datastore_name"
    echo "   Status: $status"
}

# Get or create HealthLake service role
get_service_role() {
    echo "🔐 Getting HealthLake service role..."
    
    # Look for tenant-specific HealthLake role first
    local tenant_num="${TENANT_ID:0:8}"
    # Capitalize first letter of environment
    local env_cap="$(echo "${ENVIRONMENT:0:1}" | tr '[:lower:]' '[:upper:]')${ENVIRONMENT:1}"
    local role_name="HealthLakeRole-${tenant_num}-${env_cap}"
    SERVICE_ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${role_name}"
    
    # Check if role exists
    if aws iam get-role --role-name "$role_name" >/dev/null 2>&1; then
        echo "✅ HealthLake service role exists: $SERVICE_ROLE_ARN"
    else
        # Try generic HealthLake service role
        role_name="HealthLakeServiceRole"
        SERVICE_ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${role_name}"
        
        if aws iam get-role --role-name "$role_name" >/dev/null 2>&1; then
            echo "✅ HealthLake service role exists: $SERVICE_ROLE_ARN"
        else
            echo "⚠️  HealthLake service role not found"
            echo "   Expected role: HealthLakeRole-${tenant_num}-${env_cap}"
            echo "   Or generic role: HealthLakeServiceRole"
            echo "   Please ensure the Lake Formation setup stack is deployed"
            exit 1
        fi
    fi
}

# Start HealthLake import job
start_import_job() {
    local dry_run="$1"
    
    echo "🚀 Starting HealthLake import job..."
    
    local job_name="synthea-import-$(date +%Y%m%d-%H%M%S)"
    
    if [ "$dry_run" = true ]; then
        echo "🔍 DRY RUN - Would start import job:"
        echo "   Job Name: $job_name"
        echo "   Datastore ID: $DATASTORE_ID"
        echo "   Input S3 URI: $S3_INPUT_URI"
        echo "   Service Role: $SERVICE_ROLE_ARN"
        return 0
    fi
    
    echo "📤 Starting import with:"
    echo "   Job Name: $job_name"
    echo "   S3 URI: $S3_INPUT_URI"
    
    # Create output S3 path for import results
    local output_prefix="tenants/${TENANT_ID}/fhir/import-results/$(date +%Y%m%d)"
    local output_uri="s3://$BUCKET_NAME/$output_prefix/"
    
    # Get the AWS managed S3 KMS key ARN
    local kms_key_arn
    kms_key_arn=$(aws kms describe-key --key-id alias/aws/s3 --query 'KeyMetadata.Arn' --output text --region "$REGION")
    
    local import_result
    import_result=$(aws healthlake start-fhir-import-job \
        --input-data-config "S3Uri=$S3_INPUT_URI" \
        --job-output-data-config "S3Configuration={S3Uri=$output_uri,KmsKeyId=$kms_key_arn}" \
        --datastore-id "$DATASTORE_ID" \
        --data-access-role-arn "$SERVICE_ROLE_ARN" \
        --job-name "$job_name" \
        --region "$REGION" \
        --output json)
    
    if [ $? -eq 0 ]; then
        JOB_ID=$(echo "$import_result" | jq -r '.JobId')
        echo "✅ Import job started successfully:"
        echo "   Job ID: $JOB_ID"
        echo "   Job Name: $job_name"
        
        # Save job info for monitoring
        echo "$import_result" | jq . > "/tmp/healthlake_import_${JOB_ID}.json"
        echo "📋 Job details saved to: /tmp/healthlake_import_${JOB_ID}.json"
    else
        echo "❌ Failed to start import job"
        exit 1
    fi
}

# Monitor import job status
monitor_import_job() {
    local job_id="$1"
    local timeout_minutes="${2:-30}"
    
    if [ -z "$job_id" ]; then
        echo "❌ No job ID provided for monitoring"
        return 1
    fi
    
    echo "⏱️  Monitoring import job: $job_id"
    echo "   Timeout: $timeout_minutes minutes"
    echo "   Use Ctrl+C to stop monitoring (job will continue)"
    
    local start_time=$(date +%s)
    local timeout_seconds=$((timeout_minutes * 60))
    
    while true; do
        local current_time=$(date +%s)
        local elapsed=$((current_time - start_time))
        
        if [ $elapsed -gt $timeout_seconds ]; then
            echo "⏰ Monitoring timeout reached ($timeout_minutes minutes)"
            echo "   Job is still running. Check status manually with:"
            echo "   aws healthlake describe-fhir-import-job --datastore-id $DATASTORE_ID --job-id $job_id"
            break
        fi
        
        local job_status
        job_status=$(aws healthlake describe-fhir-import-job \
            --datastore-id "$DATASTORE_ID" \
            --job-id "$job_id" \
            --region "$REGION" \
            --output json 2>/dev/null || echo "{}")
        
        if [ "$job_status" = "{}" ]; then
            echo "❌ Failed to get job status"
            break
        fi
        
        local status
        local progress
        status=$(echo "$job_status" | jq -r '.ImportJobProperties.JobStatus')
        progress=$(echo "$job_status" | jq -r '.ImportJobProperties.JobProgressReport // {}')
        
        local elapsed_formatted=$(printf "%02d:%02d" $((elapsed / 60)) $((elapsed % 60)))
        echo "[$elapsed_formatted] Status: $status"
        
        if [ "$progress" != "{}" ] && [ "$progress" != "null" ]; then
            local total=$(echo "$progress" | jq -r '.TotalNumberOfScannedFiles // 0')
            local imported=$(echo "$progress" | jq -r '.TotalNumberOfImportedResources // 0')
            if [ "$total" != "0" ] || [ "$imported" != "0" ]; then
                echo "   Progress: $imported resources imported from $total files scanned"
            fi
        fi
        
        case "$status" in
            "COMPLETED")
                echo "✅ Import job completed successfully!"
                local stats=$(echo "$job_status" | jq -r '.ImportJobProperties.JobProgressReport')
                echo "📊 Final Statistics:"
                echo "$stats" | jq .
                break
                ;;
            "FAILED")
                echo "❌ Import job failed"
                local message=$(echo "$job_status" | jq -r '.ImportJobProperties.Message // "No error message"')
                echo "   Error: $message"
                break
                ;;
            "CANCELLED")
                echo "⚠️  Import job was cancelled"
                break
                ;;
            "SUBMITTED"|"IN_PROGRESS")
                # Continue monitoring
                ;;
            *)
                echo "❓ Unknown status: $status"
                ;;
        esac
        
        sleep 30
    done
}

# List recent import jobs
list_recent_jobs() {
    echo "📋 Recent HealthLake import jobs:"
    
    aws healthlake list-fhir-import-jobs \
        --datastore-id "$DATASTORE_ID" \
        --region "$REGION" \
        --max-results 10 \
        --output table \
        --query 'ImportJobPropertiesList[*].[JobId,JobName,JobStatus,SubmitTime]'
}

# Display usage information
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Test HealthLake import job from S3 with generated FHIR data

Options:
  -e, --environment ENV    Environment (development/production, default: $DEFAULT_ENVIRONMENT)
  -t, --tenant-id ID       Tenant ID (default: $DEFAULT_TENANT_ID)
  -p, --profile PROFILE    AWS profile to use (default: $DEFAULT_AWS_PROFILE)
  -j, --job-id JOB_ID     Monitor existing job instead of starting new one
  -m, --monitor-timeout MIN  Monitoring timeout in minutes (default: 30)
  --list-jobs             List recent import jobs and exit
  --dry-run               Show what would be done without starting job
  -h, --help              Show this help message

Examples:
  $0                                    # Start import for default tenant
  $0 --dry-run                         # Preview import without starting
  $0 -j JOB_ID                        # Monitor existing job
  $0 --list-jobs                      # List recent jobs
  $0 -e production -t "20000000-0000-0000-0000-000000000002"  # Production import

Prerequisites:
  1. Lake Formation stack deployed with HealthLake datastore
  2. FHIR data uploaded to S3 using upload-to-s3.sh
  3. AWS credentials with HealthLake permissions
  4. HealthLake service role configured
EOF
}

# Parse command line arguments
parse_arguments() {
    ENVIRONMENT="$DEFAULT_ENVIRONMENT"
    TENANT_ID="$DEFAULT_TENANT_ID"
    AWS_PROFILE="$DEFAULT_AWS_PROFILE"
    JOB_ID=""
    MONITOR_TIMEOUT=30
    DRY_RUN=false
    LIST_JOBS=false
    
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
            -j|--job-id)
                JOB_ID="$2"
                shift 2
                ;;
            -m|--monitor-timeout)
                MONITOR_TIMEOUT="$2"
                shift 2
                ;;
            --list-jobs)
                LIST_JOBS=true
                shift
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
    echo "Configuration:"
    echo "  Environment: $ENVIRONMENT"
    echo "  Tenant ID: $TENANT_ID"
    echo "  AWS Profile: $AWS_PROFILE"
    if [ -n "$JOB_ID" ]; then
        echo "  Monitor Job ID: $JOB_ID"
    fi
    echo ""
    
    # Set AWS profile
    if [ -n "$AWS_PROFILE" ]; then
        export AWS_PROFILE="$AWS_PROFILE"
    fi
    
    check_prerequisites
    get_config "$ENVIRONMENT" "$TENANT_ID"
    verify_datastore
    
    if [ "$LIST_JOBS" = true ]; then
        list_recent_jobs
        exit 0
    fi
    
    if [ -n "$JOB_ID" ]; then
        # Monitor existing job
        monitor_import_job "$JOB_ID" "$MONITOR_TIMEOUT"
    else
        # Start new job
        find_latest_upload "$TENANT_ID"
        get_service_role
        start_import_job "$DRY_RUN"
        
        if [ "$DRY_RUN" = false ] && [ -n "$JOB_ID" ]; then
            echo ""
            echo "🔍 Starting monitoring (use Ctrl+C to stop monitoring)..."
            sleep 5
            monitor_import_job "$JOB_ID" "$MONITOR_TIMEOUT"
        fi
    fi
    
    if [ "$DRY_RUN" = false ]; then
        echo ""
        echo "🎉 HealthLake import test completed!"
        echo ""
        echo "Next steps:"
        echo "1. Verify data appears in HealthLake console"
        echo "2. Test Athena queries using test-athena-queries.sql"
        echo "3. Validate Lake Formation permissions and tenant isolation"
    fi
}

# Execute main function with parsed arguments
parse_arguments "$@"
main