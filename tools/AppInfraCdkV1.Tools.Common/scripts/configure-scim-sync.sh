#!/bin/bash

# =================================================================
# SCIM Sync Configuration Script for Google Workspace to AWS Identity Center
# =================================================================
#
# Description:
#   Configures and manages the SCIM synchronization after CDK deployment.
#   The stack deployment is now handled by the main CDK deployment program.
#
# Usage:
#   # First deploy the stack using CDK:
#   cd AppInfraCdkV1.Deploy
#   dotnet run -- --app=ScimSync --environment=Development
#   cdk deploy
#   
#   # Then configure with this script:
#   ./configure-scim-sync.sh <environment> [action]
#
# Arguments:
#   environment: dev, staging, or prod
#   action: configure, test, show-outputs, disable, enable (default: configure)
#
# Examples:
#   ./configure-scim-sync.sh dev             # Configure SSM parameters
#   ./configure-scim-sync.sh prod configure  # Configure production
#   ./configure-scim-sync.sh dev test        # Test the deployment
#   ./configure-scim-sync.sh staging disable # Disable sync temporarily
#
# Prerequisites:
#   - AWS CLI configured with appropriate profiles
#   - SCIM Sync stack already deployed via CDK
#   - Google Workspace service account JSON key
#   - AWS Identity Center SCIM endpoint and token
#
# =================================================================

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../../.." && pwd )"

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check prerequisites
check_prerequisites() {
    print_status "Checking prerequisites..."
    
    # Check AWS CLI
    if ! command -v aws &> /dev/null; then
        print_error "AWS CLI is not installed"
        exit 1
    fi
    
    # Check jq for JSON processing
    if ! command -v jq &> /dev/null; then
        print_warning "jq is not installed. Installing may be required for some features."
    fi
    
    print_success "Prerequisites checked"
}

# Function to validate environment and set AWS profile
validate_environment() {
    local env=$1
    case $env in
        prod|production)
            ENVIRONMENT="Production"
            AWS_PROFILE="to-prd-admin"
            ACCOUNT_ID="442042533707"
            STACK_NAME="prod-scim-sync-ue2"
            ;;
        *)
            print_error "Invalid environment: $env"
            echo ""
            echo "SCIM Sync is only deployed in Production environment."
            echo "This service syncs Google Workspace users/groups to AWS Identity Center."
            echo ""
            echo "Valid environment: prod (production)"
            echo ""
            echo "Usage: $0 prod [action]"
            exit 1
            ;;
    esac
    
    print_status "Environment: $ENVIRONMENT"
    print_status "AWS Profile: $AWS_PROFILE"
    print_status "Account ID: $ACCOUNT_ID"
    print_status "Stack Name: $STACK_NAME"
    print_warning "SCIM Sync runs ONLY in Production to sync Google Workspace to AWS Identity Center"
}

# Function to check if stack is deployed
check_stack_deployed() {
    print_status "Checking if SCIM Sync stack is deployed..."
    
    if aws cloudformation describe-stacks \
        --stack-name $STACK_NAME \
        --profile $AWS_PROFILE \
        --region us-east-2 &>/dev/null; then
        print_success "Stack $STACK_NAME is deployed"
        return 0
    else
        print_error "Stack $STACK_NAME is not deployed"
        echo ""
        echo "Please deploy the stack first using:"
        echo "  cd $PROJECT_ROOT/AppInfraCdkV1.Deploy"
        echo "  dotnet run -- --app=ScimSync --environment=$ENVIRONMENT"
        echo "  AWS_PROFILE=$AWS_PROFILE cdk deploy"
        return 1
    fi
}

