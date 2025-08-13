#!/bin/bash

# Lake Formation Identity Center Prerequisites Validation Script
# Validates prerequisites before attempting Lake Formation Identity Center integration

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    local status=$1
    local message=$2
    case $status in
        "success")
            echo -e "${GREEN}✓${NC} $message"
            ;;
        "error")
            echo -e "${RED}✗${NC} $message"
            ;;
        "warning")
            echo -e "${YELLOW}⚠${NC} $message"
            ;;
        *)
            echo "$message"
            ;;
    esac
}

# Function to check AWS CLI installation
check_aws_cli() {
    if ! command -v aws &> /dev/null; then
        print_status "error" "AWS CLI is not installed"
        return 1
    fi
    
    local version=$(aws --version 2>&1 | cut -d' ' -f1 | cut -d'/' -f2)
    print_status "success" "AWS CLI installed (version: $version)"
    return 0
}

# Function to check AWS credentials
check_aws_credentials() {
    local profile=${1:-default}
    
    if ! aws sts get-caller-identity --profile "$profile" &> /dev/null; then
        print_status "error" "Unable to authenticate with AWS using profile: $profile"
        return 1
    fi
    
    local account_id=$(aws sts get-caller-identity --profile "$profile" --query Account --output text)
    local user_arn=$(aws sts get-caller-identity --profile "$profile" --query Arn --output text)
    
    print_status "success" "AWS authentication successful"
    echo "  Account ID: $account_id"
    echo "  User/Role ARN: $user_arn"
    return 0
}

# Function to check Identity Center instance
check_identity_center() {
    local profile=${1:-default}
    local environment=${2:-dev}
    
    echo -e "\n${YELLOW}Checking Identity Center Configuration...${NC}"
    
    # Identity Center is only in production account (442042533707)
    # Dev account uses cross-account access from production Identity Center
    if [ "$environment" = "dev" ] || [ "$environment" = "development" ]; then
        print_status "info" "Development account uses Identity Center from production account"
        print_status "info" "Switching to production profile to check Identity Center..."
        profile="to-prd-admin"
    fi
    
    # List Identity Center instances (always from production account)
    local instances=$(aws sso-admin list-instances --profile "$profile" 2>/dev/null || echo "")
    
    if [ -z "$instances" ] || [ "$instances" == "[]" ]; then
        print_status "error" "No Identity Center instances found in production account"
        print_status "info" "Identity Center must be configured in production account (442042533707)"
        return 1
    fi
    
    # Get instance details
    local instance_arn=$(echo "$instances" | jq -r '.Instances[0].InstanceArn')
    local identity_store_id=$(echo "$instances" | jq -r '.Instances[0].IdentityStoreId')
    
    if [ "$instance_arn" == "null" ] || [ -z "$instance_arn" ]; then
        print_status "error" "Could not retrieve Identity Center instance ARN"
        return 1
    fi
    
    print_status "success" "Identity Center instance found in production account"
    echo "  Instance ARN: $instance_arn"
    echo "  Identity Store ID: $identity_store_id"
    echo "  Note: This Identity Center instance is shared across all accounts"
    
    # Save to environment file for later use
    echo "IDENTITY_CENTER_INSTANCE_ARN=$instance_arn" > .identity-center-env
    echo "IDENTITY_STORE_ID=$identity_store_id" >> .identity-center-env
    echo "IDENTITY_CENTER_ACCOUNT=442042533707" >> .identity-center-env
    
    return 0
}

