# Bootstrap Role Setup Guide

## Overview

The bootstrap role is a manually created IAM role that enables CDK deployments across all projects in an AWS account. This role must exist before any CDK stacks can be deployed.

## Why Manual Creation?

The bootstrap role cannot be created by CDK itself because:
1. **Chicken and egg problem**: CDK needs a role to create the first stack
2. **Cross-project sharing**: One role serves all CDK projects in the account
3. **Minimal permissions**: Only needs basic CloudFormation and IAM permissions

## Role Requirements

### Naming Convention
- **Development**: `dev-cdk-role-{region-code}-bootstrap`
- **Production**: `prod-cdk-role-{region-code}-bootstrap`

**Region Codes**:
- `us-east-1` → `ue1`
- `us-east-2` → `ue2`
- `us-west-1` → `uw1`
- `us-west-2` → `uw2`

### Required Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "CloudFormationCDKOperations",
      "Effect": "Allow",
      "Action": [
        "cloudformation:CreateStack",
        "cloudformation:UpdateStack",
        "cloudformation:DeleteStack",
        "cloudformation:DescribeStacks",
        "cloudformation:DescribeStackEvents",
        "cloudformation:ListStacks",
        "cloudformation:GetTemplate",
        "cloudformation:ValidateTemplate",
        "cloudformation:DescribeStackResources",
        "cloudformation:ListStackResources"
      ],
      "Resource": "*"
    },
    {
      "Sid": "S3CDKBootstrapOperations",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::cdk-*",
        "arn:aws:s3:::cdk-*/*"
      ]
    },
    {
      "Sid": "IAMRoleManagement",
      "Effect": "Allow",
      "Action": [
        "iam:CreateRole",
        "iam:DeleteRole",
        "iam:GetRole",
        "iam:AttachRolePolicy",
        "iam:DetachRolePolicy",
        "iam:PutRolePolicy",
        "iam:DeleteRolePolicy",
        "iam:TagRole",
        "iam:UntagRole"
      ],
      "Resource": "*"
    },
    {
      "Sid": "IAMPassRoleRestricted",
      "Effect": "Allow",
      "Action": [
        "iam:PassRole"
      ],
      "Resource": [
        "arn:aws:iam::*:role/ecsTaskExecutionRole",
        "arn:aws:iam::*:role/*-service-role",
        "arn:aws:iam::*:role/*task-role",
        "arn:aws:iam::*:role/*execution-role"
      ]
    },
    {
      "Sid": "EC2ReadOnly",
      "Effect": "Allow",
      "Action": [
        "ec2:DescribeVpcs",
        "ec2:DescribeSubnets",
        "ec2:DescribeSecurityGroups",
        "ec2:DescribeAvailabilityZones"
      ],
      "Resource": "*"
    }
  ]
}
```

### Trust Policy (Complete)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "",
      "Effect": "Allow",
      "Principal": {
        "Service": "ecs.amazonaws.com"
      },
      "Action": "sts:AssumeRole"
    },
    {
      "Sid": "AllowGitHubActionsOIDC",
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main",
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop",
            "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*",
            "repo:Third-Opinion/AppInfraCdkV1:pull_request"
          ]
        }
      }
    }
  ]
}
```

**Important**: 
- The trust policy includes both ECS service principal and GitHub Actions OIDC
- The GitHub Actions condition includes `"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*"` to allow the infrastructure PR workflow to run on all branches
- This is necessary because the `infrastructure-pr.yml` workflow runs on all branches (`branches: ['**']`) for validation purposes

## Manual Setup Process

### Prerequisites
- AWS CLI configured with appropriate permissions
- Account ID for the target environment
- GitHub organization and repository information

### Step 1: Create OIDC Provider (if not exists)

First, check if the OIDC provider already exists:

```bash
aws iam get-open-id-connect-provider --open-id-connect-provider-arn "arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
```

If it doesn't exist, create it:

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
```

### Step 2: Create Trust Policy File

Create a temporary file for the trust policy:

```bash
# For Windows PowerShell
Set-Content -Path "trust-policy.json" -Value '{"Version":"2012-10-17","Statement":[{"Sid":"","Effect":"Allow","Principal":{"Service":"ecs.amazonaws.com"},"Action":"sts:AssumeRole"},{"Sid":"AllowGitHubActionsOIDC","Effect":"Allow","Principal":{"Federated":"arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"},"Action":"sts:AssumeRoleWithWebIdentity","Condition":{"StringEquals":{"token.actions.githubusercontent.com:aud":"sts.amazonaws.com"},"StringLike":{"token.actions.githubusercontent.com:sub":["repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main","repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop","repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*","repo:Third-Opinion/AppInfraCdkV1:pull_request"]}}}]}'

