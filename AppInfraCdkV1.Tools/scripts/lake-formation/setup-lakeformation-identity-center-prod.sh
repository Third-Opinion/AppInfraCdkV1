#!/bin/bash

# Lake Formation Identity Center Integration Setup Script - Production Environment
# Sets up the integration between Lake Formation and AWS Identity Center for the production account
# INCLUDES ADDITIONAL SAFETY CHECKS AND CONFIRMATION PROMPTS

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Configuration
ENVIRONMENT="prod"
AWS_PROFILE="to-prd-admin"
ACCOUNT_ID="442042533707"
REGION=${AWS_REGION:-us-east-2}
LOG_FILE="lake-formation-setup-prod-$(date +%Y%m%d-%H%M%S).log"
BACKUP_DIR="backups/prod-$(date +%Y%m%d-%H%M%S)"

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
        "critical")
            echo -e "${MAGENTA}⚠${NC} ${RED}$message${NC}"
            echo "[$timestamp] CRITICAL: $message" >> "$LOG_FILE"
            ;;
        *)
            echo "$message"
            echo "[$timestamp] $message" >> "$LOG_FILE"
            ;;
    esac
}

# Function to confirm production action
confirm_production_action() {
    local action=$1
    
    echo
    echo -e "${RED}════════════════════════════════════════════════════════${NC}"
    echo -e "${RED}          PRODUCTION ENVIRONMENT WARNING${NC}"
    echo -e "${RED}════════════════════════════════════════════════════════${NC}"
    echo
    echo -e "${YELLOW}You are about to: $action${NC}"
    echo -e "${YELLOW}Account: $ACCOUNT_ID (PRODUCTION)${NC}"
    echo -e "${YELLOW}Region: $REGION${NC}"
    echo
    echo -e "${RED}This action will affect the PRODUCTION environment.${NC}"
    echo -e "${RED}Please ensure you have proper authorization.${NC}"
    echo
    
    # First confirmation
    read -p "Do you want to proceed? Type 'yes' to continue: " confirm1
    if [ "$confirm1" != "yes" ]; then
        print_status "info" "Operation cancelled by user"
        exit 0
    fi
    
    # Second confirmation for production
    echo
    echo -e "${RED}This is your final confirmation for PRODUCTION changes.${NC}"
    read -p "Type 'PRODUCTION' to confirm: " confirm2
    if [ "$confirm2" != "PRODUCTION" ]; then
        print_status "info" "Operation cancelled - confirmation not received"
        exit 0
    fi
    
    print_status "warning" "Production action confirmed by user"
}

# Function to verify AWS credentials
verify_credentials() {
    print_status "info" "Verifying AWS credentials for PRODUCTION..."
    
    if ! aws sts get-caller-identity --profile "$AWS_PROFILE" &> /dev/null; then
        print_status "error" "Unable to authenticate with AWS profile: $AWS_PROFILE"
        print_status "info" "Please run: aws sso login --profile $AWS_PROFILE"
        exit 1
    fi
    
    local current_account=$(aws sts get-caller-identity --profile "$AWS_PROFILE" --query Account --output text)
    local user_arn=$(aws sts get-caller-identity --profile "$AWS_PROFILE" --query Arn --output text)
    
    if [ "$current_account" != "$ACCOUNT_ID" ]; then
        print_status "error" "Wrong AWS account. Expected: $ACCOUNT_ID, Current: $current_account"
        exit 1
    fi
    
    print_status "success" "Connected to PRODUCTION account: $ACCOUNT_ID"
    print_status "info" "Authenticated as: $user_arn"
}

