# GitHub Actions OIDC Setup Guide

This guide explains how to set up OpenID Connect (OIDC) authentication between GitHub Actions and AWS, eliminating the need for long-lived AWS access keys.

## Overview

GitHub Actions can authenticate to AWS using OIDC tokens, which are more secure than static credentials. This setup involves:
1. Creating an OIDC identity provider in AWS
2. Creating IAM roles that trust GitHub's OIDC provider
3. Configuring GitHub Actions to assume these roles

## Prerequisites

- AWS CLI installed and configured
- Administrative access to your AWS accounts
- Administrative access to your GitHub repository

## Step 1: Create OIDC Identity Provider

Run this command once per AWS account to create the GitHub OIDC provider:

```bash
# For Development Account (615299752206)
AWS_PROFILE=to-dev-admin aws iam create-open-id-connect-provider \
    --url https://token.actions.githubusercontent.com \
    --client-id-list sts.amazonaws.com \
    --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1 \
    --region us-east-2

# For Production Account (442042533707)
AWS_PROFILE=to-prd-admin aws iam create-open-id-connect-provider \
    --url https://token.actions.githubusercontent.com \
    --client-id-list sts.amazonaws.com \
    --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1 \
    --region us-east-2
```

**Note**: If the provider already exists, you'll get an error. That's okay - it means it's already set up.

## Step 2: Create IAM Roles for GitHub Actions

### Development Environment Role

