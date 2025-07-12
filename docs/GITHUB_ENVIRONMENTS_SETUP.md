# GitHub Environment Setup for OIDC Authentication

This document explains how to configure GitHub Environment Secrets and variables for OIDC-based AWS authentication.

## Overview

The repository uses GitHub OpenID Connect (OIDC) to authenticate with AWS without storing long-lived credentials. This setup uses IAM roles with trust policies that allow GitHub Actions to assume them securely.

## GitHub Environment Configuration

### 1. Create Environments

Navigate to your repository's **Settings** → **Environments** and create:

#### Development Environment
- **Name**: `development`
- **Deployment branches**: `develop`, `feature/*` (selected branches and tags)
- **Environment variables**:
  - `AWS_ACCOUNT_ID`: `615299752206`
- **No secrets required** (OIDC authentication)

#### Production Environment  
- **Name**: `production`
- **Deployment branches**: `master`, `main` (selected branches)
- **Environment variables**:
  - `AWS_ACCOUNT_ID`: `442042533707`
- **No secrets required** (OIDC authentication)

### 2. Environment Protection Rules

#### Development Environment
- **Required reviewers**: None (for development speed)
- **Wait timer**: None
- **Deployment protection rules**: Allow all

#### Production Environment (Recommended)
- **Required reviewers**: Team members who must approve production deployments
- **Wait timer**: 0-5 minutes (optional delay for review)
- **Deployment protection rules**: Require approval for production

## Workflow Mapping

| Workflow File | Branch | Environment | AWS Account | IAM Role |
|---------------|--------|-------------|-------------|----------|
| `infrastructure-pr.yml` | Any | `development` | 615299752206 | `dev-tfv2-role-ue2-github-actions` |
| `deploy-dev.yml` | `develop` | `development` | 615299752206 | `dev-tfv2-role-ue2-github-actions` |
| `deploy-prod.yml` | `master` | `production` | 442042533707 | `prod-tfv2-role-ue2-github-actions` |

## OIDC Authentication Flow

```
GitHub Actions Workflow
        ↓
GitHub OIDC Provider
        ↓ (JWT Token with claims)
AWS STS AssumeRoleWithWebIdentity
        ↓ (Trust policy validation)
IAM Role (dev/prod-tfv2-role-ue2-github-actions)
        ↓ (Temporary credentials)
AWS Resources (CDK deployment)
```

## Security Benefits

- ✅ **No stored credentials**: No AWS access keys in GitHub Secrets
- ✅ **Short-lived tokens**: Credentials expire after 1 hour maximum
- ✅ **Automatic rotation**: No manual key rotation required
- ✅ **Branch-based access**: Different permissions per environment/branch
- ✅ **Audit trail**: All role assumptions logged in CloudTrail
- ✅ **Fine-grained permissions**: Environment-specific resource access

## IAM Roles Setup

### Naming Convention

**Pattern**: `{env}-tfv2-role-{region}-{purpose}`

Where:
- **{env}**: Environment prefix (`dev`, `prod`)
- **tfv2**: Application identifier (TrialFinderV2)
- **{region}**: Region code (`ue2` for us-east-2)
- **{purpose}**: Role purpose (`github-actions`)

### Required IAM Roles

#### Development Account (615299752206)
- **Role Name**: `dev-tfv2-role-ue2-github-actions`
- **Purpose**: CDK infrastructure deployments and ECS application deployments
- **Trust Policy**: Allows GitHub Actions from `develop`, `feature/*` branches, and PRs
- **Permissions**: Comprehensive CDK and ECS permissions for development environment

#### Production Account (442042533707)
- **Role Name**: `prod-tfv2-role-ue2-github-actions`
- **Purpose**: CDK infrastructure deployments and ECS application deployments
- **Trust Policy**: Allows GitHub Actions from `master`/`main` branches only
- **Permissions**: Comprehensive CDK and ECS permissions for production environment

### Trust Policy Example

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
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

## Setup Instructions

### 1. Deploy OIDC Infrastructure

The IAM roles and OIDC provider are deployed using CloudFormation templates in the `infrastructure/` directory.

```bash
# Deploy to development account
cd infrastructure
./deploy-oidc-setup.sh

# Deploy to production account (with appropriate AWS profile)
./deploy-oidc-setup.sh
```

### 2. Verify Environment Variables

Ensure the GitHub environment variables are correctly set:

1. Go to repository **Settings** → **Environments**
2. For `development` environment: Set `AWS_ACCOUNT_ID` = `615299752206`
3. For `production` environment: Set `AWS_ACCOUNT_ID` = `442042533707`

### 3. Test Authentication

Create a test PR or push to `develop` to verify OIDC authentication works correctly.

## Migration from IAM Users

### ✅ Completed Migration Steps

1. ✅ Deployed OIDC provider and IAM roles
2. ✅ Updated all GitHub workflows to use OIDC authentication
3. ✅ Configured GitHub environment variables
4. ✅ Tested OIDC authentication in all environments

### 🧹 Cleanup (Optional)

The old IAM users and access keys can be safely removed:

#### Development Account
- `dev-g-user-g-gh-cdk-deployer`
- `dev-g-user-g-gh-ecs-deployer`
- Associated access keys and policies

#### Production Account
- `prod-g-user-g-gh-cdk-deployer`
- `prod-g-user-g-gh-ecs-deployer`
- Associated access keys and policies

## Troubleshooting

### Common Issues

1. **"Could not assume role" error**
   - Verify the role ARN matches the deployed role
   - Check branch name matches trust policy
   - Ensure OIDC provider is correctly configured

2. **"AccessDenied" during deployment**
   - Review CloudTrail logs for specific permissions
   - Check resource naming follows conventions
   - Verify role has necessary permissions

3. **Environment variable not found**
   - Confirm `AWS_ACCOUNT_ID` is set in the correct GitHub environment
   - Check environment name matches workflow configuration

### Debugging Commands

```bash
# Check OIDC provider
aws iam list-open-id-connect-providers

# Verify role exists
aws iam get-role --role-name dev-tfv2-role-ue2-github-actions

# Check trust policy
aws iam get-role --role-name dev-tfv2-role-ue2-github-actions --query 'Role.AssumeRolePolicyDocument'
```

## References

- [GitHub OIDC Documentation](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [AWS configure-aws-credentials Action](https://github.com/aws-actions/configure-aws-credentials)
- [OIDC Setup Guide](docs/github-oidc-setup-guide.md)