# Function to check for required groups in Identity Center
check_identity_center_groups() {
    local profile=${1:-default}
    local identity_store_id=$2
    local environment=${3:-dev}
    
    echo -e "\n${YELLOW}Checking Required Groups in Identity Center...${NC}"
    
    # Always use production profile since Identity Center is only in production
    print_status "info" "Checking groups in production Identity Center (shared across accounts)..."
    profile="to-prd-admin"
    
    # Required groups for Lake Formation
    local required_groups=(
        "data-analysts-dev"
        "data-analysts-phi"
        "data-engineers-phi"
    )
    
    local groups_found=0
    local groups_missing=0
    
    for group_name in "${required_groups[@]}"; do
        # Search for group in Identity Center (production account)
        local group_search=$(aws identitystore list-groups \
            --identity-store-id "$identity_store_id" \
            --profile "$profile" \
            --filters "AttributePath=DisplayName,AttributeValue=$group_name" \
            2>/dev/null || echo "")
        
        if [ -z "$group_search" ] || [ "$(echo "$group_search" | jq '.Groups | length')" -eq 0 ]; then
            print_status "warning" "Group not yet synced: $group_name"
            groups_missing=$((groups_missing + 1))
        else
            local group_id=$(echo "$group_search" | jq -r '.Groups[0].GroupId')
            print_status "success" "Group found: $group_name (ID: $group_id)"
            groups_found=$((groups_found + 1))
        fi
    done
    
    echo
    if [ "$groups_missing" -gt 0 ]; then
        print_status "warning" "$groups_missing group(s) not yet synced from Google Workspace"
        print_status "info" "This is expected if you've just created the groups in Google Workspace"
        print_status "info" "Sync typically takes 15-40 minutes to complete"
        print_status "info" "You can proceed with Lake Formation setup - groups will be available after sync"
        echo
        echo -e "${YELLOW}Next Steps:${NC}"
        echo "  1. Ensure groups exist in Google Workspace Admin Console"
        echo "  2. Wait for automatic sync (or trigger manual sync if configured)"
        echo "  3. Re-run this script later to verify groups have synced"
        echo "  4. Lake Formation configuration will work once groups are synced"
        # Don't return error - just warning
        return 0
    else
        print_status "success" "All required groups found in Identity Center!"
    fi
    
    return 0
}

# Function to check Lake Formation configuration
check_lake_formation_configuration() {
    local profile=${1:-default}
    
    echo -e "\n${YELLOW}Checking Lake Formation Configuration...${NC}"
    
    # Check for existing Identity Center configuration
    local existing_config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --profile "$profile" 2>/dev/null || echo "")
    
    if [ -n "$existing_config" ] && [ "$existing_config" != "{}" ]; then
        local instance_arn=$(echo "$existing_config" | jq -r '.InstanceArn // empty')
        if [ -n "$instance_arn" ] && [ "$instance_arn" != "empty" ]; then
            print_status "warning" "Lake Formation Identity Center integration already exists"
            echo "  Instance ARN: $instance_arn"
            echo "  You may need to update the existing configuration instead of creating a new one"
        fi
    else
        print_status "success" "No existing Lake Formation Identity Center configuration found"
        print_status "info" "Ready to create new Identity Center integration"
    fi
    
    # Check data lake settings
    local data_lake_settings=$(aws lakeformation get-data-lake-settings \
        --profile "$profile" 2>/dev/null || echo "")
    
    if [ -n "$data_lake_settings" ]; then
        local admins=$(echo "$data_lake_settings" | jq -r '.DataLakeSettings.DataLakeAdmins | length' 2>/dev/null || echo "0")
        if [ "$admins" -gt 0 ]; then
            print_status "info" "Found $admins Lake Formation admin(s) configured"
        else
            print_status "warning" "No Lake Formation admins configured yet"
        fi
    fi
    
    return 0
}