# For Linux/macOS
echo '{"Version":"2012-10-17","Statement":[{"Sid":"","Effect":"Allow","Principal":{"Service":"ecs.amazonaws.com"},"Action":"sts:AssumeRole"},{"Sid":"AllowGitHubActionsOIDC","Effect":"Allow","Principal":{"Federated":"arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"},"Action":"sts:AssumeRoleWithWebIdentity","Condition":{"StringEquals":{"token.actions.githubusercontent.com:aud":"sts.amazonaws.com"},"StringLike":{"token.actions.githubusercontent.com:sub":["repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main","repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop","repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*","repo:Third-Opinion/AppInfraCdkV1:pull_request"]}}}]}' > trust-policy.json
```

**Important**: Replace `ACCOUNT_ID` with your actual AWS account ID.

### Step 3: Create Access Policy File

Create a temporary file for the access policy:

```bash
# For Windows PowerShell
Set-Content -Path "access-policy.json" -Value '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Action":["cloudformation:*","s3:*","iam:CreateRole","iam:DeleteRole","iam:GetRole","iam:PassRole","iam:AttachRolePolicy","iam:DetachRolePolicy","iam:PutRolePolicy","iam:DeleteRolePolicy","iam:TagRole","iam:UntagRole"],"Resource":"*"}]}'

# For Linux/macOS
echo '{"Version":"2012-10-17","Statement":[{"Sid":"CloudFormationCDKOperations","Effect":"Allow","Action":["cloudformation:CreateStack","cloudformation:UpdateStack","cloudformation:DeleteStack","cloudformation:DescribeStacks","cloudformation:DescribeStackEvents","cloudformation:ListStacks","cloudformation:GetTemplate","cloudformation:ValidateTemplate","cloudformation:DescribeStackResources","cloudformation:ListStackResources"],"Resource":"*"},{"Sid":"S3CDKBootstrapOperations","Effect":"Allow","Action":["s3:GetObject","s3:PutObject","s3:DeleteObject","s3:ListBucket"],"Resource":["arn:aws:s3:::cdk-*","arn:aws:s3:::cdk-*/*"]},{"Sid":"IAMRoleManagement","Effect":"Allow","Action":["iam:CreateRole","iam:DeleteRole","iam:GetRole","iam:AttachRolePolicy","iam:DetachRolePolicy","iam:PutRolePolicy","iam:DeleteRolePolicy","iam:TagRole","iam:UntagRole"],"Resource":"*"},{"Sid":"IAMPassRoleRestricted","Effect":"Allow","Action":["iam:PassRole"],"Resource":["arn:aws:iam::*:role/ecsTaskExecutionRole","arn:aws:iam::*:role/*-service-role","arn:aws:iam::*:role/*task-role","arn:aws:iam::*:role/*execution-role"]},{"Sid":"EC2ReadOnly","Effect":"Allow","Action":["ec2:DescribeVpcs","ec2:DescribeSubnets","ec2:DescribeSecurityGroups","ec2:DescribeAvailabilityZones"],"Resource":"*"}]}' > access-policy.json
```

### Step 4: Create Bootstrap Role

Create the IAM role with the trust policy:

```bash
aws iam create-role \
  --role-name dev-cdk-role-ue2-bootstrap \
  --assume-role-policy-document file://trust-policy.json \
  --description "Bootstrap role for CDK deployments in dev environment" \
  --max-session-duration 3600
```

### Step 5: Create Access Policy

Create the IAM policy:

```bash
aws iam create-policy \
  --policy-name dev-cdk-policy-ue2-bootstrap \
  --policy-document file://access-policy.json \
  --description "Bootstrap policy for CDK deployments in dev environment"
```

### Step 6: Attach Policy to Role

Attach the policy to the role:

```bash
aws iam attach-role-policy \
  --role-name dev-cdk-role-ue2-bootstrap \
  --policy-arn arn:aws:iam::ACCOUNT_ID:policy/dev-cdk-policy-ue2-bootstrap
