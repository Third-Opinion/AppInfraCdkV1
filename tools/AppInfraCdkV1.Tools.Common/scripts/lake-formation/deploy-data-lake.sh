#!/bin/bash

# Lake Formation Master Deployment Script
# This script orchestrates the deployment of Lake Formation infrastructure
# using CDK and manages Identity Center integration

set -euo pipefail

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../../../.." && pwd )"

# Function to print colored messages
print_status() {
    local status=$1
    local message=$2
    
    case $status in
        "info")
            echo -e "${BLUE}[INFO]${NC} $message"
            ;;
        "success")
            echo -e "${GREEN}[SUCCESS]${NC} $message"
            ;;
        "warning")
            echo -e "${YELLOW}[WARNING]${NC} $message"
            ;;
        "error")
            echo -e "${RED}[ERROR]${NC} $message"
            ;;
    esac
}

# Function to detect AWS environment
detect_environment() {
    local account_id=$(aws sts get-caller-identity --query Account --output text 2>/dev/null || echo "")
    
    if [ -z "$account_id" ]; then
        print_status "error" "Failed to get AWS account ID. Please configure AWS credentials."
        exit 1
    fi
    
    case $account_id in
        "615299752206")
            export ENVIRONMENT="Development"
            export AWS_PROFILE="to-dev-admin"
            export CDK_ENVIRONMENT="Development"
            ;;
        "442042533707")
            export ENVIRONMENT="Production"
            export AWS_PROFILE="to-prd-admin"
            export CDK_ENVIRONMENT="Production"
            ;;
        *)
            print_status "error" "Unknown AWS account: $account_id"
            exit 1
            ;;
    esac
    
    print_status "info" "Detected environment: $ENVIRONMENT (Account: $account_id)"
    print_status "info" "Using AWS profile: $AWS_PROFILE"
}

# Function to check prerequisites
check_prerequisites() {
    print_status "info" "Checking prerequisites..."
    
    # Check for required tools
    command -v aws >/dev/null 2>&1 || { print_status "error" "AWS CLI is required but not installed."; exit 1; }
    command -v dotnet >/dev/null 2>&1 || { print_status "error" "dotnet is required but not installed."; exit 1; }
    command -v npx >/dev/null 2>&1 || { print_status "error" "npx is required but not installed."; exit 1; }
    
    # Run prerequisites check script
    if [ -f "$SCRIPT_DIR/check-prerequisites.sh" ]; then
        print_status "info" "Running prerequisites validation..."
        bash "$SCRIPT_DIR/check-prerequisites.sh" || {
            print_status "warning" "Prerequisites check completed with warnings"
        }
    fi
    
    print_status "success" "Prerequisites check completed"
}

# Function to setup Identity Center integration
setup_identity_center() {
    print_status "info" "Setting up Lake Formation Identity Center integration..."
    
    local setup_script=""
    if [ "$ENVIRONMENT" == "Development" ]; then
        setup_script="$SCRIPT_DIR/setup-lakeformation-identity-center-dev.sh"
    else
        setup_script="$SCRIPT_DIR/setup-lakeformation-identity-center-prod.sh"
    fi
    
    if [ -f "$setup_script" ]; then
        print_status "info" "Running Identity Center setup script..."
        bash "$setup_script" || {
            print_status "error" "Identity Center setup failed"
            return 1
        }
    else
        print_status "warning" "Identity Center setup script not found: $setup_script"
        return 1
    fi
    
    print_status "success" "Identity Center integration completed"
}

# Function to deploy CDK stacks
deploy_cdk_stacks() {
    print_status "info" "Deploying Lake Formation CDK stacks..."
    
    cd "$PROJECT_ROOT"
    
    # Build the project first
    print_status "info" "Building CDK project..."
    dotnet build AppInfraCdkV1.Apps/AppInfraCdkV1.Apps.csproj || {
        print_status "error" "Build failed"
        return 1
    }
    
    # Set environment variables for CDK
    export CDK_DEFAULT_ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
    export CDK_DEFAULT_REGION="us-east-2"
    
    # Determine stack names based on environment
    local env_prefix=""
    if [ "$ENVIRONMENT" == "Development" ]; then
        env_prefix="dev"
    else
        env_prefix="prod"
    fi
    
    # List available stacks
    print_status "info" "Available Lake Formation stacks:"
    npx cdk list --app "dotnet run --project AppInfraCdkV1.Apps -- --app=LakeFormation --environment=$CDK_ENVIRONMENT" | grep -E "${env_prefix}-lf-" || true
    
    # Deploy stacks in order
    local stacks=(
        "${env_prefix}-lf-storage-ue2"
        "${env_prefix}-lf-setup-ue2"
        "${env_prefix}-lf-permissions-ue2"
    )
    
    for stack in "${stacks[@]}"; do
        print_status "info" "Deploying stack: $stack"
        npx cdk deploy "$stack" \
            --app "dotnet run --project AppInfraCdkV1.Apps -- --app=LakeFormation --environment=$CDK_ENVIRONMENT" \
            --require-approval never \
            --profile "$AWS_PROFILE" || {
            print_status "error" "Failed to deploy stack: $stack"
            return 1
        }
        print_status "success" "Stack deployed: $stack"
    done
    
    print_status "success" "All CDK stacks deployed successfully"
}