# Function to check required IAM permissions
check_iam_permissions() {
    local profile=${1:-default}
    
    echo -e "\n${YELLOW}Checking Required IAM Permissions...${NC}"
    
    local permissions_ok=true
    
    # Test Lake Formation permissions
    print_status "info" "Testing Lake Formation permissions..."
    
    # Try to describe Lake Formation Identity Center configuration
    if aws lakeformation describe-lake-formation-identity-center-configuration \
        --profile "$profile" &> /dev/null; then
        print_status "success" "Can access Lake Formation Identity Center configuration"
    else
        # This might fail if not configured yet, try list-data-lake-settings instead
        if aws lakeformation list-data-lake-settings \
            --profile "$profile" &> /dev/null; then
            print_status "success" "Can access Lake Formation settings"
        else
            print_status "error" "Cannot access Lake Formation - check IAM permissions"
            permissions_ok=false
        fi
    fi
    
    # Test SSO Admin permissions (always check in production for Identity Center)
    print_status "info" "Testing Identity Center admin permissions..."
    local sso_profile="$profile"
    if [[ "$profile" == "to-dev-admin" ]]; then
        # For dev, we need to check production Identity Center
        sso_profile="to-prd-admin"
        print_status "info" "Checking production Identity Center permissions..."
    fi
    
    if aws sso-admin list-instances --profile "$sso_profile" &> /dev/null; then
        print_status "success" "Can access Identity Center admin APIs"
    else
        print_status "error" "Cannot access Identity Center admin APIs"
        permissions_ok=false
    fi
    
    # Test Identity Store permissions
    print_status "info" "Testing Identity Store permissions..."
    if [ -n "${IDENTITY_STORE_ID:-}" ]; then
        if aws identitystore describe-identity-store \
            --identity-store-id "$IDENTITY_STORE_ID" \
            --profile "$sso_profile" &> /dev/null; then
            print_status "success" "Can access Identity Store APIs"
        else
            print_status "error" "Cannot access Identity Store APIs"
            permissions_ok=false
        fi
    else
        print_status "warning" "Identity Store ID not available, skipping Identity Store permission check"
    fi
    
    # Test ability to simulate policies (optional but helpful)
    print_status "info" "Testing IAM policy simulation capabilities..."
    local caller_arn=$(aws sts get-caller-identity --profile "$profile" --query Arn --output text 2>/dev/null)
    if [ -n "$caller_arn" ]; then
        # Try to simulate a Lake Formation action
        if aws iam simulate-principal-policy \
            --policy-source-arn "$caller_arn" \
            --action-names "lakeformation:CreateLakeFormationIdentityCenterConfiguration" \
            --profile "$profile" &> /dev/null; then
            print_status "success" "IAM policy simulation available"
        else
            print_status "warning" "Cannot simulate IAM policies (non-critical)"
        fi
    fi
    
    # Summary of required permissions
    echo
    print_status "info" "Required permissions for Lake Formation Identity Center setup:"
    local required_permissions=(
        "lakeformation:CreateLakeFormationIdentityCenterConfiguration"
        "lakeformation:DescribeLakeFormationIdentityCenterConfiguration"
        "lakeformation:UpdateLakeFormationIdentityCenterConfiguration"
        "lakeformation:ListDataLakeSettings"
        "lakeformation:GetDataLakeSettings"
        "sso-admin:ListInstances"
        "identitystore:ListGroups"
        "identitystore:DescribeIdentityStore"
    )
    
    for perm in "${required_permissions[@]}"; do
        echo "  - $perm"
    done
    
    if [ "$permissions_ok" = false ]; then
        print_status "error" "Some permission checks failed"
        print_status "info" "Ensure your IAM role has the required permissions listed above"
        return 1
    else
        print_status "success" "Permission checks passed"
        return 0
    fi
}

# Main execution
main() {
    local environment=${1:-dev}
    local profile=""
    
    # Set profile based on environment
    case $environment in
        dev|development)
            profile="to-dev-admin"
            echo -e "${GREEN}=== Lake Formation Prerequisites Check - Development ===${NC}"
            ;;
        prod|production)
            profile="to-prd-admin"
            echo -e "${RED}=== Lake Formation Prerequisites Check - Production ===${NC}"
            ;;
        *)
            echo "Usage: $0 [dev|prod]"
            exit 1
            ;;
    esac
    
    echo "Environment: $environment"
    echo "AWS Profile: $profile"
    echo "Timestamp: $(date)"
    echo "================================================"
    
    local has_errors=false
    
    # Run all checks
    if ! check_aws_cli; then
        has_errors=true
    fi
    
    if ! check_aws_credentials "$profile"; then
        has_errors=true
    fi
    
    # Identity Center check - always uses production account
    if ! check_identity_center "$profile" "$environment"; then
        has_errors=true
    else
        # Get Identity Store ID from saved environment
        source .identity-center-env
        check_identity_center_groups "$profile" "$IDENTITY_STORE_ID" "$environment"
    fi
    
    if ! check_lake_formation_configuration "$profile"; then
        has_errors=true
    fi
    
    if ! check_iam_permissions "$profile"; then
        has_errors=true
    fi
    
    echo -e "\n================================================"
    
    if [ "$has_errors" = true ]; then
        print_status "error" "Prerequisites check completed with errors"
        echo -e "${YELLOW}Please resolve the issues above before proceeding with Lake Formation Identity Center integration${NC}"
        exit 1
    else
        print_status "success" "All prerequisites validated successfully!"
        echo -e "${GREEN}You can proceed with Lake Formation Identity Center integration${NC}"
        
        if [ -f .identity-center-env ]; then
            echo -e "\nIdentity Center configuration saved to .identity-center-env"
        fi
    fi
}

# Run main function
main "$@"