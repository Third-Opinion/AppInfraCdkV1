#!/bin/bash

# auto-healthlake-import.sh
# Automated HealthLake import script for test-dev-healthlake-store
# Automatically finds latest upload and starts import job

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="$SCRIPT_DIR/../lakeformation-config.json"

# Configuration from lakeformation-config.json
ENVIRONMENT="development"
TENANT_ID="10000000-0000-0000-0000-000000000001"
DATASTORE_ID="test-dev-healthlake-store"
AWS_PROFILE="to-dev-admin"
REGION="us-east-2"
ACCOUNT_ID="615299752206"
BUCKET_NAME="thirdopinion-raw-development-us-east-2"

echo "🏥 Automated HealthLake Import"
echo "============================="
echo "Datastore: $DATASTORE_ID"
echo "Tenant: $TENANT_ID"
echo "Environment: $ENVIRONMENT"
echo ""

# Set AWS profile
export AWS_PROFILE="$AWS_PROFILE"

# Check prerequisites
check_prerequisites() {
    echo "🔍 Checking prerequisites..."
    
    if ! command -v aws &> /dev/null; then
        echo "❌ AWS CLI not installed"
        exit 1
    fi
    
    if ! command -v jq &> /dev/null; then
        echo "❌ jq not installed"
        exit 1
    fi
    
    # Test AWS access
    local current_account
    current_account=$(aws sts get-caller-identity --query "Account" --output text 2>/dev/null || echo "")
    
    if [ "$current_account" != "$ACCOUNT_ID" ]; then
        echo "❌ AWS credentials issue. Expected account: $ACCOUNT_ID, got: $current_account"
        echo "   Try: aws sso login --profile $AWS_PROFILE"
        exit 1
    fi
    
    echo "✅ Prerequisites satisfied"
}

# Find latest upload
find_latest_upload() {
    echo "🔍 Finding latest FHIR data upload..."
    
    local tenant_prefix="tenants/${TENANT_ID}/fhir/ingestion"
    
    # Get latest upload folder
    LATEST_UPLOAD=$(aws s3 ls "s3://$BUCKET_NAME/$tenant_prefix/" | \
        grep -E "[0-9]{8}_[0-9]{6}_synthea" | \
        sort -k2 | \
        tail -1 | \
        awk '{print $2}' | \
        sed 's|/||')
    
    if [ -z "$LATEST_UPLOAD" ]; then
        echo "❌ No Synthea uploads found"
        echo "   Please run './upload-to-s3.sh' first"
        exit 1
    fi
    
    S3_INPUT_URI="s3://$BUCKET_NAME/$tenant_prefix/$LATEST_UPLOAD/"
    
    echo "✅ Found latest upload:"
    echo "   Upload ID: $LATEST_UPLOAD"
    echo "   S3 URI: $S3_INPUT_URI"
    
    # List files in upload
    echo "📁 Files in upload:"
    aws s3 ls "$S3_INPUT_URI" --recursive | head -10 | while read -r line; do
        echo "   $(echo "$line" | awk '{print $4}')"
    done
    
    local total_files
    total_files=$(aws s3 ls "$S3_INPUT_URI" --recursive | wc -l)
    echo "   Total files: $total_files"
}

# Verify HealthLake datastore
verify_datastore() {
    echo "🏥 Verifying HealthLake datastore..."
    
    local datastore_info
    datastore_info=$(aws healthlake describe-fhir-datastore \
        --datastore-id "$DATASTORE_ID" \
        --region "$REGION" 2>/dev/null || echo "")
    
    if [ -z "$datastore_info" ]; then
        echo "❌ HealthLake datastore not found: $DATASTORE_ID"
        echo "   Ensure Lake Formation HealthLake stack is deployed"
        exit 1
    fi
    
    local status
    status=$(echo "$datastore_info" | jq -r '.DatastoreProperties.DatastoreStatus')
    
    if [ "$status" != "ACTIVE" ]; then
        echo "❌ HealthLake datastore not active: $status"
        echo "   Wait for datastore to become active"
        exit 1
    fi
    
    local datastore_name
    datastore_name=$(echo "$datastore_info" | jq -r '.DatastoreProperties.DatastoreName')
    
    echo "✅ HealthLake datastore verified:"
    echo "   ID: $DATASTORE_ID"
    echo "   Name: $datastore_name"
    echo "   Status: $status"
}

