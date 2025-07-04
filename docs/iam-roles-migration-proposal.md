# IAM Roles Migration Proposal

## Executive Summary

This proposal outlines a migration strategy from IAM users with access keys to IAM roles for improved security and management of AWS resource access in the AppInfraCdkV1 project.

## Current State

Currently, the project uses IAM users with access keys stored in GitHub Secrets:
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`

This approach has several limitations:
- Access keys are long-lived credentials that pose a security risk
- Manual rotation of keys is required
- No automatic credential expiration
- Keys can be compromised if exposed

## Proposed Solution

### 2: GitHub Actions with OpenID Connect (OIDC) - **Recommended**

Use GitHub's OIDC provider to assume IAM roles directly without storing any credentials.

**Benefits:**
- No stored credentials in GitHub Secrets
- Short-lived tokens (1 hour max)
- Automatic credential rotation
- Fine-grained permissions per workflow
- Audit trail through CloudTrail

**Implementation Steps:**

1. **Configure GitHub as OIDC Provider in AWS**
   ```bash
   aws iam create-open-id-connect-provider \
     --url https://token.actions.githubusercontent.com \
     --client-id-list sts.amazonaws.com \
     --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
   ```

2. **Create IAM Roles for Each Environment**
   
   Development Role: `arn:aws:iam::615299752206:role/github-actions-dev-deploy`
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

   Production Role: `arn:aws:iam::442042533707:role/github-actions-prod-deploy`
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
             "token.actions.githubusercontent.com:sub": "repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/master"
           }
         }
       }
     ]
   }
   ```

3. **Update GitHub Workflows**
   
   Replace the current AWS credentials configuration:
   ```yaml
   - name: Configure AWS credentials
     uses: aws-actions/configure-aws-credentials@v4
     with:
       aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
       aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
       aws-region: us-east-1
   ```

   With OIDC role assumption:
   ```yaml
   - name: Configure AWS credentials
     uses: aws-actions/configure-aws-credentials@v4
     with:
       role-to-assume: arn:aws:iam::615299752206:role/github-actions-dev-deploy
       role-session-name: GitHubActions-${{ github.run_id }}
       aws-region: us-east-1
   ```

### Option 2: AWS IAM Identity Center (SSO)

For local development, use AWS IAM Identity Center for developers to assume roles.

**Benefits:**
- Centralized user management
- Temporary credentials
- MFA support
- Integration with corporate identity providers

**Implementation:**
1. Configure AWS IAM Identity Center
2. Create permission sets for developers
3. Map permission sets to AWS accounts and roles
4. Developers use `aws sso login` for authentication

### Option 3: CDK Deployment Role

Create dedicated CDK deployment roles that can be assumed by GitHub Actions.

**Benefits:**
- Follows AWS CDK best practices
- Leverages CDK bootstrap roles
- Consistent with CDK security model

## Security Improvements

1. **Principle of Least Privilege**: Each role has only the permissions needed for its specific purpose
2. **Temporary Credentials**: All credentials expire automatically
3. **No Stored Secrets**: OIDC eliminates the need for stored access keys
4. **Audit Trail**: All role assumptions are logged in CloudTrail
5. **Environment Isolation**: Separate roles for each environment prevent cross-environment access

## Migration Plan

### Phase 1: Setup OIDC Provider (Week 1)
- Configure GitHub OIDC provider in both AWS accounts
- Create IAM roles with existing permissions

### Phase 2: Test in Development (Week 2)
- Update development workflow to use OIDC
- Test all deployment scenarios
- Monitor CloudTrail for any permission issues

### Phase 3: Production Rollout (Week 3)
- Update production workflow
- Remove old IAM user access keys
- Update documentation

### Phase 4: Developer Access (Week 4)
- Configure AWS IAM Identity Center
- Onboard developers
- Create developer documentation

## Rollback Plan

If issues arise during migration:
1. GitHub workflows can temporarily revert to using stored credentials
2. IAM users remain active during migration
3. Gradual rollout allows testing before full commitment

## Cost Implications

- **OIDC**: No additional cost
- **IAM Identity Center**: No additional cost for AWS SSO
- **CloudTrail**: Minimal cost for additional logging

## Recommendations

1. **Start with Option 1 (GitHub OIDC)** for CI/CD workflows as it provides the best security with minimal complexity
2. **Implement Option 2 (IAM Identity Center)** for developer access
3. **Consider Option 3** as a future enhancement for full CDK integration

## Next Steps

1. Get approval for the migration approach
2. Create OIDC provider in both AWS accounts
3. Create and test IAM roles
4. Update workflows incrementally
5. Document the new process

## References

- [GitHub OIDC Documentation](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [AWS GitHub Actions](https://github.com/aws-actions/configure-aws-credentials#assuming-a-role)
- [AWS IAM Identity Center](https://docs.aws.amazon.com/singlesignon/latest/userguide/what-is.html)