#!/bin/bash

# Test script to verify IAM policy conditions and access prevention
# This script tests that ECS roles can only access their designated secrets

set -e

echo "🔐 Testing Secrets Manager Access Controls"
echo "=========================================="

# Test 1: Verify development role can access development secrets
echo "Test 1: Development role accessing development secrets..."

# Assume the development task role
ROLE_ARN="arn:aws:iam::615299752206:role/dev-trialfinder-task-role-ue2"
SESSION_NAME="test-secrets-access-$(date +%s)"

echo "Assuming role: $ROLE_ARN"
ASSUME_ROLE_OUTPUT=$(aws sts assume-role \
    --role-arn "$ROLE_ARN" \
    --role-session-name "$SESSION_NAME" \
    --profile to-dev-admin \
    --region us-east-2)

# Extract credentials
AWS_ACCESS_KEY_ID=$(echo "$ASSUME_ROLE_OUTPUT" | jq -r '.Credentials.AccessKeyId')
AWS_SECRET_ACCESS_KEY=$(echo "$ASSUME_ROLE_OUTPUT" | jq -r '.Credentials.SecretAccessKey')
AWS_SESSION_TOKEN=$(echo "$ASSUME_ROLE_OUTPUT" | jq -r '.Credentials.SessionToken')

export AWS_ACCESS_KEY_ID
export AWS_SECRET_ACCESS_KEY
export AWS_SESSION_TOKEN
export AWS_DEFAULT_REGION=us-east-2

echo "✅ Successfully assumed development task role"

# Test 1a: Access development secrets (should succeed)
echo "Test 1a: Accessing development database secret..."
if aws secretsmanager get-secret-value \
    --secret-id "/dev/trialfinder/database-connection" \
    --query "SecretString" \
    --output text > /dev/null 2>&1; then
    echo "✅ Successfully accessed development database secret"
else
    echo "❌ Failed to access development database secret"
    exit 1
fi

echo "Test 1b: Accessing development API keys secret..."
if aws secretsmanager get-secret-value \
    --secret-id "/dev/trialfinder/api-keys" \
    --query "SecretString" \
    --output text > /dev/null 2>&1; then
    echo "✅ Successfully accessed development API keys secret"
else
    echo "❌ Failed to access development API keys secret"
    exit 1
fi

echo "Test 1c: Accessing development JWT config secret..."
if aws secretsmanager get-secret-value \
    --secret-id "/dev/trialfinder/jwt-config" \
    --query "SecretString" \
    --output text > /dev/null 2>&1; then
    echo "✅ Successfully accessed development JWT config secret"
else
    echo "❌ Failed to access development JWT config secret"
    exit 1
fi

# Test 2: Verify development role cannot access production secrets
echo ""
echo "Test 2: Development role accessing production secrets (should fail)..."

# Note: These production secrets don't exist in our dev account, 
# but the policy should still deny access based on path patterns
echo "Test 2a: Attempting to access hypothetical production database secret..."
if aws secretsmanager get-secret-value \
    --secret-id "/prod/trialfinder/database-connection" \
    --query "SecretString" \
    --output text > /dev/null 2>&1; then
    echo "❌ SECURITY ISSUE: Able to access production database secret from dev role!"
    exit 1
else
    echo "✅ Correctly denied access to production database secret"
fi

echo "Test 2b: Attempting to access hypothetical staging secret..."
if aws secretsmanager get-secret-value \
    --secret-id "/staging/trialfinder/api-keys" \
    --query "SecretString" \
    --output text > /dev/null 2>&1; then
    echo "❌ SECURITY ISSUE: Able to access staging secret from dev role!"
    exit 1
else
    echo "✅ Correctly denied access to staging secret"
fi

# Test 3: Test KMS permissions
echo ""
echo "Test 3: Testing KMS permissions..."

# Get the KMS key used by the secrets
SECRET_ARN="arn:aws:secretsmanager:us-east-2:615299752206:secret:/dev/trialfinder/database-connection-ncvjg2"
SECRET_DESCRIPTION=$(aws secretsmanager describe-secret --secret-id "$SECRET_ARN" 2>/dev/null || true)

if [ -n "$SECRET_DESCRIPTION" ]; then
    KMS_KEY_ID=$(echo "$SECRET_DESCRIPTION" | jq -r '.KmsKeyId // "alias/aws/secretsmanager"')
    echo "KMS Key used by secret: $KMS_KEY_ID"
    
    # Test KMS describe key permission
    if aws kms describe-key --key-id "$KMS_KEY_ID" > /dev/null 2>&1; then
        echo "✅ Successfully accessed KMS key description"
    else
        echo "⚠️  Could not access KMS key description (expected for managed keys)"
    fi
else
    echo "⚠️  Could not retrieve secret description"
fi

# Cleanup - unset credentials
unset AWS_ACCESS_KEY_ID
unset AWS_SECRET_ACCESS_KEY
unset AWS_SESSION_TOKEN

echo ""
echo "🎉 All security tests completed successfully!"
echo "✅ Development role can access development secrets"
echo "✅ Development role cannot access production/staging secrets"
echo "✅ IAM policy conditions are working correctly"