```

### Step 7: Add Tags to Role

Add appropriate tags to the role:

```bash
aws iam tag-role \
  --role-name dev-cdk-role-ue2-bootstrap \
  --tags Key=ManagedBy,Value=Manual Key=Purpose,Value=CDK-Bootstrap Key=Environment,Value=dev Key=Service,Value=CDK
```

### Step 8: Clean Up Temporary Files

Remove the temporary files:

```bash
# For Windows PowerShell
Remove-Item trust-policy.json -Force
Remove-Item access-policy.json -Force

# For Linux/macOS
rm trust-policy.json access-policy.json
```

## Environment-Specific Setup

### Development Environment
- **Role Name**: `dev-cdk-role-ue2-bootstrap`
- **Policy Name**: `dev-cdk-policy-ue2-bootstrap`
- **Environment**: `dev`

### Production Environment
- **Role Name**: `prod-cdk-role-ue2-bootstrap`
- **Policy Name**: `prod-cdk-policy-ue2-bootstrap`
- **Environment**: `prod`

## Update Environment Configuration

Add the bootstrap role ARN to your environment configuration:

```json
{
  "environments": {
    "dev": {
      "bootstrapRoleArn": "arn:aws:iam::ACCOUNT_ID:role/dev-cdk-role-ue2-bootstrap"
    },
    "prod": {
      "bootstrapRoleArn": "arn:aws:iam::ACCOUNT_ID:role/prod-cdk-role-ue2-bootstrap"
    }
  }
}
```

## Usage in GitHub Actions

The bootstrap role ARN should be stored as a GitHub secret and used in CDK deployment workflows:

```yaml
- name: Deploy CDK Stack
  env:
    AWS_ROLE_ARN: ${{ secrets.CDK_BOOTSTRAP_ROLE_ARN }}
  run: |
    aws sts assume-role --role-arn $AWS_ROLE_ARN --role-session-name cdk-deploy
    # ... CDK deployment commands
```

## Security Considerations

1. **Minimal permissions**: The bootstrap role has only the minimum permissions needed
2. **OIDC authentication**: Uses GitHub Actions OIDC for secure authentication
3. **Session duration**: Limited to 1 hour maximum
4. **Audit trail**: All role assumptions are logged in CloudTrail
5. **Scoped S3 access**: Limited to CDK bootstrap buckets (`cdk-*`) only
6. **Specific CloudFormation operations**: Only necessary CDK operations allowed
7. **Read-only EC2 access**: Only descriptive operations for VPC/subnet discovery

### Security Improvements Over Previous Version

The current policy includes several security improvements over the previous overly broad permissions:

- **CloudFormation**: Changed from `cloudformation:*` to specific operations only
- **S3**: Changed from `s3:*` to only `cdk-*` buckets
- **IAM**: Kept necessary role management permissions but scoped to CDK operations
- **IAM PassRole**: Restricted to specific role patterns using resource ARNs instead of service conditions for better security
- **EC2**: Added read-only access for VPC/subnet discovery (required by CDK)
- **Resource scoping**: S3 access is limited to CDK bootstrap buckets only

## Troubleshooting

### Common Issues

1. **Role not found**: Ensure the bootstrap role exists before running CDK
2. **Permission denied**: Verify the trust policy allows your GitHub repository
3. **OIDC provider missing**: Create the OIDC provider before creating the role
4. **JSON formatting errors**: Ensure JSON files are properly formatted with no extra characters

### Validation Commands

```bash
# Check if role exists
aws iam get-role --role-name dev-cdk-role-ue2-bootstrap

# Test role assumption
aws sts assume-role --role-arn arn:aws:iam::ACCOUNT_ID:role/dev-cdk-role-ue2-bootstrap --role-session-name test

# List attached policies
aws iam list-attached-role-policies --role-name dev-cdk-role-ue2-bootstrap

# Get role tags
aws iam list-role-tags --role-name dev-cdk-role-ue2-bootstrap
```

## Example: Complete Setup for Development

Here's a complete example for setting up the development bootstrap role:

```bash
# Set variables
ACCOUNT_ID="615299752206"
ENVIRONMENT="dev"
REGION_CODE="ue2"

