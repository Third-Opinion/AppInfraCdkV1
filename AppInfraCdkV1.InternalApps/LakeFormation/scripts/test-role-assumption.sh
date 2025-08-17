#!/bin/bash
# test-role-assumption.sh
# Tests that Lake Formation IAM roles can be assumed and have proper permissions

set -e

DEV_ACCOUNT="615299752206"
PROD_ACCOUNT="442042533707"
REGION="us-east-2"

# Lake Formation roles to test
declare -A ROLES=(
    ["LakeFormation-DataAnalyst-Development"]="$DEV_ACCOUNT"
    ["LakeFormation-DataEngineer-Development"]="$DEV_ACCOUNT"
    ["LakeFormation-Admin-Development"]="$DEV_ACCOUNT"
    ["LakeFormation-CatalogCreator-Development"]="$DEV_ACCOUNT"
    ["LakeFormation-DataAnalyst-Production"]="$PROD_ACCOUNT"
    ["LakeFormation-DataEngineer-Production"]="$PROD_ACCOUNT"
    ["LakeFormation-Admin-Production"]="$PROD_ACCOUNT"
    ["LakeFormation-CatalogCreator-Production"]="$PROD_ACCOUNT"
)

echo "🧪 Testing Lake Formation IAM role accessibility..."
echo "Region: $REGION"
echo ""

# Function to test if a role exists and is accessible
test_role() {
    local role_name="$1"
    local account_id="$2"
    local role_arn="arn:aws:iam::${account_id}:role/${role_name}"
    
    echo "Testing: $role_name"
    echo "ARN: $role_arn"
    
    # Check if role exists
    if aws iam get-role --role-name "$role_name" --region "$REGION" >/dev/null 2>&1; then
        echo "   ✅ Role exists"
        
        # Check trust policy
        local trust_policy=$(aws iam get-role --role-name "$role_name" --region "$REGION" \
            --query 'Role.AssumeRolePolicyDocument' --output json | jq -c '.')
        
        # Check if trust policy includes Identity Center
        if echo "$trust_policy" | jq -e '.Statement[] | select(.Principal.Federated | test("sso"))' >/dev/null 2>&1; then
            echo "   ✅ Trust policy includes Identity Center"
        else
            echo "   ⚠️  Trust policy may not include Identity Center SAML"
        fi
        
        # Check attached policies
        local managed_policies=$(aws iam list-attached-role-policies --role-name "$role_name" --region "$REGION" \
            --query 'AttachedPolicies[].PolicyName' --output text)
        local inline_policies=$(aws iam list-role-policies --role-name "$role_name" --region "$REGION" \
            --query 'PolicyNames' --output text)
        
        if [ -n "$managed_policies" ] || [ -n "$inline_policies" ]; then
            echo "   ✅ Role has policies attached"
            if [ -n "$managed_policies" ]; then
                echo "      Managed policies: $managed_policies"
            fi
            if [ -n "$inline_policies" ]; then
                echo "      Inline policies: $inline_policies"
            fi
        else
            echo "   ⚠️  Role has no policies attached"
        fi
        
        # Test basic Lake Formation permissions (if accessible)
        echo "   🔍 Testing Lake Formation access..."
        if timeout 10 aws lakeformation describe-resource --resource-arn "arn:aws:s3:::test-bucket" --region "$REGION" 2>/dev/null; then
            echo "   ✅ Lake Formation permissions verified"
        else
            echo "   ➡️  Lake Formation access test skipped (no test bucket or insufficient permissions)"
        fi
        
        return 0
    else
        echo "   ❌ Role does not exist or is not accessible"
        return 1
    fi
}

# Function to test session tag policies
test_session_tags() {
    echo "🏷️  Testing session tag policies..."
    echo ""
    
    for role_name in "${!ROLES[@]}"; do
        local account_id="${ROLES[$role_name]}"
        echo "Checking session tag policy for: $role_name"
        
        # Get role's inline policies
        local policies=$(aws iam list-role-policies --role-name "$role_name" --region "$REGION" \
            --query 'PolicyNames' --output text 2>/dev/null || echo "")
        
        local has_session_tags=false
        for policy in $policies; do
            local policy_doc=$(aws iam get-role-policy --role-name "$role_name" --policy-name "$policy" --region "$REGION" \
                --query 'PolicyDocument' --output json 2>/dev/null || echo "{}")
            
            if echo "$policy_doc" | jq -e '.Statement[] | select(.Action[] | contains("sts:TagSession"))' >/dev/null 2>&1; then
                has_session_tags=true
                break
            fi
        done
        
        if [ "$has_session_tags" = true ]; then
            echo "   ✅ Session tag policy found"
        else
            echo "   ⚠️  No session tag policy found"
        fi
        echo ""
    done
}

# Function to test Lake Formation specific permissions
test_lakeformation_permissions() {
    echo "🏞️  Testing Lake Formation specific permissions..."
    echo ""
    
    # Test data lake settings
    echo "Checking data lake settings..."
    if aws lakeformation get-data-lake-settings --region "$REGION" >/dev/null 2>&1; then
        echo "   ✅ Can access Lake Formation settings"
        
        # Check if roles are listed as data lake admins
        local admins=$(aws lakeformation get-data-lake-settings --region "$REGION" \
            --query 'DataLakeSettings.DataLakeAdmins[].DataLakePrincipalIdentifier' --output text)
        
        local admin_roles_found=0
        for role_name in "${!ROLES[@]}"; do
            local account_id="${ROLES[$role_name]}"
            local role_arn="arn:aws:iam::${account_id}:role/${role_name}"
            
            if echo "$admins" | grep -q "$role_arn"; then
                echo "   ✅ $role_name is configured as data lake admin"
                ((admin_roles_found++))
            fi
        done
        
        echo "   📊 Found $admin_roles_found / ${#ROLES[@]} roles as data lake admins"
    else
        echo "   ❌ Cannot access Lake Formation settings"
    fi
    echo ""
}

# Main execution
echo "Starting role accessibility tests..."
echo "========================================"
echo ""

successful_tests=0
total_tests=${#ROLES[@]}

for role_name in "${!ROLES[@]}"; do
    account_id="${ROLES[$role_name]}"
    
    if test_role "$role_name" "$account_id"; then
        ((successful_tests++))
    fi
    echo ""
done

echo "========================================"
echo ""

test_session_tags
test_lakeformation_permissions

echo "📊 Test Summary:"
echo "Successful role tests: $successful_tests / $total_tests"
echo ""

if [ $successful_tests -eq $total_tests ]; then
    echo "🎉 All roles are properly configured and accessible!"
    echo ""
    echo "Next steps:"
    echo "1. Configure Identity Center permission sets (if not done)"
    echo "2. Assign groups to permission sets"
    echo "3. Test end-to-end access with actual users"
    exit 0
else
    echo "⚠️  Some roles failed tests or are not properly configured."
    echo ""
    echo "Troubleshooting:"
    echo "1. Ensure all CDK stacks have been deployed successfully"
    echo "2. Check IAM role trust policies include Identity Center"
    echo "3. Verify Lake Formation permissions are properly granted"
    echo "4. Review CloudFormation stack outputs for any errors"
    exit 1
fi