# Get service role ARN
get_service_role() {
    echo "🔐 Getting HealthLake service role..."
    
    # Try to find the Lake Formation service role
    local role_names=("HealthLakeServiceRole" "LakeFormationServiceRole" "dev-lf-setup-ue2-LakeFormationServiceRole")
    
    for role_name in "${role_names[@]}"; do
        if aws iam get-role --role-name "$role_name" >/dev/null 2>&1; then
            SERVICE_ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${role_name}"
            echo "✅ Found service role: $SERVICE_ROLE_ARN"
            return 0
        fi
    done
    
    echo "❌ No suitable service role found"
    echo "   Expected roles: ${role_names[*]}"
    echo "   Ensure Lake Formation setup stack is deployed"
    exit 1
}

# Start import job
start_import_job() {
    echo "🚀 Starting HealthLake import job..."
    
    local job_name="auto-import-$(date +%Y%m%d-%H%M%S)"
    
    echo "📤 Import details:"
    echo "   Job Name: $job_name"
    echo "   Datastore: $DATASTORE_ID"
    echo "   S3 URI: $S3_INPUT_URI"
    echo "   Service Role: $SERVICE_ROLE_ARN"
    
    local import_result
    import_result=$(aws healthlake start-fhir-import-job \
        --input-data-config "S3Uri=$S3_INPUT_URI" \
        --datastore-id "$DATASTORE_ID" \
        --data-access-role-arn "$SERVICE_ROLE_ARN" \
        --job-name "$job_name" \
        --region "$REGION" \
        --output json 2>&1)
    
    if [ $? -eq 0 ]; then
        JOB_ID=$(echo "$import_result" | jq -r '.JobId')
        echo "✅ Import job started successfully:"
        echo "   Job ID: $JOB_ID"
        echo "   Job Name: $job_name"
        
        # Save job info
        echo "$import_result" | jq . > "/tmp/healthlake_import_${JOB_ID}.json"
        echo "📋 Job details saved to: /tmp/healthlake_import_${JOB_ID}.json"
        
        return 0
    else
        echo "❌ Failed to start import job:"
        echo "$import_result"
        
        # Check common issues
        if echo "$import_result" | grep -q "AccessDenied"; then
            echo ""
            echo "💡 Troubleshooting tips:"
            echo "   1. Ensure service role has HealthLake permissions"
            echo "   2. Check S3 bucket access permissions"
            echo "   3. Verify datastore is in ACTIVE state"
        fi
        
        exit 1
    fi
}

