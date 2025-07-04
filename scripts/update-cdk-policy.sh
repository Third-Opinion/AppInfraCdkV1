#!/bin/bash

# Script to update CDK deployment policy with required permissions
# This fixes the iam:PassRole error in GitHub Actions

set -e

echo "🔧 Updating CDK deployment policy..."

# Development Account
echo "📝 Updating Development account policy..."
aws iam create-policy-version \
  --policy-arn "arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-cdk-deploy" \
  --policy-document file://iam-policies/cdk-deploy-policy.json \
  --set-as-default \
  --profile=to-dev-admin

echo "✅ Development account policy updated"

# Production Account  
echo "📝 Updating Production account policy..."
aws iam create-policy-version \
  --policy-arn "arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-cdk-deploy" \
  --policy-document file://iam-policies/cdk-deploy-policy.json \
  --set-as-default \
  --profile=to-prd-admin

echo "✅ Production account policy updated"

echo "🎉 CDK deployment policies updated successfully!"
echo "💡 The GitHub Actions should now be able to deploy CDK stacks."