# Function to configure SSM parameters
configure_parameters() {
    print_status "Configuring SCIM Sync parameters in SSM Parameter Store..."
    
    local param_prefix="/scim-sync/$(echo ${ENVIRONMENT} | tr '[:upper:]' '[:lower:]')"
    
    # Prompt for Google Workspace configuration
    echo ""
    print_status "Google Workspace Configuration"
    read -p "Enter Google Workspace domain (e.g., example.com): " google_domain
    read -p "Enter path to Google service account JSON key file: " service_account_file
    
    # Validate service account file
    if [ ! -f "$service_account_file" ]; then
        print_error "Service account file not found: $service_account_file"
        exit 1
    fi
    
    # Read and validate service account JSON
    if command -v jq &> /dev/null; then
        service_account_json=$(cat "$service_account_file" | jq -c '.')
        if [ $? -ne 0 ]; then
            print_error "Invalid JSON in service account file"
            exit 1
        fi
    else
        service_account_json=$(cat "$service_account_file")
    fi
    
    # Prompt for AWS Identity Center configuration
    echo ""
    print_status "AWS Identity Center Configuration"
    read -p "Enter AWS Identity Center SCIM endpoint URL: " scim_endpoint
    read -s -p "Enter AWS Identity Center SCIM access token: " scim_token
    echo ""
    
    # Prompt for sync configuration
    echo ""
    print_status "Synchronization Configuration"
    read -p "Enter group filter regex (default: .*): " group_filter
    group_filter=${group_filter:-".*"}
    
    read -p "Enter sync frequency in minutes (default: 30): " sync_frequency
    sync_frequency=${sync_frequency:-"30"}
    
    read -p "Enable sync immediately? (y/n, default: y): " enable_sync
    enable_sync=${enable_sync:-"y"}
    sync_enabled="false"
    if [ "$enable_sync" = "y" ]; then
        sync_enabled="true"
    fi
    
    # Update SSM parameters
    print_status "Updating SSM parameters..."
    
    aws ssm put-parameter \
        --name "${param_prefix}/google/domain" \
        --value "$google_domain" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "Google Workspace domain"
    
    aws ssm put-parameter \
        --name "${param_prefix}/google/service-account-key" \
        --value "$service_account_json" \
        --type "SecureString" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "Google Workspace service account key (encrypted)"
    
    aws ssm put-parameter \
        --name "${param_prefix}/aws/identity-center-scim-endpoint" \
        --value "$scim_endpoint" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "AWS Identity Center SCIM endpoint URL"
    
    aws ssm put-parameter \
        --name "${param_prefix}/aws/identity-center-scim-token" \
        --value "$scim_token" \
        --type "SecureString" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "AWS Identity Center SCIM access token (encrypted)"
    
    aws ssm put-parameter \
        --name "${param_prefix}/sync/group-filters" \
        --value "$group_filter" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "Regular expression for filtering groups to sync"
    
    aws ssm put-parameter \
        --name "${param_prefix}/sync/frequency-minutes" \
        --value "$sync_frequency" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "Sync frequency in minutes"
    
    aws ssm put-parameter \
        --name "${param_prefix}/sync/enabled" \
        --value "$sync_enabled" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE \
        --description "Enable/disable sync flag"
    
    print_success "Configuration updated successfully"
    
    if [ "$sync_enabled" = "true" ]; then
        print_status "Sync is enabled and will run every $sync_frequency minutes"
    else
        print_warning "Sync is currently disabled. Run '$0 $1 enable' to enable it"
    fi
}

# Function to test the deployment
test_deployment() {
    print_status "Testing SCIM Sync deployment..."
    
    # Get Lambda function name from stack outputs
    local function_name=$(aws cloudformation describe-stacks \
        --stack-name $STACK_NAME \
        --query "Stacks[0].Outputs[?OutputKey=='ScimSyncFunctionName'].OutputValue" \
        --output text \
        --profile $AWS_PROFILE)
    
    if [ -z "$function_name" ]; then
        print_error "Could not find Lambda function name. Is the stack deployed?"
        exit 1
    fi
    
    print_status "Found Lambda function: $function_name"
    
    # Invoke Lambda function for test
    print_status "Invoking Lambda function for test sync..."
    
    local payload='{"source":"manual","action":"test-sync","environment":"'$ENVIRONMENT'"}'
    local output_file="/tmp/scim-sync-test-$(date +%s).json"
    
    aws lambda invoke \
        --function-name $function_name \
        --payload "$payload" \
        --profile $AWS_PROFILE \
        $output_file
    
    # Check the output
    if [ -f "$output_file" ]; then
        print_status "Lambda response:"
        if command -v jq &> /dev/null; then
            cat "$output_file" | jq '.'
        else
            cat "$output_file"
        fi
        rm "$output_file"
    fi
    
    # Show recent logs
    print_status "Fetching recent CloudWatch logs..."
    local log_group="/aws/lambda/${function_name}"
    
    aws logs tail "$log_group" \
        --since 5m \
        --profile $AWS_PROFILE \
        --format short || print_warning "Could not fetch logs"
    
    print_success "Test completed"
}

# Function to show stack outputs
show_outputs() {
    print_status "Stack outputs for $STACK_NAME:"
    
    aws cloudformation describe-stacks \
        --stack-name $STACK_NAME \
        --query "Stacks[0].Outputs[*].[OutputKey,OutputValue,Description]" \
        --output table \
        --profile $AWS_PROFILE
}

