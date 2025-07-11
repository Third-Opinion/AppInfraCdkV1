## AWS CLI Usage Guidelines
- aws cli commands should use the appropriate profile:
  - For development tasks, use `to-dev-admin` for account: 615299752206
  - For production tasks, use `to-prd-admin` for account: 442042533707 (ask for confirmation before using)
  - If there is an authentication error, prompt me to run 'aws sso login'

## GitHub Actions Authentication
- GitHub Actions uses OIDC authentication with AWS (no access keys needed)
- Development deployments use role: `dev-tfv2-role-ue2-github-actions`
- Production deployments use role: `prod-tfv2-role-ue2-github-actions`
- GitHub environment variables store AWS account IDs dynamically
  
## Environment Mapping
- `develop` branch → use `to-dev-admin` profile
- `main` branch → use `to-prd-admin` profile


## GitHub CLI Usage Guidelines
- Use `gh` CLI for GitHub operations

## Always run all tests before creating a pull request