1. Create a trust policy file (`github-trust-policy-dev.json`):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::615299752206:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop",
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/feature/*",
            "repo:Third-Opinion/AppInfraCdkV1:pull_request"
          ]
        }
      }
    }
  ]
}
```

2. Create the role:

```bash
AWS_PROFILE=to-dev-admin aws iam create-role \
    --role-name dev-cdk-role-ue2-github-actions \
    --assume-role-policy-document file://github-trust-policy-dev.json \
    --description "Role for GitHub Actions to deploy to Development environment" \
    --max-session-duration 3600
```

3. Attach necessary policies:

```bash
# Attach PowerUserAccess for CDK deployments
AWS_PROFILE=to-dev-admin aws iam attach-role-policy \
    --role-name dev-cdk-role-ue2-github-actions \
    --policy-arn arn:aws:iam::aws:policy/PowerUserAccess

# Create and attach additional CDK-specific permissions
cat > cdk-deploy-policy.json << 'EOF'
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "iam:CreateRole",
        "iam:DeleteRole",
        "iam:PutRolePolicy",
        "iam:DeleteRolePolicy",
        "iam:GetRole",
        "iam:GetRolePolicy",
        "iam:PassRole",
        "iam:AttachRolePolicy",
        "iam:DetachRolePolicy",
        "iam:ListRolePolicies",
        "iam:ListAttachedRolePolicies",
        "iam:UpdateRole",
        "iam:TagRole",
        "iam:UntagRole"
      ],
      "Resource": "*"
    }
  ]
}
EOF

AWS_PROFILE=to-dev-admin aws iam put-role-policy \
    --role-name dev-cdk-role-ue2-github-actions \
    --policy-name CDKDeploymentPolicy \
    --policy-document file://cdk-deploy-policy.json
```

### Production Environment Role

1. Create a trust policy file (`github-trust-policy-prod.json`):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::442042533707:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main",
            "repo:Third-Opinion/AppInfraCdkV1:environment:production"
          ]
        }
      }
    }
  ]
}
```

2. Create the role:

```bash
AWS_PROFILE=to-prd-admin aws iam create-role \
    --role-name prod-tfv2-role-ue2-github-actions \
    --assume-role-policy-document file://github-trust-policy-prod.json \
    --description "Role for GitHub Actions to deploy to Production environment" \
    --max-session-duration 3600
```

3. Attach policies (same as development):

```bash
AWS_PROFILE=to-prd-admin aws iam attach-role-policy \
    --role-name prod-tfv2-role-ue2-github-actions \
    --policy-arn arn:aws:iam::aws:policy/PowerUserAccess

AWS_PROFILE=to-prd-admin aws iam put-role-policy \
    --role-name prod-tfv2-role-ue2-github-actions \
    --policy-name CDKDeploymentPolicy \
    --policy-document file://cdk-deploy-policy.json
```

## Step 3: Configure GitHub Repository

### Add Repository Variables

In your GitHub repository, go to **Settings > Secrets and variables > Actions > Variables** and add:

- `AWS_ACCOUNT_ID`: The appropriate account ID (automatically set by GitHub environments)

### Configure GitHub Environments (Optional but Recommended)

1. Go to **Settings > Environments**
2. Create `development` environment:
   - Add protection rules if desired
   - Add environment variable: `AWS_ACCOUNT_ID` = `615299752206`
3. Create `production` environment:
   - Add required reviewers
   - Add environment variable: `AWS_ACCOUNT_ID` = `442042533707`

## Step 4: Use in GitHub Actions

Here's how to use OIDC authentication in your GitHub Actions workflows:

```yaml
name: Deploy to AWS

on:
  push:
    branches: [main, develop]

env:
  AWS_REGION: us-east-2

permissions:
  id-token: write   # This is required for requesting the JWT
  contents: read    # This is required for actions/checkout

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: ${{ github.ref == 'refs/heads/main' && 'production' || 'development' }}
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Configure AWS credentials
      uses: aws-actions/configure-aws-credentials@v4
      with:
        role-to-assume: arn:aws:iam::${{ vars.AWS_ACCOUNT_ID }}:role/${{ github.ref == 'refs/heads/main' && 'prod-tfv2-role-ue2-github-actions' || 'dev-cdk-role-ue2-github-actions' }}
        role-session-name: GitHubActions-Deploy-${{ github.run_id }}
        aws-region: ${{ env.AWS_REGION }}
    
    - name: Deploy with CDK
      run: |
        npx cdk deploy --all --require-approval never
```

## Verifying the Setup

### Check OIDC Provider

```bash
# List OIDC providers
AWS_PROFILE=to-dev-admin aws iam list-open-id-connect-providers

# Get details of the GitHub OIDC provider
AWS_PROFILE=to-dev-admin aws iam get-open-id-connect-provider \
    --open-id-connect-provider-arn arn:aws:iam::615299752206:oidc-provider/token.actions.githubusercontent.com
```

### Check Role Configuration

```bash
# Get role details
AWS_PROFILE=to-dev-admin aws iam get-role \
    --role-name dev-cdk-role-ue2-github-actions

# List attached policies
AWS_PROFILE=to-dev-admin aws iam list-attached-role-policies \
    --role-name dev-cdk-role-ue2-github-actions

# List inline policies
AWS_PROFILE=to-dev-admin aws iam list-role-policies \
    --role-name dev-cdk-role-ue2-github-actions
```

## Troubleshooting

### Common Issues

1. **"Could not assume role"**
   - Verify the trust policy matches your repository name exactly
   - Check that the branch/environment conditions are correct
   - Ensure the OIDC provider exists in the account

2. **"The security token included in the request is invalid"**
   - Check that `permissions.id-token: write` is set in the workflow
   - Verify the role ARN is correct

3. **"Access Denied" during deployment**
   - The role may need additional permissions
   - Check CloudTrail logs for specific denied actions

### Viewing CloudTrail Logs

```bash
# Check recent AssumeRoleWithWebIdentity calls
AWS_PROFILE=to-dev-admin aws cloudtrail lookup-events \
    --lookup-attributes AttributeKey=EventName,AttributeValue=AssumeRoleWithWebIdentity \
    --max-items 10
```

## Security Best Practices

1. **Limit Trust Conditions**: Always specify exact repository and branch patterns
2. **Use Environment Protection**: Configure GitHub environment protection rules for production
3. **Principle of Least Privilege**: Grant only necessary permissions to roles
4. **Regular Audits**: Periodically review role permissions and trust policies
5. **Session Duration**: Keep session duration as short as practical (1 hour default)

## Updating Trust Policies

To add or remove branches/repositories that can assume a role:

```bash
# Get current trust policy
AWS_PROFILE=to-dev-admin aws iam get-role \
    --role-name dev-cdk-role-ue2-github-actions \
    --query 'Role.AssumeRolePolicyDocument' > current-trust-policy.json

# Edit the policy file, then update
AWS_PROFILE=to-dev-admin aws iam update-assume-role-policy \
    --role-name dev-cdk-role-ue2-github-actions \
    --policy-document file://updated-trust-policy.json
```

## Removing the Setup

If you need to remove the OIDC setup:

```bash
# Delete roles
AWS_PROFILE=to-dev-admin aws iam delete-role \
    --role-name dev-cdk-role-ue2-github-actions

# Delete OIDC provider (be careful - other applications might use it)
AWS_PROFILE=to-dev-admin aws iam delete-open-id-connect-provider \
    --open-id-connect-provider-arn arn:aws:iam::615299752206:oidc-provider/token.actions.githubusercontent.com
```

## Additional Resources

- [GitHub OIDC Documentation](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [AWS OIDC Provider Documentation](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html)
- [aws-actions/configure-aws-credentials](https://github.com/aws-actions/configure-aws-credentials)