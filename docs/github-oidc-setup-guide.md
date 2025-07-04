# GitHub OIDC Authentication Setup Guide

This guide describes how to set up and use GitHub OpenID Connect (OIDC) authentication for deploying AWS CDK applications without storing AWS credentials in GitHub Secrets.

## Overview

Instead of using long-lived AWS access keys stored in GitHub Secrets, this solution uses GitHub's OIDC provider to authenticate directly with AWS, providing temporary credentials for each workflow run.

### Benefits

- **No stored credentials**: Eliminates the risk of credential exposure
- **Short-lived tokens**: Credentials expire after 1 hour maximum
- **Automatic rotation**: No manual key rotation required
- **Fine-grained permissions**: Different permissions per branch/environment
- **Audit trail**: All role assumptions are logged in CloudTrail

## Architecture

```
GitHub Actions Workflow
        |
        | (1) Request OIDC token
        v
GitHub OIDC Provider
        |
        | (2) Issue JWT token
        v
AWS STS (Security Token Service)
        |
        | (3) Assume IAM Role
        v
IAM Role (github-actions-dev-deploy or github-actions-prod-deploy)
        |
        | (4) Temporary credentials
        v
AWS Resources (CDK deployment)
```

## Setup Instructions

### Prerequisites

- AWS CLI installed and configured
- Appropriate AWS permissions to create IAM roles and OIDC providers
- Access to both development (615299752206) and production (442042533707) AWS accounts

### Step 1: Deploy OIDC Infrastructure

1. Navigate to the infrastructure directory:
   ```bash
   cd infrastructure
   ```

2. For Development Account (615299752206):
   ```bash
   # Configure AWS CLI for development account
   aws configure --profile dev-admin
   
   # Deploy the CloudFormation stack
   ./deploy-oidc-setup.sh
   ```

3. For Production Account (442042533707):
   ```bash
   # Configure AWS CLI for production account
   aws configure --profile prod-admin
   
   # Deploy the CloudFormation stack
   ./deploy-oidc-setup.sh
   ```

The script will automatically detect which account you're in and deploy the appropriate CloudFormation template.

### Step 2: Verify GitHub Workflows

The GitHub workflows have been updated to use OIDC authentication:

1. **deploy-dev.yml**: Uses `arn:aws:iam::615299752206:role/github-actions-dev-deploy`
2. **deploy-prod.yml**: Uses `arn:aws:iam::442042533707:role/github-actions-prod-deploy`
3. **infrastructure-pr.yml**: Uses the development role for validation

Each workflow includes the required permissions:
```yaml
permissions:
  id-token: write   # Required for OIDC
  contents: read    # Required for checkout
```

### Step 3: Remove Old Credentials (After Testing)

Once OIDC authentication is working correctly:

1. Go to GitHub repository settings
2. Navigate to Secrets and variables > Actions
3. Delete the following secrets (after confirming OIDC works):
   - `AWS_ACCESS_KEY_ID`
   - `AWS_SECRET_ACCESS_KEY`
   - `PROD_AWS_ACCESS_KEY_ID` (if exists)
   - `PROD_AWS_SECRET_ACCESS_KEY` (if exists)

## How It Works

### 1. GitHub Requests OIDC Token

When a workflow runs, GitHub Actions requests an OIDC token that includes claims about:
- Repository name
- Branch or tag
- Workflow trigger (push, pull_request, etc.)
- Actor (who triggered the workflow)

### 2. AWS Validates Token

The IAM role's trust policy validates:
- The token is from GitHub's OIDC provider
- The audience claim is `sts.amazonaws.com`
- The subject claim matches allowed patterns (branches/tags)

### 3. Role Assumption

If validation passes, AWS STS issues temporary credentials with the permissions defined in the role's policies.

## Security Configuration

### Development Role Trust Policy

Allows deployments from:
- `develop` branch
- `development` branch
- Any `feature/*` branch
- Pull requests

### Production Role Trust Policy

Allows deployments only from:
- `master` branch
- `main` branch (for future compatibility)

### IAM Permissions

Both roles have permissions scoped to environment-specific resources:
- CloudFormation stacks prefixed with environment name
- S3 buckets following naming conventions
- IAM roles with environment prefixes
- Secrets Manager with tag-based access control

## Troubleshooting

### Common Issues

1. **"Could not assume role" error**
   - Verify the OIDC provider is correctly configured
   - Check the role ARN in the workflow matches the deployed role
   - Ensure the branch name matches the trust policy

2. **"AccessDenied" during deployment**
   - Review CloudTrail logs for specific permission errors
   - Check if resources follow the expected naming conventions
   - Verify the role has necessary permissions for the operation

3. **"Invalid JWT token" error**
   - Ensure `permissions.id-token: write` is set in the workflow
   - Check if the workflow is running from an allowed branch

### Debugging Steps

1. Check CloudFormation stack status:
   ```bash
   aws cloudformation describe-stacks --stack-name github-oidc-dev-setup
   ```

2. Verify OIDC provider:
   ```bash
   aws iam list-open-id-connect-providers
   ```

3. Review role trust policy:
   ```bash
   aws iam get-role --role-name github-actions-dev-deploy
   ```

## Maintenance

### Updating Trust Policies

To add new branches or modify access patterns:

1. Update the CloudFormation template
2. Redeploy using `./deploy-oidc-setup.sh`
3. Changes take effect immediately

### Adding New Applications

When adding new applications to the CDK project:
1. Ensure they follow the naming conventions (environment prefixes)
2. Update the workflow matrix to include the new app
3. No changes needed to OIDC configuration

## References

- [GitHub OIDC Documentation](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [AWS GitHub Actions](https://github.com/aws-actions/configure-aws-credentials#assuming-a-role)
- [IAM OIDC Identity Providers](https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_providers_create_oidc.html)