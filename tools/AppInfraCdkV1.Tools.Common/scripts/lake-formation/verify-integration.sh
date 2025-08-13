#!/bin/bash

# Lake Formation Identity Center Integration Verification Script
# Verifies that the Lake Formation Identity Center integration is properly configured

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
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
        "info")
            echo -e "${BLUE}ℹ${NC} $message"
            ;;
        *)
            echo "$message"
            ;;
    esac
}

# Function to verify Lake Formation Identity Center configuration
verify_lakeformation_config() {
    local profile=$1
    local account_id=$2
    local region=${3:-us-east-2}
    
    print_status "info" "Verifying Lake Formation Identity Center configuration..."
    
    # Get the configuration
    local config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$account_id" \
        --profile "$profile" \
        --region "$region" \
        2>/dev/null || echo "{}")
    
    if [ "$config" = "{}" ] || [ -z "$config" ]; then
        print_status "error" "No Lake Formation Identity Center configuration found"
        return 1
    fi
    
    # Parse configuration details
    local instance_arn=$(echo "$config" | jq -r '.InstanceArn // empty')
    local catalog_id=$(echo "$config" | jq -r '.CatalogId // empty')
    local external_filtering=$(echo "$config" | jq -r '.ExternalFiltering.Status // empty')
    local resource_share=$(echo "$config" | jq -r '.ResourceShare // empty')
    
    if [ -z "$instance_arn" ]; then
        print_status "error" "Identity Center instance ARN not found in configuration"
        return 1
    fi
    
    print_status "success" "Lake Formation Identity Center configuration found"
    echo "  Instance ARN: $instance_arn"
    echo "  Catalog ID: $catalog_id"
    echo "  External Filtering: $external_filtering"
    
    if [ -n "$resource_share" ] && [ "$resource_share" != "null" ]; then
        echo "  Resource Share: $resource_share"
    fi
    
    return 0
}

# Function to verify Identity Center groups are mapped
verify_identity_center_groups() {
    local profile=$1
    local identity_store_id=$2
    local environment=$3
    
    print_status "info" "Verifying Identity Center groups..."
    print_status "info" "Note: Groups are in production Identity Center, shared across accounts"
    
    # Always use production profile for Identity Center operations
    local ic_profile="to-prd-admin"
    
    # List of expected groups
    local expected_groups=(
        "data-analysts-dev"
        "data-analysts-phi"
        "data-engineers-phi"
    )
    
    local groups_found=0
    local groups_missing=0
    local group_ids=()
    
    for group_name in "${expected_groups[@]}"; do
        # Search for the group in production Identity Center
        local group_search=$(aws identitystore list-groups \
            --identity-store-id "$identity_store_id" \
            --profile "$ic_profile" \
            --filters "AttributePath=DisplayName,AttributeValue=$group_name" \
            2>/dev/null || echo "{}")
        
        if [ "$group_search" = "{}" ] || [ "$(echo "$group_search" | jq '.Groups | length')" -eq 0 ]; then
            print_status "warning" "Group not yet synced: $group_name"
            groups_missing=$((groups_missing + 1))
        else
            local group_id=$(echo "$group_search" | jq -r '.Groups[0].GroupId')
            local display_name=$(echo "$group_search" | jq -r '.Groups[0].DisplayName')
            print_status "success" "Group found: $display_name (ID: $group_id)"
            group_ids+=("$group_id")
            groups_found=$((groups_found + 1))
        fi
    done
    
    if [ "$groups_missing" -gt 0 ]; then
        echo
        print_status "info" "$groups_missing group(s) not yet synced from Google Workspace"
        print_status "info" "This is expected if groups were recently created"
        print_status "info" "Sync typically takes 15-40 minutes to complete"
        # Don't return error - just continue verification
    fi
    
    if [ "$groups_found" -gt 0 ]; then
        print_status "success" "$groups_found group(s) available in Identity Center"
    fi
    
    return 0
}

# Function to verify Lake Formation permissions
verify_lakeformation_permissions() {
    local profile=$1
    local account_id=$2
    local region=${3:-us-east-2}
    
    print_status "info" "Checking Lake Formation permissions for Identity Center principals..."
    
    # List permissions
    local permissions=$(aws lakeformation list-permissions \
        --catalog-id "$account_id" \
        --profile "$profile" \
        --region "$region" \
        --max-results 100 \
        2>/dev/null || echo "{}")
    
    if [ "$permissions" = "{}" ]; then
        print_status "warning" "Could not retrieve Lake Formation permissions"
        return 1
    fi
    
    # Count permissions by principal type
    local total_permissions=$(echo "$permissions" | jq '.PrincipalResourcePermissions | length')
    local identity_center_permissions=$(echo "$permissions" | jq '[.PrincipalResourcePermissions[] | select(.Principal.DataLakePrincipalIdentifier | contains("identitystore"))] | length')
    
    print_status "info" "Total permissions: $total_permissions"
    
    if [ "$identity_center_permissions" -gt 0 ]; then
        print_status "success" "Found $identity_center_permissions Identity Center-based permissions"
        
        # Show sample permissions
        echo "  Sample Identity Center permissions:"
        echo "$permissions" | jq -r '.PrincipalResourcePermissions[] | 
            select(.Principal.DataLakePrincipalIdentifier | contains("identitystore")) | 
            "\(.Principal.DataLakePrincipalIdentifier) -> \(.Permissions[])"' | head -5
    else
        print_status "warning" "No Identity Center-based permissions found yet"
        echo "  This is normal if groups haven't been granted Lake Formation permissions yet"
    fi
    
    return 0
}