# Function to grant permissions
grant_permissions() {
    print_status "info" "Granting Lake Formation permissions..."
    
    # The permissions are managed through CDK, but we need to update group IDs
    print_status "info" "Updating CloudFormation parameters with Identity Center group IDs..."
    
    # Get group IDs from Identity Center
    local groups=("data-analysts-dev" "data-analysts-phi" "data-engineers-phi")
    
    for group in "${groups[@]}"; do
        print_status "info" "Looking up group ID for: $group@thirdopinion.io"
        
        # This would normally query Identity Center for the group ID
        # For now, we'll prompt for manual input
        print_status "warning" "Manual step required: Update CloudFormation stack parameters with group IDs"
        print_status "info" "Group: $group - Get ID from AWS Identity Center console"
    done
    
    print_status "info" "Permissions will be applied through CDK stack updates"
}

# Function to test deployment
test_deployment() {
    print_status "info" "Testing Lake Formation deployment..."
    
    # Check if stacks are deployed
    local env_prefix=""
    if [ "$ENVIRONMENT" == "Development" ]; then
        env_prefix="dev"
    else
        env_prefix="prod"
    fi
    
    print_status "info" "Checking stack status..."
    aws cloudformation describe-stacks \
        --stack-name "${env_prefix}-lf-storage-ue2" \
        --query 'Stacks[0].StackStatus' \
        --output text \
        --profile "$AWS_PROFILE" || {
        print_status "error" "Storage stack not found"
        return 1
    }
    
    # Check S3 buckets
    print_status "info" "Verifying S3 buckets..."
    aws s3 ls --profile "$AWS_PROFILE" | grep -E "thirdopinion-(raw|curated|phi)-${env_prefix}" || {
        print_status "warning" "Some expected buckets not found"
    }
    
    # Check Lake Formation configuration
    print_status "info" "Checking Lake Formation configuration..."
    aws lakeformation describe-lake-formation-identity-center-configuration \
        --profile "$AWS_PROFILE" 2>/dev/null || {
        print_status "warning" "Lake Formation Identity Center configuration not found"
    }
    
    # Run validation script if available
    if [ -f "$SCRIPT_DIR/test-integration.sh" ]; then
        print_status "info" "Running integration tests..."
        bash "$SCRIPT_DIR/test-integration.sh"
    fi
    
    print_status "success" "Deployment testing completed"
}

# Function to destroy resources
destroy_resources() {
    print_status "warning" "Preparing to destroy Lake Formation resources..."
    
    # Confirmation prompt
    read -p "Are you sure you want to destroy all Lake Formation resources? (yes/no): " confirm
    if [ "$confirm" != "yes" ]; then
        print_status "info" "Destruction cancelled"
        return 0
    fi
    
    cd "$PROJECT_ROOT"
    
    # Determine stack names based on environment
    local env_prefix=""
    if [ "$ENVIRONMENT" == "Development" ]; then
        env_prefix="dev"
    else
        env_prefix="prod"
    fi
    
    # Destroy stacks in reverse order
    local stacks=(
        "${env_prefix}-lf-permissions-ue2"
        "${env_prefix}-lf-setup-ue2"
        "${env_prefix}-lf-storage-ue2"
    )
    
    for stack in "${stacks[@]}"; do
        print_status "info" "Destroying stack: $stack"
        npx cdk destroy "$stack" \
            --app "dotnet run --project AppInfraCdkV1.Apps -- --app=LakeFormation --environment=$CDK_ENVIRONMENT" \
            --force \
            --profile "$AWS_PROFILE" || {
            print_status "warning" "Failed to destroy stack: $stack (may not exist)"
        }
    done
    
    print_status "success" "Resource destruction completed"
}

# Function to run full deployment
full_deployment() {
    print_status "info" "Starting full Lake Formation deployment..."
    
    check_prerequisites || exit 1
    setup_identity_center || {
        print_status "warning" "Identity Center setup failed, continuing with deployment..."
    }
    deploy_cdk_stacks || exit 1
    grant_permissions
    test_deployment
    
    print_status "success" "Full deployment completed successfully!"
}

# Function to show usage
show_usage() {
    cat << EOF
Usage: $0 <action> [options]

Actions:
    setup-identity-center   Setup Lake Formation Identity Center integration
    deploy                  Deploy Lake Formation CDK stacks
    grant-permissions       Grant Lake Formation permissions to groups
    test                   Test the deployment
    full                   Run full deployment (all steps)
    destroy                Destroy all Lake Formation resources
    help                   Show this help message

Options:
    --profile <profile>    Override AWS profile
    --environment <env>    Override environment (Development/Production)

Examples:
    $0 full                         # Run full deployment
    $0 deploy                       # Deploy CDK stacks only
    $0 test                         # Test existing deployment
    $0 destroy                      # Destroy all resources
    $0 deploy --profile to-dev-admin --environment Development

EOF
}

# Main script execution
main() {
    local action=${1:-help}
    shift || true
    
    # Parse additional options
    while [[ $# -gt 0 ]]; do
        case $1 in
            --profile)
                export AWS_PROFILE="$2"
                shift 2
                ;;
            --environment)
                export CDK_ENVIRONMENT="$2"
                export ENVIRONMENT="$2"
                shift 2
                ;;
            *)
                print_status "error" "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
    
    # Detect environment if not set
    if [ -z "${ENVIRONMENT:-}" ]; then
        detect_environment
    fi
    
    case $action in
        setup-identity-center)
            check_prerequisites
            setup_identity_center
            ;;
        deploy)
            check_prerequisites
            deploy_cdk_stacks
            ;;
        grant-permissions)
            grant_permissions
            ;;
        test)
            test_deployment
            ;;
        full)
            full_deployment
            ;;
        destroy)
            destroy_resources
            ;;
        help)
            show_usage
            ;;
        *)
            print_status "error" "Unknown action: $action"
            show_usage
            exit 1
            ;;
    esac
}

# Run main function
main "$@"