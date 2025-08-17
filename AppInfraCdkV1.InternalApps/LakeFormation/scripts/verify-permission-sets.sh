#!/bin/bash
# verify-permission-sets.sh
# Validates that all required Lake Formation permission sets exist in Identity Center

set -e

INSTANCE_ARN="arn:aws:sso:::instance/ssoins-66849025a110d385"

echo "🔍 Checking for Lake Formation permission sets in Identity Center..."
echo "Instance ARN: $INSTANCE_ARN"
echo ""

# Required permission sets
declare -a REQUIRED_PERMISSION_SETS=(
    "LakeFormation-DataAnalyst-Dev-PermissionSet"
    "LakeFormation-DataEngineer-Dev-PermissionSet"
    "LakeFormation-DataAnalyst-Prod-PermissionSet"
    "LakeFormation-DataEngineer-Prod-PermissionSet"
    "LakeFormation-Admin-Prod-PermissionSet"
)

found_count=0
total_count=${#REQUIRED_PERMISSION_SETS[@]}

# Get all permission sets
permission_sets=$(aws sso-admin list-permission-sets --instance-arn "$INSTANCE_ARN" --query 'PermissionSets' --output text)

if [ -z "$permission_sets" ]; then
    echo "❌ No permission sets found in Identity Center"
    exit 1
fi

echo "📋 Checking required permission sets:"
echo ""

for required_set in "${REQUIRED_PERMISSION_SETS[@]}"; do
    found=false
    
    for permission_set_arn in $permission_sets; do
        name=$(aws sso-admin describe-permission-set \
            --instance-arn "$INSTANCE_ARN" \
            --permission-set-arn "$permission_set_arn" \
            --query 'PermissionSet.Name' --output text 2>/dev/null)
        
        if [ "$name" = "$required_set" ]; then
            echo "✅ $required_set"
            echo "   ARN: $permission_set_arn"
            
            # Get additional details
            duration=$(aws sso-admin describe-permission-set \
                --instance-arn "$INSTANCE_ARN" \
                --permission-set-arn "$permission_set_arn" \
                --query 'PermissionSet.SessionDuration' --output text)
            echo "   Session Duration: $duration"
            
            found=true
            ((found_count++))
            break
        fi
    done
    
    if [ "$found" = false ]; then
        echo "❌ $required_set (NOT FOUND)"
    fi
    echo ""
done

echo "📊 Summary:"
echo "Found: $found_count / $total_count permission sets"

if [ $found_count -eq $total_count ]; then
    echo "🎉 All required permission sets are configured!"
    exit 0
else
    echo "⚠️  Missing $(($total_count - $found_count)) permission set(s)"
    echo "Please refer to the setup documentation to create missing permission sets."
    exit 1
fi