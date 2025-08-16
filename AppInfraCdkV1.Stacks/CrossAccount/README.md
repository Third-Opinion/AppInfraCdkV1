# GitHub Actions OIDC Authentication

This directory contains documentation for setting up GitHub Actions to authenticate with AWS using OpenID Connect (OIDC).

## Current Setup

This project uses GitHub Actions with OIDC authentication to deploy to AWS without storing long-lived credentials. The following roles are configured:

- **Development**: `arn:aws:iam::615299752206:role/dev-cdk-role-ue2-github-actions`
- **Production**: `arn:aws:iam::442042533707:role/prod-tfv2-role-ue2-github-actions`

## Documentation

See [GITHUB_ACTIONS_OIDC_SETUP.md](./GITHUB_ACTIONS_OIDC_SETUP.md) for:
- Complete setup instructions using AWS CLI
- Trust policy configuration
- GitHub Actions workflow examples
- Troubleshooting guide
- Security best practices

## Why OIDC Instead of Access Keys?

1. **No Long-Lived Credentials**: OIDC tokens are short-lived (1 hour max)
2. **Fine-Grained Access Control**: Restrict access to specific branches/environments
3. **Audit Trail**: All actions are traceable through CloudTrail
4. **No Secret Rotation**: No need to rotate access keys
5. **GitHub Native**: Integrated directly into GitHub Actions

## Quick Reference

### Using OIDC in GitHub Actions

```yaml
permissions:
  id-token: write   # Required for OIDC
  contents: read

steps:
  - name: Configure AWS credentials
    uses: aws-actions/configure-aws-credentials@v4
    with:
      role-to-assume: ${{ vars.ROLE_ARN }}
      aws-region: us-east-2
```

### Verifying Role Access

```bash
# Check if role can be assumed
aws sts assume-role-with-web-identity \
    --role-arn arn:aws:iam::615299752206:role/dev-cdk-role-ue2-github-actions \
    --role-session-name test-session \
    --web-identity-token $(gh auth token)
```