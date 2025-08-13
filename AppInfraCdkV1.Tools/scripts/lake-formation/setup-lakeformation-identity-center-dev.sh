#!/bin/bash

# Lake Formation Identity Center Integration Setup Script - Development Environment
# Sets up the integration between Lake Formation and AWS Identity Center for the dev account
# NOTE: Identity Center is hosted in production account (442042533707) and shared with dev

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
ENVIRONMENT="dev"
AWS_PROFILE="to-dev-admin"
ACCOUNT_ID="615299752206"
IDENTITY_CENTER_ACCOUNT="442042533707"  # Identity Center is in production
REGION=${AWS_REGION:-us-east-2}
LOG_FILE="lake-formation-setup-dev-$(date +%Y%m%d-%H%M%S).log"

# Function to print colored output
print_status() {
    local status=$1
    local message=$2
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    
    case $status in
        "success")
            echo -e "${GREEN}✓${NC} $message"
            echo "[$timestamp] SUCCESS: $message" >> "$LOG_FILE"
            ;;
        "error")
            echo -e "${RED}✗${NC} $message"
            echo "[$timestamp] ERROR: $message" >> "$LOG_FILE"
            ;;
        "warning")
            echo -e "${YELLOW}⚠${NC} $message"
            echo "[$timestamp] WARNING: $message" >> "$LOG_FILE"
            ;;
        "info")
            echo -e "${BLUE}ℹ${NC} $message"
            echo "[$timestamp] INFO: $message" >> "$LOG_FILE"
            ;;
        *)
            echo "$message"
            echo "[$timestamp] $message" >> "$LOG_FILE"
            ;;
    esac
}

# Function to verify AWS credentials
verify_credentials() {
    print_status "info" "Verifying AWS credentials..."
    
    if ! aws sts get-caller-identity --profile "$AWS_PROFILE" &> /dev/null; then
        print_status "error" "Unable to authenticate with AWS profile: $AWS_PROFILE"
        print_status "info" "Please run: aws sso login --profile $AWS_PROFILE"
        exit 1
    fi
    
    local current_account=$(aws sts get-caller-identity --profile "$AWS_PROFILE" --query Account --output text)
    
    if [ "$current_account" != "$ACCOUNT_ID" ]; then
        print_status "error" "Wrong AWS account. Expected: $ACCOUNT_ID, Current: $current_account"
        exit 1
    fi
    
    print_status "success" "Connected to development account: $ACCOUNT_ID"
}

# Function to check prerequisites
check_prerequisites() {
    print_status "info" "Running prerequisites check..."
    
    # Check if prerequisites script exists and run it
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local prereq_script="$script_dir/check-prerequisites.sh"
    
    if [ -f "$prereq_script" ]; then
        if ! bash "$prereq_script" dev; then
            print_status "error" "Prerequisites check failed. Please resolve issues before continuing."
            exit 1
        fi
    else
        print_status "warning" "Prerequisites script not found. Continuing without validation."
    fi
    
    # Load Identity Center configuration if available
    if [ -f .identity-center-env ]; then
        source .identity-center-env
        print_status "success" "Loaded Identity Center configuration"
    else
        print_status "error" "Identity Center configuration not found. Run prerequisites check first."
        exit 1
    fi
}

# Function to check existing Lake Formation Identity Center configuration
check_existing_configuration() {
    print_status "info" "Checking for existing Lake Formation Identity Center configuration..."
    
    local existing_config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" \
        2>/dev/null || echo "{}")
    
    if [ "$existing_config" != "{}" ] && [ -n "$existing_config" ]; then
        local instance_arn=$(echo "$existing_config" | jq -r '.InstanceArn // empty')
        
        if [ -n "$instance_arn" ]; then
            print_status "warning" "Existing Lake Formation Identity Center configuration found"
            echo "  Instance ARN: $instance_arn"
            
            read -p "Do you want to update the existing configuration? (y/n): " -n 1 -r
            echo
            if [[ ! $REPLY =~ ^[Yy]$ ]]; then
                print_status "info" "Operation cancelled by user"
                exit 0
            fi
            return 1  # Indicates update mode
        fi
    fi
    
    print_status "success" "No existing configuration found. Will create new integration."
    return 0  # Indicates create mode
}

# Function to create Lake Formation Identity Center configuration
create_identity_center_configuration() {
    print_status "info" "Creating Lake Formation Identity Center configuration..."
    print_status "info" "Linking to Identity Center in production account ($IDENTITY_CENTER_ACCOUNT)..."
    
    # Prepare the command
    # The instance ARN will be from production account's Identity Center
    local cmd="aws lakeformation create-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --instance-arn $IDENTITY_CENTER_INSTANCE_ARN \
        --profile $AWS_PROFILE \
        --region $REGION"
    
    # Optionally add external filtering configuration
    # For now, we'll enable with no specific targets
    cmd="$cmd --external-filtering Status=ENABLED"
    
    print_status "info" "Executing integration command..."
    print_status "info" "This will link dev Lake Formation to production Identity Center"
    echo "Command: $cmd" >> "$LOG_FILE"
    
    # Execute the command
    if output=$(eval "$cmd" 2>&1); then
        print_status "success" "Lake Formation Identity Center configuration created successfully!"
        print_status "success" "Dev account now uses Identity Center from production account"
        echo "$output" | jq '.' >> "$LOG_FILE" 2>/dev/null || echo "$output" >> "$LOG_FILE"
        return 0
    else
        print_status "error" "Failed to create configuration: $output"
        echo "$output" >> "$LOG_FILE"
        return 1
    fi
}