# Monitor import job
monitor_import_job() {
    local job_id="$1"
    
    if [ -z "$job_id" ]; then
        echo "❌ No job ID to monitor"
        return 1
    fi
    
    echo ""
    echo "⏱️  Monitoring import job: $job_id"
    echo "   Use Ctrl+C to stop monitoring (job will continue)"
    echo ""
    
    local start_time=$(date +%s)
    local last_status=""
    
    while true; do
        local current_time=$(date +%s)
        local elapsed=$((current_time - start_time))
        
        # Get job status
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
        local message
        local progress
        status=$(echo "$job_status" | jq -r '.ImportJobProperties.JobStatus')
        message=$(echo "$job_status" | jq -r '.ImportJobProperties.Message // ""')
        progress=$(echo "$job_status" | jq -r '.ImportJobProperties.JobProgressReport // {}')
        
        # Only show updates when status changes or every 30 seconds
        if [ "$status" != "$last_status" ] || [ $((elapsed % 30)) -eq 0 ]; then
            local elapsed_formatted=$(printf "%02d:%02d" $((elapsed / 60)) $((elapsed % 60)))
            echo "[$elapsed_formatted] Status: $status"
            
            if [ "$progress" != "{}" ] && [ "$progress" != "null" ]; then
                local scanned=$(echo "$progress" | jq -r '.TotalNumberOfScannedFiles // 0')
                local imported=$(echo "$progress" | jq -r '.TotalNumberOfImportedResources // 0')
                local failed=$(echo "$progress" | jq -r '.TotalNumberOfResourcesWithCustomerError // 0')
                
                if [ "$scanned" != "0" ] || [ "$imported" != "0" ]; then
                    echo "   Progress: $imported resources imported, $failed errors, $scanned files scanned"
                fi
            fi
            
            if [ -n "$message" ] && [ "$message" != "null" ]; then
                echo "   Message: $message"
            fi
            
            last_status="$status"
        fi
        
        case "$status" in
            "COMPLETED")
                echo ""
                echo "🎉 Import job completed successfully!"
                echo ""
                echo "📊 Final Statistics:"
                echo "$progress" | jq .
                break
                ;;
            "FAILED")
                echo ""
                echo "❌ Import job failed"
                if [ -n "$message" ] && [ "$message" != "null" ]; then
                    echo "   Error: $message"
                fi
                break
                ;;
            "CANCELLED")
                echo ""
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
        
        sleep 10
    done
}

# Show next steps
show_next_steps() {
    echo ""
    echo "🎯 Next Steps:"
    echo "============="
    echo ""
    echo "1. 🔍 Verify data in HealthLake console:"
    echo "   https://console.aws.amazon.com/healthlake/home?region=$REGION#/datastores/$DATASTORE_ID"
    echo ""
    echo "2. 📊 Query data with Athena:"
    echo "   Use queries from: test-athena-queries.sql"
    echo "   Database: fhir_raw_${TENANT_ID//-/_}_development"
    echo ""
    echo "3. 🔐 Test Lake Formation permissions:"
    echo "   Verify tenant isolation works correctly"
    echo ""
    echo "4. 📈 Performance testing:"
    echo "   Run larger dataset imports and complex queries"
}

# Show usage
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Automated HealthLake import for test-dev-healthlake-store

Options:
  --no-monitor     Start import job but don't monitor progress
  --status-only    Show current datastore status and recent jobs
  --help           Show this help message

Examples:
  $0                    # Full automated import with monitoring
  $0 --no-monitor      # Start import and exit
  $0 --status-only     # Check datastore status

This script will:
1. Find the latest FHIR data upload in S3
2. Verify HealthLake datastore is ready
3. Start an import job automatically
4. Monitor progress until completion
EOF
}

# Parse command line arguments
parse_arguments() {
    NO_MONITOR=false
    STATUS_ONLY=false
    
    while [[ $# -gt 0 ]]; do
        case $1 in
            --no-monitor)
                NO_MONITOR=true
                shift
                ;;
            --status-only)
                STATUS_ONLY=true
                shift
                ;;
            --help)
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
    if [ "$STATUS_ONLY" = true ]; then
        check_prerequisites
        verify_datastore
        
        echo ""
        echo "📋 Recent import jobs:"
        aws healthlake list-fhir-import-jobs \
            --datastore-id "$DATASTORE_ID" \
            --region "$REGION" \
            --max-results 5 \
            --output table \
            --query 'ImportJobPropertiesList[*].[JobId,JobName,JobStatus,SubmitTime]'
        
        exit 0
    fi
    
    echo "Starting automated HealthLake import..."
    echo ""
    
    check_prerequisites
    find_latest_upload
    verify_datastore
    get_service_role
    start_import_job
    
    if [ "$NO_MONITOR" = false ] && [ -n "$JOB_ID" ]; then
        monitor_import_job "$JOB_ID"
        show_next_steps
    else
        echo ""
        echo "✅ Import job started: $JOB_ID"
        echo "   Monitor with: ./test-healthlake-import.sh -j $JOB_ID"
    fi
}

# Execute with parsed arguments
parse_arguments "$@"
main