# Function to enable sync
enable_sync() {
    print_status "Enabling SCIM synchronization..."
    
    local param_prefix="/scim-sync/$(echo ${ENVIRONMENT} | tr '[:upper:]' '[:lower:]')"
    
    aws ssm put-parameter \
        --name "${param_prefix}/sync/enabled" \
        --value "true" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE
    
    # Also enable the EventBridge rule
    local rule_name=$(aws cloudformation describe-stacks \
        --stack-name $STACK_NAME \
        --query "Stacks[0].Outputs[?OutputKey=='SyncScheduleRuleName'].OutputValue" \
        --output text \
        --profile $AWS_PROFILE)
    
    if [ -n "$rule_name" ]; then
        aws events enable-rule \
            --name "$rule_name" \
            --profile $AWS_PROFILE
        print_success "EventBridge rule enabled: $rule_name"
    fi
    
    print_success "SCIM synchronization enabled"
}

# Function to disable sync
disable_sync() {
    print_status "Disabling SCIM synchronization..."
    
    local param_prefix="/scim-sync/$(echo ${ENVIRONMENT} | tr '[:upper:]' '[:lower:]')"
    
    aws ssm put-parameter \
        --name "${param_prefix}/sync/enabled" \
        --value "false" \
        --type "String" \
        --overwrite \
        --profile $AWS_PROFILE
    
    # Also disable the EventBridge rule
    local rule_name=$(aws cloudformation describe-stacks \
        --stack-name $STACK_NAME \
        --query "Stacks[0].Outputs[?OutputKey=='SyncScheduleRuleName'].OutputValue" \
        --output text \
        --profile $AWS_PROFILE)
    
    if [ -n "$rule_name" ]; then
        aws events disable-rule \
            --name "$rule_name" \
            --profile $AWS_PROFILE
        print_success "EventBridge rule disabled: $rule_name"
    fi
    
    print_success "SCIM synchronization disabled"
}

# Function to show deployment instructions
show_deployment_instructions() {
    echo ""
    print_status "SCIM Sync Stack Deployment Instructions"
    echo "========================================="
    echo ""
    echo "1. Deploy the stack using CDK:"
    echo "   cd $PROJECT_ROOT/AppInfraCdkV1.Deploy"
    echo "   dotnet run -- --app=ScimSync --environment=$ENVIRONMENT"
    echo "   AWS_PROFILE=$AWS_PROFILE cdk deploy"
    echo ""
    echo "2. Configure SSM parameters:"
    echo "   $0 $1 configure"
    echo ""
    echo "3. Test the deployment:"
    echo "   $0 $1 test"
    echo ""
    echo "4. Monitor logs:"
    echo "   aws logs tail /aws/lambda/\$FUNCTION_NAME --follow --profile $AWS_PROFILE"
    echo ""
}

# Main script execution
main() {
    print_status "SCIM Sync Configuration Script"
    print_status "==============================="
    
    # Check if environment is provided
    if [ $# -lt 1 ]; then
        print_error "Environment not specified"
        echo ""
        echo "SCIM Sync is a Production-only service that syncs Google Workspace to AWS Identity Center"
        echo ""
        echo "Usage: $0 prod [action]"
        echo "Actions: configure, test, show-outputs, enable, disable"
        echo ""
        echo "Example: $0 prod configure"
        exit 1
    fi
    
    # Parse arguments
    ENVIRONMENT_ARG=$1
    ACTION=${2:-configure}
    
    # Validate environment
    validate_environment $ENVIRONMENT_ARG
    
    # Check prerequisites
    check_prerequisites
    
    # Check if stack is deployed (except for deployment instructions)
    if [ "$ACTION" != "help" ]; then
        if ! check_stack_deployed; then
            show_deployment_instructions
            exit 1
        fi
    fi
    
    # Execute action
    case $ACTION in
        configure)
            configure_parameters
            ;;
        test)
            test_deployment
            ;;
        show-outputs)
            show_outputs
            ;;
        enable)
            enable_sync
            ;;
        disable)
            disable_sync
            ;;
        help)
            show_deployment_instructions
            ;;
        *)
            print_error "Invalid action: $ACTION"
            echo "Valid actions: configure, test, show-outputs, enable, disable, help"
            exit 1
            ;;
    esac
    
    print_success "Operation completed successfully"
}

# Run main function
main "$@"