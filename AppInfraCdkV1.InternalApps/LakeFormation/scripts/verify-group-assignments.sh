#!/bin/bash
# verify-group-assignments.sh
# Validates that Identity Center groups are properly assigned to Lake Formation permission sets

set -e

INSTANCE_ARN="arn:aws:sso:::instance/ssoins-66849025a110d385"
DEV_ACCOUNT="615299752206"
PROD_ACCOUNT="442042533707"

# Group ID mappings
declare -A GROUP_IDS=(
    ["data-analysts-dev"]="018b8550-9071-70ef-4204-7120281ac19b"
    ["data-engineers-dev"]="613b1560-20b1-70c1-06fb-3ab507e41773"
    ["data-analysts-phi"]="511be500-20f1-707b-80ca-33140a93b483"
    ["data-engineers-phi"]="c12b0510-8081-70d2-945d-77f64fbd73c6"
    ["data-lake-admin-prd"]="d14b65b0-10d1-70da-6801-3b67fa213c71"
)

# Expected assignments: Group -> Account -> Permission Set Names
declare -A EXPECTED_ASSIGNMENTS=(
    ["data-analysts-dev"]="$DEV_ACCOUNT:LakeFormation-DataAnalyst-Dev-PermissionSet"
    ["data-engineers-dev"]="$DEV_ACCOUNT:LakeFormation-DataEngineer-Dev-PermissionSet"
    ["data-analysts-phi"]="$PROD_ACCOUNT:LakeFormation-DataAnalyst-Prod-PermissionSet"
    ["data-engineers-phi"]="$PROD_ACCOUNT:LakeFormation-DataEngineer-Prod-PermissionSet"
    ["data-lake-admin-prd"]="$PROD_ACCOUNT:LakeFormation-Admin-Prod-PermissionSet"
)

echo "🔍 Verifying Identity Center group assignments for Lake Formation..."
echo "Instance ARN: $INSTANCE_ARN"
echo ""

# Function to get permission set name by ARN
get_permission_set_name() {
    local pset_arn="$1"
    aws sso-admin describe-permission-set \
        --instance-arn "$INSTANCE_ARN" \
        --permission-set-arn "$pset_arn" \
        --query 'PermissionSet.Name' --output text 2>/dev/null || echo "Unknown"
}

# Function to check assignments for an account
check_account_assignments() {
    local account_id="$1"
    local account_name="$2"
    
    echo "📋 Checking assignments for $account_name account ($account_id):"
    
    # Get all group assignments for this account
    local assignments=$(aws sso-admin list-account-assignments \
        --instance-arn "$INSTANCE_ARN" \
        --account-id "$account_id" \
        --query 'AccountAssignments[?PrincipalType==`GROUP`]' \
        --output json)
    
    if [ "$assignments" = "[]" ] || [ -z "$assignments" ]; then
        echo "   ❌ No group assignments found"
        return 1
    fi
    
    # Parse assignments
    local found_assignments=0
    echo "$assignments" | jq -r '.[] | "\(.PrincipalId):\(.PermissionSetArn)"' | while read assignment; do
        local group_id=$(echo "$assignment" | cut -d: -f1)
        local pset_arn=$(echo "$assignment" | cut -d: -f2-)
        local pset_name=$(get_permission_set_name "$pset_arn")
        
        # Find group name by ID
        local group_name=""
        for gname in "${!GROUP_IDS[@]}"; do
            if [ "${GROUP_IDS[$gname]}" = "$group_id" ]; then
                group_name="$gname"
                break
            fi
        done
        
        if [ -n "$group_name" ] && [[ "$pset_name" == *"LakeFormation"* ]]; then
            echo "   ✅ $group_name -> $pset_name"
            ((found_assignments++))
        fi
    done
    
    echo "   Found $found_assignments Lake Formation assignments"
    echo ""
}

# Function to validate expected assignments
validate_expected_assignments() {
    echo "🎯 Validating expected group-to-permission-set mappings:"
    echo ""
    
    local validation_passed=0
    local total_expected=${#EXPECTED_ASSIGNMENTS[@]}
    
    for group_name in "${!EXPECTED_ASSIGNMENTS[@]}"; do
        local assignment="${EXPECTED_ASSIGNMENTS[$group_name]}"
        local account_id=$(echo "$assignment" | cut -d: -f1)
        local expected_pset_name=$(echo "$assignment" | cut -d: -f2)
        local group_id="${GROUP_IDS[$group_name]}"
        
        echo "Checking: $group_name ($group_id)"
        echo "Expected: $expected_pset_name in account $account_id"
        
        # Get assignments for this group in the expected account
        local assignments=$(aws sso-admin list-account-assignments \
            --instance-arn "$INSTANCE_ARN" \
            --account-id "$account_id" \
            --query "AccountAssignments[?PrincipalType==\`GROUP\` && PrincipalId==\`$group_id\`]" \
            --output json)
        
        local found_expected=false
        if [ "$assignments" != "[]" ] && [ -n "$assignments" ]; then
            echo "$assignments" | jq -r '.[].PermissionSetArn' | while read pset_arn; do
                local pset_name=$(get_permission_set_name "$pset_arn")
                if [ "$pset_name" = "$expected_pset_name" ]; then
                    echo "   ✅ Correctly assigned"
                    found_expected=true
                    ((validation_passed++))
                    break
                fi
            done
        fi
        
        if [ "$found_expected" = false ]; then
            echo "   ❌ Missing expected assignment"
        fi
        echo ""
    done
    
    echo "📊 Validation Summary:"
    echo "Correctly assigned: $validation_passed / $total_expected"
    
    if [ $validation_passed -eq $total_expected ]; then
        echo "🎉 All expected assignments are configured!"
        return 0
    else
        echo "⚠️  Missing $(($total_expected - $validation_passed)) expected assignment(s)"
        return 1
    fi
}

# Main execution
echo "🔍 Checking all assignments by account..."
echo ""

check_account_assignments "$DEV_ACCOUNT" "Development"
check_account_assignments "$PROD_ACCOUNT" "Production"

echo "=========================================="
echo ""

validate_expected_assignments

exit_code=$?

if [ $exit_code -eq 0 ]; then
    echo ""
    echo "🎉 All group assignments are properly configured!"
else
    echo ""
    echo "⚠️  Some assignments are missing. Please refer to the setup documentation."
    echo "    Run the setup script or configure manually in the Identity Center console."
fi

exit $exit_code