# Function to update Lake Formation Identity Center configuration
update_identity_center_configuration() {
    print_status "info" "Updating Lake Formation Identity Center configuration..."
    
    # Prepare the update command
    local cmd="aws lakeformation update-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --profile $AWS_PROFILE \
        --region $REGION"
    
    # Add configuration parameters as needed
    cmd="$cmd --external-filtering Status=ENABLED"
    
    print_status "info" "Executing update command..."
    echo "Command: $cmd" >> "$LOG_FILE"
    
    # Execute the command
    if output=$(eval "$cmd" 2>&1); then
        print_status "success" "Lake Formation Identity Center configuration updated successfully!"
        echo "$output" | jq '.' >> "$LOG_FILE" 2>/dev/null || echo "$output" >> "$LOG_FILE"
        return 0
    else
        print_status "error" "Failed to update configuration: $output"
        echo "$output" >> "$LOG_FILE"
        return 1
    fi
}

# Function to verify the integration
verify_integration() {
    print_status "info" "Verifying Lake Formation Identity Center integration..."
    
    local config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" \
        2>/dev/null || echo "{}")
    
    if [ "$config" != "{}" ] && [ -n "$config" ]; then
        local instance_arn=$(echo "$config" | jq -r '.InstanceArn // empty')
        local external_filtering=$(echo "$config" | jq -r '.ExternalFiltering.Status // empty')
        
        if [ -n "$instance_arn" ]; then
            print_status "success" "Integration verified successfully!"
            echo "  Instance ARN: $instance_arn"
            echo "  External Filtering: $external_filtering"
            
            # Save configuration details
            echo "$config" | jq '.' > "lake-formation-identity-center-config-dev.json"
            print_status "info" "Configuration saved to: lake-formation-identity-center-config-dev.json"
            
            return 0
        fi
    fi
    
    print_status "error" "Could not verify integration"
    return 1
}

# Function to create rollback snapshot
create_rollback_snapshot() {
    print_status "info" "Creating rollback snapshot..."
    
    local snapshot_file="lake-formation-snapshot-dev-$(date +%Y%m%d-%H%M%S).json"
    
    # Get current data lake settings
    aws lakeformation get-data-lake-settings \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" > "$snapshot_file" 2>/dev/null || true
    
    # Get current Identity Center configuration if it exists
    aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" >> "$snapshot_file" 2>/dev/null || true
    
    if [ -f "$snapshot_file" ]; then
        print_status "success" "Rollback snapshot created: $snapshot_file"
    fi
}

# Main execution
main() {
    echo -e "${GREEN}======================================${NC}"
    echo -e "${GREEN}Lake Formation Identity Center Setup${NC}"
    echo -e "${GREEN}Environment: Development${NC}"
    echo -e "${GREEN}Account: $ACCOUNT_ID${NC}"
    echo -e "${GREEN}Identity Center Account: $IDENTITY_CENTER_ACCOUNT (Production)${NC}"
    echo -e "${GREEN}Region: $REGION${NC}"
    echo -e "${GREEN}======================================${NC}"
    echo
    
    print_status "info" "Note: Identity Center is hosted in production account"
    print_status "info" "Dev account will use cross-account access to production Identity Center"
    echo
    
    # Initialize log file
    echo "Lake Formation Identity Center Setup Log - Development" > "$LOG_FILE"
    echo "Started: $(date)" >> "$LOG_FILE"
    echo "Account: $ACCOUNT_ID" >> "$LOG_FILE"
    echo "Region: $REGION" >> "$LOG_FILE"
    echo "----------------------------------------" >> "$LOG_FILE"
    
    # Step 1: Verify credentials
    verify_credentials
    
    # Step 2: Check prerequisites
    check_prerequisites
    
    # Step 3: Create rollback snapshot
    create_rollback_snapshot
    
    # Step 4: Check for existing configuration
    if check_existing_configuration; then
        # Create new configuration
        if create_identity_center_configuration; then
            print_status "success" "Configuration created successfully"
        else
            print_status "error" "Failed to create configuration"
            exit 1
        fi
    else
        # Update existing configuration
        if update_identity_center_configuration; then
            print_status "success" "Configuration updated successfully"
        else
            print_status "error" "Failed to update configuration"
            exit 1
        fi
    fi
    
    # Step 5: Verify the integration
    if verify_integration; then
        print_status "success" "Lake Formation Identity Center integration completed successfully!"
    else
        print_status "warning" "Integration completed but verification failed. Please check manually."
    fi
    
    echo
    echo -e "${GREEN}======================================${NC}"
    echo -e "${GREEN}Setup Complete!${NC}"
    echo -e "${GREEN}Log file: $LOG_FILE${NC}"
    echo -e "${GREEN}======================================${NC}"
    
    # Provide next steps
    echo
    print_status "info" "Next steps:"
    echo "  1. Verify groups are syncing from Google Workspace"
    echo "  2. Test permissions with a sample Lake Formation resource"
    echo "  3. Run the production setup script when ready"
}

# Trap errors
trap 'print_status "error" "Script failed at line $LINENO"' ERR

# Run main function
main "$@"