# Create trust policy
Set-Content -Path "trust-policy.json" -Value "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"\",\"Effect\":\"Allow\",\"Principal\":{\"Service\":\"ecs.amazonaws.com\"},\"Action\":\"sts:AssumeRole\"},{\"Sid\":\"AllowGitHubActionsOIDC\",\"Effect\":\"Allow\",\"Principal\":{\"Federated\":\"arn:aws:iam::$ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com\"},\"Action\":\"sts:AssumeRoleWithWebIdentity\",\"Condition\":{\"StringEquals\":{\"token.actions.githubusercontent.com:aud\":\"sts.amazonaws.com\"},\"StringLike\":{\"token.actions.githubusercontent.com:sub\":[\"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main\",\"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop\",\"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*\",\"repo:Third-Opinion/AppInfraCdkV1:pull_request\"]}}}]}"

# Create access policy
Set-Content -Path "access-policy.json" -Value "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"CloudFormationCDKOperations\",\"Effect\":\"Allow\",\"Action\":[\"cloudformation:CreateStack\",\"cloudformation:UpdateStack\",\"cloudformation:DeleteStack\",\"cloudformation:DescribeStacks\",\"cloudformation:DescribeStackEvents\",\"cloudformation:ListStacks\",\"cloudformation:GetTemplate\",\"cloudformation:ValidateTemplate\",\"cloudformation:DescribeStackResources\",\"cloudformation:ListStackResources\"],\"Resource\":\"*\"},{\"Sid\":\"S3CDKBootstrapOperations\",\"Effect\":\"Allow\",\"Action\":[\"s3:GetObject\",\"s3:PutObject\",\"s3:DeleteObject\",\"s3:ListBucket\"],\"Resource\":[\"arn:aws:s3:::cdk-*\",\"arn:aws:s3:::cdk-*/*\"]},{\"Sid\":\"IAMRoleManagement\",\"Effect\":\"Allow\",\"Action\":[\"iam:CreateRole\",\"iam:DeleteRole\",\"iam:GetRole\",\"iam:AttachRolePolicy\",\"iam:DetachRolePolicy\",\"iam:PutRolePolicy\",\"iam:DeleteRolePolicy\",\"iam:TagRole\",\"iam:UntagRole\"],\"Resource\":\"*\"},{\"Sid\":\"IAMPassRoleRestricted\",\"Effect\":\"Allow\",\"Action\":[\"iam:PassRole\"],\"Resource\":[\"arn:aws:iam::*:role/ecsTaskExecutionRole\",\"arn:aws:iam::*:role/*-service-role\",\"arn:aws:iam::*:role/*task-role\",\"arn:aws:iam::*:role/*execution-role\"]},{\"Sid\":\"EC2ReadOnly\",\"Effect\":\"Allow\",\"Action\":[\"ec2:DescribeVpcs\",\"ec2:DescribeSubnets\",\"ec2:DescribeSecurityGroups\",\"ec2:DescribeAvailabilityZones\"],\"Resource\":\"*\"}]}"

# Create role
aws iam create-role --role-name $ENVIRONMENT-cdk-role-$REGION_CODE-bootstrap --assume-role-policy-document file://trust-policy.json --description "Bootstrap role for CDK deployments in $ENVIRONMENT environment" --max-session-duration 3600

# Create policy
aws iam create-policy --policy-name $ENVIRONMENT-cdk-policy-$REGION_CODE-bootstrap --policy-document file://access-policy.json --description "Bootstrap policy for CDK deployments in $ENVIRONMENT environment"

# Attach policy to role
aws iam attach-role-policy --role-name $ENVIRONMENT-cdk-role-$REGION_CODE-bootstrap --policy-arn arn:aws:iam::$ACCOUNT_ID:policy/$ENVIRONMENT-cdk-policy-$REGION_CODE-bootstrap

# Add tags
aws iam tag-role --role-name $ENVIRONMENT-cdk-role-$REGION_CODE-bootstrap --tags Key=ManagedBy,Value=Manual Key=Purpose,Value=CDK-Bootstrap Key=Environment,Value=$ENVIRONMENT Key=Service,Value=CDK

# Clean up
Remove-Item trust-policy.json -Force
Remove-Item access-policy.json -Force

echo "Bootstrap role created: arn:aws:iam::$ACCOUNT_ID:role/$ENVIRONMENT-cdk-role-$REGION_CODE-bootstrap"
``` 