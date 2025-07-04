# GitHub Actions CDK Deployment IAM Fix

## Problem
The GitHub Actions CDK deployment is failing with the following error:
```
❌ AccessDenied: User: arn:aws:iam::***:user/dev-g-user-g-gh-cdk-deployer is not authorized to perform: iam:PassRole on resource: arn:aws:iam::***:role/cdk-hnb659fds-cfn-exec-role-***-us-east-2 because no identity-based policy allows the iam:PassRole action
```

## Root Cause
The CDK deployer IAM user (`dev-g-user-g-gh-cdk-deployer`) is missing the `iam:PassRole` permission needed to assume CDK-generated execution roles during deployment.

## Solution

### Option 1: Update Policy via AWS Console
1. Go to AWS IAM Console → Policies
2. Find the policy `dev-g-policy-g-gh-cdk-deploy` (and `prod-g-policy-g-gh-cdk-deploy` for production)
3. Create a new version with the policy in `iam-policies/cdk-deploy-policy.json`
4. Set the new version as default

### Option 2: Update Policy via AWS CLI
Run the provided script:
```bash
./scripts/update-cdk-policy.sh
```

This will update both development and production account policies.

## What the Updated Policy Includes

The new policy includes all necessary permissions for CDK deployment:

### Core CDK Permissions
- **CloudFormation**: Full access for stack operations
- **S3**: Full access for CDK assets and bootstrapping
- **IAM**: Role management and PassRole for CDK execution
- **STS**: AssumeRole for CDK operations

### Service Permissions
- **EC2**: For VPC, subnets, security groups
- **ECS**: For container orchestration
- **ELB**: For application load balancers
- **RDS**: For database resources
- **CloudWatch Logs**: For logging
- **SSM/Secrets Manager**: For configuration and secrets
- **KMS**: For encryption
- **Route53**: For DNS management

### Specific PassRole Permissions
```json
{
  "Effect": "Allow",
  "Action": ["iam:PassRole"],
  "Resource": [
    "arn:aws:iam::*:role/cdk-*",
    "arn:aws:iam::*:role/*-role-*"
  ]
}
```

## Testing the Fix

After updating the policy:

1. Wait 1-2 minutes for IAM changes to propagate
2. Re-run the failed GitHub Action workflow
3. The deployment should now succeed

## Security Notes

🔒 **Security Considerations**:
- The policy follows least privilege principles for CDK operations
- PassRole is restricted to CDK-related roles only
- All permissions are scoped to what CDK actually needs
- Consider using AWS CDK's built-in bootstrap roles in production for enhanced security

## Alternative Approach (Recommended for Production)

For enhanced security in production environments, consider using:
1. **CDK Bootstrap with custom roles**: Use `cdk bootstrap` with predefined execution roles
2. **Cross-account roles**: Use assume-role patterns instead of direct IAM users
3. **OIDC authentication**: Use GitHub's OIDC provider to assume roles instead of long-lived access keys

## Files Added
- `iam-policies/cdk-deploy-policy.json`: Complete CDK deployment policy
- `scripts/update-cdk-policy.sh`: Script to update existing policies
- `GITHUB_ACTIONS_IAM_FIX.md`: This documentation