# Function to test group synchronization
test_group_sync() {
    local profile=$1
    local identity_store_id=$2
    
    print_status "info" "Testing group synchronization status..."
    
    # Get all groups from Identity Center
    local all_groups=$(aws identitystore list-groups \
        --identity-store-id "$identity_store_id" \
        --profile "$profile" \
        --max-results 100 \
        2>/dev/null || echo "{}")
    
    if [ "$all_groups" = "{}" ]; then
        print_status "error" "Could not list groups from Identity Center"
        return 1
    fi
    
    local total_groups=$(echo "$all_groups" | jq '.Groups | length')
    print_status "info" "Total groups in Identity Center: $total_groups"
    
    # Check for AWS-specific groups (likely synced from Google Workspace)
    local aws_groups=$(echo "$all_groups" | jq '[.Groups[] | select(.DisplayName | contains("aws-") or contains("AWS-") or contains("data-"))] | length')
    
    if [ "$aws_groups" -gt 0 ]; then
        print_status "success" "Found $aws_groups AWS/data-related groups"
        echo "  Groups:"
        echo "$all_groups" | jq -r '.Groups[] | 
            select(.DisplayName | contains("aws-") or contains("AWS-") or contains("data-")) | 
            "  - \(.DisplayName)"' | head -10
    else
        print_status "warning" "No AWS/data-related groups found yet"
    fi
    
    return 0
}

# Function to generate verification report
generate_report() {
    local environment=$1
    local profile=$2
    local account_id=$3
    local report_file="lake-formation-verification-${environment}-$(date +%Y%m%d-%H%M%S).json"
    
    print_status "info" "Generating verification report..."
    
    # Create JSON report
    cat > "$report_file" << EOF
{
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "environment": "$environment",
  "account_id": "$account_id",
  "profile": "$profile",
  "verification_results": {
EOF
    
    # Add Lake Formation config status
    local lf_config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$account_id" \
        --profile "$profile" \
        2>/dev/null || echo '{"status": "not_configured"}')
    
    echo '    "lake_formation_config": ' >> "$report_file"
    echo "$lf_config" | jq '.' | sed 's/^/    /' >> "$report_file"
    echo ',' >> "$report_file"
    
    # Add Identity Center status
    local ic_instances=$(aws sso-admin list-instances --profile "$profile" 2>/dev/null || echo '{"instances": []}')
    echo '    "identity_center": ' >> "$report_file"
    echo "$ic_instances" | jq '.' | sed 's/^/    /' >> "$report_file"
    
    # Close JSON
    cat >> "$report_file" << EOF
  }
}
EOF
    
    print_status "success" "Verification report saved to: $report_file"
    
    return 0
}

# Main verification function
main() {
    local environment=${1:-dev}
    local profile=""
    local account_id=""
    
    # Set configuration based on environment
    case $environment in
        dev|development)
            profile="to-dev-admin"
            account_id="615299752206"
            echo -e "${GREEN}=== Lake Formation Identity Center Verification - Development ===${NC}"
            ;;
        prod|production)
            profile="to-prd-admin"
            account_id="442042533707"
            echo -e "${RED}=== Lake Formation Identity Center Verification - Production ===${NC}"
            ;;
        *)
            echo "Usage: $0 [dev|prod]"
            exit 1
            ;;
    esac
    
    echo "Environment: $environment"
    echo "Account: $account_id"
    echo "Profile: $profile"
    echo "Timestamp: $(date)"
    echo "================================================"
    echo
    
    local has_errors=false
    
    # Step 1: Verify Lake Formation configuration
    if ! verify_lakeformation_config "$profile" "$account_id"; then
        has_errors=true
    fi
    echo
    
    # Step 2: Get Identity Store ID
    local identity_store_id=""
    if [ -f .identity-center-env ]; then
        source .identity-center-env
        identity_store_id="$IDENTITY_STORE_ID"
    else
        # Try to get it from AWS
        local instances=$(aws sso-admin list-instances --profile "$profile" 2>/dev/null || echo "")
        if [ -n "$instances" ]; then
            identity_store_id=$(echo "$instances" | jq -r '.Instances[0].IdentityStoreId // empty')
        fi
    fi
    
    if [ -n "$identity_store_id" ]; then
        # Step 3: Verify Identity Center groups
        if ! verify_identity_center_groups "$profile" "$identity_store_id" "$environment"; then
            has_errors=true
        fi
        echo
        
        # Step 4: Test group synchronization
        test_group_sync "$profile" "$identity_store_id"
        echo
    else
        print_status "warning" "Identity Store ID not found. Skipping group verification."
        echo
    fi
    
    # Step 5: Verify Lake Formation permissions
    verify_lakeformation_permissions "$profile" "$account_id"
    echo
    
    # Step 6: Generate report
    generate_report "$environment" "$profile" "$account_id"
    echo
    
    # Summary
    echo "================================================"
    if [ "$has_errors" = true ]; then
        print_status "warning" "Verification completed with warnings"
        echo -e "${YELLOW}Some components may need additional configuration${NC}"
    else
        print_status "success" "Verification completed successfully!"
        echo -e "${GREEN}Lake Formation Identity Center integration is operational${NC}"
    fi
    
    # Next steps
    echo
    print_status "info" "Recommended next steps:"
    echo "  1. Grant Lake Formation permissions to Identity Center groups"
    echo "  2. Create test databases and tables"
    echo "  3. Verify user access through Identity Center SSO"
    echo "  4. Monitor CloudTrail logs for access patterns"
}

# Run main function
main "$@"