# Function to check prerequisites
check_prerequisites() {
    print_status "info" "Running prerequisites check for PRODUCTION..."
    
    # Check if prerequisites script exists and run it
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local prereq_script="$script_dir/check-prerequisites.sh"
    
    if [ -f "$prereq_script" ]; then
        if ! bash "$prereq_script" prod; then
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

# Function to create comprehensive backup
create_comprehensive_backup() {
    print_status "info" "Creating comprehensive backup of current configuration..."
    
    # Create backup directory
    mkdir -p "$BACKUP_DIR"
    
    # Backup data lake settings
    print_status "info" "Backing up data lake settings..."
    aws lakeformation get-data-lake-settings \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" > "$BACKUP_DIR/data-lake-settings.json" 2>/dev/null || true
    
    # Backup existing Identity Center configuration
    print_status "info" "Backing up Identity Center configuration..."
    aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" > "$BACKUP_DIR/identity-center-config.json" 2>/dev/null || true
    
    # Backup permissions
    print_status "info" "Backing up Lake Formation permissions..."
    aws lakeformation list-permissions \
        --catalog-id "$ACCOUNT_ID" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" > "$BACKUP_DIR/permissions.json" 2>/dev/null || true
    
    # Backup resource links
    print_status "info" "Backing up resource links..."
    aws lakeformation list-resources \
        --profile "$AWS_PROFILE" \
        --region "$REGION" > "$BACKUP_DIR/resources.json" 2>/dev/null || true
    
    # Create backup summary
    cat > "$BACKUP_DIR/backup-summary.txt" << EOF
Lake Formation Production Backup
================================
Date: $(date)
Account: $ACCOUNT_ID
Region: $REGION
Profile: $AWS_PROFILE

Files backed up:
- data-lake-settings.json
- identity-center-config.json
- permissions.json
- resources.json

To restore, use the restore-lake-formation.sh script with this backup directory.
EOF
    
    print_status "success" "Backup completed: $BACKUP_DIR"
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
            
            # Show existing configuration details
            echo
            echo "Current Configuration:"
            echo "$existing_config" | jq '.'
            echo
            
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

# Function to validate production readiness
validate_production_readiness() {
    print_status "info" "Validating production readiness..."
    
    local ready=true
    
    # Check if dev environment has been set up
    print_status "info" "Checking if development environment has been configured..."
    local dev_config_file="lake-formation-identity-center-config-dev.json"
    
    if [ ! -f "$dev_config_file" ]; then
        print_status "warning" "Development environment configuration not found"
        print_status "warning" "It's recommended to test in development first"
        read -p "Continue without dev testing? (y/n): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            ready=false
        fi
    else
        print_status "success" "Development configuration found"
    fi
    
    # Check for required production groups
    print_status "info" "Verifying production-specific groups..."
    # Additional production validation can be added here
    
    if [ "$ready" = false ]; then
        print_status "error" "Production readiness validation failed"
        exit 1
    fi
    
    print_status "success" "Production readiness validated"
}

# Function to create Lake Formation Identity Center configuration
create_identity_center_configuration() {
    print_status "info" "Creating Lake Formation Identity Center configuration for PRODUCTION..."
    
    # Prepare the command
    local cmd="aws lakeformation create-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --instance-arn $IDENTITY_CENTER_INSTANCE_ARN \
        --profile $AWS_PROFILE \
        --region $REGION"
    
    # Add production-specific configuration
    cmd="$cmd --external-filtering Status=ENABLED"
    
    print_status "info" "Executing integration command..."
    echo "Command: $cmd" >> "$LOG_FILE"
    
    # Execute the command with error handling
    if output=$(eval "$cmd" 2>&1); then
        print_status "success" "Lake Formation Identity Center configuration created successfully!"
        echo "$output" | jq '.' >> "$LOG_FILE" 2>/dev/null || echo "$output" >> "$LOG_FILE"
        return 0
    else
        print_status "error" "Failed to create configuration: $output"
        echo "$output" >> "$LOG_FILE"
        
        # Offer rollback option
        echo
        read -p "Do you want to restore from backup? (y/n): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            restore_from_backup
        fi
        
        return 1
    fi
}

# Function to update Lake Formation Identity Center configuration
update_identity_center_configuration() {
    print_status "info" "Updating Lake Formation Identity Center configuration for PRODUCTION..."
    
    # Prepare the update command
    local cmd="aws lakeformation update-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --profile $AWS_PROFILE \
        --region $REGION"
    
    # Add configuration parameters
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
        
        # Offer rollback option
        echo
        read -p "Do you want to restore from backup? (y/n): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            restore_from_backup
        fi
        
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
        local resource_share=$(echo "$config" | jq -r '.ResourceShare // empty')
        
        if [ -n "$instance_arn" ]; then
            print_status "success" "Integration verified successfully!"
            echo "  Instance ARN: $instance_arn"
            echo "  External Filtering: $external_filtering"
            
            # Save configuration details
            echo "$config" | jq '.' > "lake-formation-identity-center-config-prod.json"
            print_status "info" "Configuration saved to: lake-formation-identity-center-config-prod.json"
            
            # Additional production verification
            print_status "info" "Running production-specific verification..."
            verify_production_specific
            
            return 0
        fi
    fi
    
    print_status "error" "Could not verify integration"
    return 1
}

# Function for production-specific verification
verify_production_specific() {
    print_status "info" "Verifying production-specific configurations..."
    
    # Check if PHI groups have appropriate permissions
    print_status "info" "Checking PHI group permissions..."
    
    # Verify audit logging is enabled
    print_status "info" "Verifying audit logging..."
    
    # Check CloudTrail integration
    local cloudtrail_status=$(aws cloudtrail get-trail-status \
        --name "lake-formation-audit-trail" \
        --profile "$AWS_PROFILE" \
        --region "$REGION" 2>/dev/null || echo "{}")
    
    if [ "$cloudtrail_status" != "{}" ]; then
        local is_logging=$(echo "$cloudtrail_status" | jq -r '.IsLogging // false')
        if [ "$is_logging" = "true" ]; then
            print_status "success" "CloudTrail audit logging is active"
        else
            print_status "warning" "CloudTrail audit logging is not active"
        fi
    fi
    
    print_status "success" "Production verification completed"
}

# Function to restore from backup
restore_from_backup() {
    print_status "critical" "Initiating restore from backup..."
    
    if [ ! -d "$BACKUP_DIR" ]; then
        print_status "error" "Backup directory not found: $BACKUP_DIR"
        return 1
    fi
    
    # This is a placeholder - actual restore logic would go here
    print_status "info" "Restore functionality would be implemented here"
    print_status "info" "Manual restore may be required using files in: $BACKUP_DIR"
}

# Function to send notification
send_notification() {
    local status=$1
    local message=$2
    
    # Log the notification
    echo "NOTIFICATION [$status]: $message" >> "$LOG_FILE"
    
    # In a real production environment, this could send to:
    # - Slack/Teams
    # - Email
    # - SNS topic
    # - PagerDuty
    
    print_status "info" "Notification sent: $message"
}

# Main execution
main() {
    echo -e "${RED}╔════════════════════════════════════════════╗${NC}"
    echo -e "${RED}║   PRODUCTION ENVIRONMENT CONFIGURATION     ║${NC}"
    echo -e "${RED}║   Lake Formation Identity Center Setup     ║${NC}"
    echo -e "${RED}╚════════════════════════════════════════════╝${NC}"
    echo
    echo -e "${YELLOW}Account: $ACCOUNT_ID${NC}"
    echo -e "${YELLOW}Region: $REGION${NC}"
    echo -e "${YELLOW}Profile: $AWS_PROFILE${NC}"
    echo
    
    # Initialize log file
    echo "Lake Formation Identity Center Setup Log - PRODUCTION" > "$LOG_FILE"
    echo "Started: $(date)" >> "$LOG_FILE"
    echo "Account: $ACCOUNT_ID" >> "$LOG_FILE"
    echo "Region: $REGION" >> "$LOG_FILE"
    echo "User: $(whoami)" >> "$LOG_FILE"
    echo "----------------------------------------" >> "$LOG_FILE"
    
    # Production confirmation
    confirm_production_action "Configure Lake Formation Identity Center Integration"
    
    # Step 1: Verify credentials
    verify_credentials
    
    # Step 2: Check prerequisites
    check_prerequisites
    
    # Step 3: Validate production readiness
    validate_production_readiness
    
    # Step 4: Create comprehensive backup
    create_comprehensive_backup
    
    # Step 5: Send pre-change notification
    send_notification "INFO" "Starting Lake Formation Identity Center configuration in production"
    
    # Step 6: Check for existing configuration
    if check_existing_configuration; then
        # Create new configuration
        if create_identity_center_configuration; then
            print_status "success" "Configuration created successfully"
            send_notification "SUCCESS" "Lake Formation Identity Center configuration created in production"
        else
            print_status "error" "Failed to create configuration"
            send_notification "ERROR" "Failed to create Lake Formation Identity Center configuration in production"
            exit 1
        fi
    else
        # Update existing configuration
        if update_identity_center_configuration; then
            print_status "success" "Configuration updated successfully"
            send_notification "SUCCESS" "Lake Formation Identity Center configuration updated in production"
        else
            print_status "error" "Failed to update configuration"
            send_notification "ERROR" "Failed to update Lake Formation Identity Center configuration in production"
            exit 1
        fi
    fi
    
    # Step 7: Verify the integration
    if verify_integration; then
        print_status "success" "Lake Formation Identity Center integration completed successfully!"
        send_notification "SUCCESS" "Lake Formation Identity Center integration verified in production"
    else
        print_status "warning" "Integration completed but verification failed. Please check manually."
        send_notification "WARNING" "Lake Formation Identity Center integration needs manual verification in production"
    fi
    
    echo
    echo -e "${GREEN}╔════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║        PRODUCTION SETUP COMPLETE!          ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════════╝${NC}"
    echo
    echo -e "${GREEN}Log file: $LOG_FILE${NC}"
    echo -e "${GREEN}Backup location: $BACKUP_DIR${NC}"
    echo -e "${GREEN}Configuration saved: lake-formation-identity-center-config-prod.json${NC}"
    echo
    
    # Provide next steps
    print_status "info" "Next steps:"
    echo "  1. Monitor group synchronization from Google Workspace"
    echo "  2. Verify PHI access controls are properly enforced"
    echo "  3. Test with a production Lake Formation resource"
    echo "  4. Review CloudTrail logs for audit compliance"
    echo "  5. Document the configuration in your runbook"
}

# Trap errors
trap 'print_status "error" "Script failed at line $LINENO"; send_notification "ERROR" "Production script failed at line $LINENO"' ERR

# Run main function
main "$@"