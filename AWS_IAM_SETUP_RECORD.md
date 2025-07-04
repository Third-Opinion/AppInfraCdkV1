# AWS IAM Users and Policies Creation Record

This document records the AWS CLI commands executed to create GitHub Actions deployment users and policies.

**Execution Date**: July 4, 2025  
**Accounts**: Development (615299752206) and Production (442042533707)

## Development Account Setup (using --profile=to-dev-admin)

### 1. Create Users

```bash
# Create CDK Deployer User
aws iam create-user --user-name dev-g-user-g-gh-cdk-deployer --profile=to-dev-admin
```
**Result**: User created with ARN: `arn:aws:iam::615299752206:user/dev-g-user-g-gh-cdk-deployer`

```bash
# Create ECS Deployer User
aws iam create-user --user-name dev-g-user-g-gh-ecs-deployer --profile=to-dev-admin
```
**Result**: User created with ARN: `arn:aws:iam::615299752206:user/dev-g-user-g-gh-ecs-deployer`

### 2. Create Policies

```bash
# Create CDK Deployment Policy
aws iam create-policy --policy-name dev-g-policy-g-gh-cdk-deploy --policy-document file:///tmp/cdk-policy.json --profile=to-dev-admin
```
**Result**: Policy created with ARN: `arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-cdk-deploy`

```bash
# Create ECS Deployment Policy
aws iam create-policy --policy-name dev-g-policy-g-gh-ecs-deploy --policy-document file:///tmp/ecs-policy.json --profile=to-dev-admin
```
**Result**: Policy created with ARN: `arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-ecs-deploy`

### 3. Attach Policies to Users

```bash
# Attach CDK policy to CDK user
aws iam attach-user-policy --user-name dev-g-user-g-gh-cdk-deployer --policy-arn "arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-cdk-deploy" --profile=to-dev-admin

# Attach ECS policy to ECS user
aws iam attach-user-policy --user-name dev-g-user-g-gh-ecs-deployer --policy-arn "arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-ecs-deploy" --profile=to-dev-admin
```
**Result**: Policies attached successfully

### 4. Create Access Keys

```bash
# Create access key for CDK deployer
aws iam create-access-key --user-name dev-g-user-g-gh-cdk-deployer --profile=to-dev-admin
```
**Result**: 
- **Access Key ID**: `AKIAY6QVZEEHFVRB5HT6`
- **Secret Access Key**: See 1Password

```bash
# Create access key for ECS deployer
aws iam create-access-key --user-name dev-g-user-g-gh-ecs-deployer --profile=to-dev-admin
```
**Result**: 
- **Access Key ID**: `AKIAY6QVZEEHLL3UJJ7V`
- **Secret Access Key**: See 1Password

## Production Account Setup (using --profile=to-prd-admin)

### 1. Create Users

```bash
# Create CDK Deployer User
aws iam create-user --user-name prod-g-user-g-gh-cdk-deployer --profile=to-prd-admin
```
**Result**: User created with ARN: `arn:aws:iam::442042533707:user/prod-g-user-g-gh-cdk-deployer`

```bash
# Create ECS Deployer User
aws iam create-user --user-name prod-g-user-g-gh-ecs-deployer --profile=to-prd-admin
```
**Result**: User created with ARN: `arn:aws:iam::442042533707:user/prod-g-user-g-gh-ecs-deployer`

### 2. Create Policies

```bash
# Create CDK Deployment Policy
aws iam create-policy --policy-name prod-g-policy-g-gh-cdk-deploy --policy-document file:///tmp/cdk-policy.json --profile=to-prd-admin
```
**Result**: Policy created with ARN: `arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-cdk-deploy`

```bash
# Create ECS Deployment Policy
aws iam create-policy --policy-name prod-g-policy-g-gh-ecs-deploy --policy-document file:///tmp/ecs-policy.json --profile=to-prd-admin
```
**Result**: Policy created with ARN: `arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-ecs-deploy`

### 3. Attach Policies to Users

```bash
# Attach CDK policy to CDK user
aws iam attach-user-policy --user-name prod-g-user-g-gh-cdk-deployer --policy-arn "arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-cdk-deploy" --profile=to-prd-admin

# Attach ECS policy to ECS user
aws iam attach-user-policy --user-name prod-g-user-g-gh-ecs-deployer --policy-arn "arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-ecs-deploy" --profile=to-prd-admin
```
**Result**: Policies attached successfully

