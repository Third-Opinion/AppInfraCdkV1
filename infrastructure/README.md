# Infrastructure Setup

This directory contains CloudFormation templates and scripts for setting up GitHub OIDC authentication for AWS deployments.

## Files

- `github-oidc-setup.yaml` - Combined template for reference (do not deploy directly)
- `dev-account-oidc-setup.yaml` - CloudFormation template for development account
- `prod-account-oidc-setup.yaml` - CloudFormation template for production account  
- `deploy-oidc-setup.sh` - Deployment script that auto-detects AWS account

## Quick Start

1. Ensure AWS CLI is configured with appropriate credentials
2. Run the deployment script:
   ```bash
   ./deploy-oidc-setup.sh
   ```

The script will:
- Detect your current AWS account
- Deploy the appropriate CloudFormation template
- Output the IAM role ARN for GitHub workflows

## Manual Deployment

If you prefer to deploy manually:

### Development Account
```bash
aws cloudformation deploy \
  --template-file dev-account-oidc-setup.yaml \
  --stack-name github-oidc-dev-setup \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameter-overrides \
    GitHubOrg=Third-Opinion \
    GitHubRepo=AppInfraCdkV1
```

### Production Account
```bash
aws cloudformation deploy \
  --template-file prod-account-oidc-setup.yaml \
  --stack-name github-oidc-prod-setup \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameter-overrides \
    GitHubOrg=Third-Opinion \
    GitHubRepo=AppInfraCdkV1
```

## Role ARNs

After deployment, use these role ARNs in your GitHub workflows:

- **Development**: `arn:aws:iam::615299752206:role/github-actions-dev-deploy`
- **Production**: `arn:aws:iam::442042533707:role/github-actions-prod-deploy`

## See Also

- [GitHub OIDC Setup Guide](../docs/github-oidc-setup-guide.md) - Comprehensive setup and troubleshooting guide