### 4. Create Access Keys

```bash
# Create access key for CDK deployer
aws iam create-access-key --user-name prod-g-user-g-gh-cdk-deployer --profile=to-prd-admin
```
**Result**: 
- **Access Key ID**: `AKIAWN26JY5FZX7T725T`
- **Secret Access Key**: See 1Password

```bash
# Create access key for ECS deployer
aws iam create-access-key --user-name prod-g-user-g-gh-ecs-deployer --profile=to-prd-admin
```
**Result**: 
- **Access Key ID**: `AKIAWN26JY5F7OC3TVNV`
- **Secret Access Key**: See 1Password

## Summary of Created Resources

### Development Account (615299752206)
| Resource Type | Name | ARN/Access Key |
|---------------|------|----------------|
| IAM User | `dev-g-user-g-gh-cdk-deployer` | `arn:aws:iam::615299752206:user/dev-g-user-g-gh-cdk-deployer` |
| IAM User | `dev-g-user-g-gh-ecs-deployer` | `arn:aws:iam::615299752206:user/dev-g-user-g-gh-ecs-deployer` |
| IAM Policy | `dev-g-policy-g-gh-cdk-deploy` | `arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-cdk-deploy` |
| IAM Policy | `dev-g-policy-g-gh-ecs-deploy` | `arn:aws:iam::615299752206:policy/dev-g-policy-g-gh-ecs-deploy` |
| Access Key | CDK Deployer | `AKIAY6QVZEEHFVRB5HT6` |
| Access Key | ECS Deployer | `AKIAY6QVZEEHLL3UJJ7V` |

### Production Account (442042533707)
| Resource Type | Name | ARN/Access Key |
|---------------|------|----------------|
| IAM User | `prod-g-user-g-gh-cdk-deployer` | `arn:aws:iam::442042533707:user/prod-g-user-g-gh-cdk-deployer` |
| IAM User | `prod-g-user-g-gh-ecs-deployer` | `arn:aws:iam::442042533707:user/prod-g-user-g-gh-ecs-deployer` |
| IAM Policy | `prod-g-policy-g-gh-cdk-deploy` | `arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-cdk-deploy` |
| IAM Policy | `prod-g-policy-g-gh-ecs-deploy` | `arn:aws:iam::442042533707:policy/prod-g-policy-g-gh-ecs-deploy` |
| Access Key | CDK Deployer | `AKIAWN26JY5FZX7T725T` |
| Access Key | ECS Deployer | `AKIAWN26JY5F7OC3TVNV` |

## GitHub Environment Secrets Configuration

### Development Environment Secrets
Configure these in GitHub Settings → Environments → `development`:

**For CDK Deployments:**
- `AWS_ACCESS_KEY_ID`: `AKIAY6QVZEEHFVRB5HT6`
- `AWS_SECRET_ACCESS_KEY`: See 1Password

**For ECS Deployments:**
- `AWS_ACCESS_KEY_ID`: `AKIAY6QVZEEHLL3UJJ7V`
- `AWS_SECRET_ACCESS_KEY`: See 1Password

### Production Environment Secrets
Configure these in GitHub Settings → Environments → `production`:

**For CDK Deployments:**
- `AWS_ACCESS_KEY_ID`: `AKIAWN26JY5FZX7T725T`
- `AWS_SECRET_ACCESS_KEY`: See 1Password

**For ECS Deployments:**
- `AWS_ACCESS_KEY_ID`: `AKIAWN26JY5F7OC3TVNV`
- `AWS_SECRET_ACCESS_KEY`: See 1Password

## Security Notes

🔒 **IMPORTANT**: 
- The secret access keys are stored securely in 1Password
- Store them securely in GitHub Environment Secrets only
- Never commit these keys to version control
- Rotate these keys regularly according to your security policy
- Consider using GitHub's dependabot to monitor for exposed secrets

## Next Steps

1. Configure GitHub Environment Secrets as documented above
2. Update your GitHub Actions workflows to use the appropriate environment
3. Test deployments with the new credentials
4. Remove any old/unused IAM users and access keys
5. Set up key rotation schedule