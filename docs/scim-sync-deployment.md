# SCIM Sync Deployment Guide

## Overview

The SCIM Sync stack is now fully integrated into the main CDK deployment system. This guide explains how to deploy and configure the SCIM synchronization between Google Workspace and AWS Identity Center.

**IMPORTANT**: SCIM Sync is a **Production-only** service. It runs exclusively in the production account (442042533707) to synchronize Google Workspace users and groups to AWS Identity Center, which manages access across all AWS accounts in the organization.

## Architecture

The SCIM Sync stack creates:
- Lambda function for synchronization logic
- EventBridge rule for scheduled execution
- SSM Parameter Store for secure configuration
- CloudWatch Logs for monitoring
- IAM roles with appropriate permissions

## Deployment Process

### Step 1: Deploy the Stack via CDK

The SCIM Sync stack is deployed through the main CDK deployment program (Production only):

```bash
# Navigate to the deployment directory
cd AppInfraCdkV1.Deploy

# Synthesize the stack for Production
dotnet run -- --app=ScimSync --environment=Production

# Deploy using AWS CDK
AWS_PROFILE=to-prd-admin cdk deploy
```

The stack will be named: `prod-scim-sync-ue2`

### Step 2: Configure SSM Parameters

After the stack is deployed, configure the SCIM synchronization parameters:

```bash
# Navigate to scripts directory
cd tools/AppInfraCdkV1.Tools.Common/scripts

# Configure for production (only environment available)
./configure-scim-sync.sh prod configure
```

You will be prompted for:
1. **Google Workspace Configuration**:
   - Domain name (e.g., `yourcompany.com`)
   - Path to service account JSON key file

2. **AWS Identity Center Configuration**:
   - SCIM endpoint URL (from Identity Center console)
   - SCIM access token (from Identity Center console)

3. **Sync Settings**:
   - Group filter regex (default: `.*` for all groups)
   - Sync frequency in minutes (default: 30)
   - Whether to enable sync immediately

### Step 3: Test the Deployment

Verify the synchronization is working:

```bash
# Test the deployment
./configure-scim-sync.sh prod test

# View stack outputs
./configure-scim-sync.sh prod show-outputs
```

## Configuration Management

### View Current Configuration

```bash
# List all parameters
aws ssm get-parameters-by-path \
  --path "/scim-sync/production" \
  --recursive \
  --profile to-prd-admin

# Get specific parameter
aws ssm get-parameter \
  --name "/scim-sync/production/sync/enabled" \
  --profile to-prd-admin
```

### Update Configuration

Use the configure script to update parameters:

```bash
./configure-scim-sync.sh prod configure
```

Or update individual parameters:

```bash
# Update sync frequency
aws ssm put-parameter \
  --name "/scim-sync/production/sync/frequency-minutes" \
  --value "15" \
  --overwrite \
  --profile to-prd-admin

# Update group filter
aws ssm put-parameter \
  --name "/scim-sync/production/sync/group-filters" \
  --value "^(engineering|devops)-.*" \
  --overwrite \
  --profile to-prd-admin
```

## Enable/Disable Synchronization

### Disable Sync

```bash
# Disable via script (recommended)
./configure-scim-sync.sh prod disable

# Or manually via SSM
aws ssm put-parameter \
  --name "/scim-sync/production/sync/enabled" \
  --value "false" \
  --overwrite \
  --profile to-prd-admin
```

### Enable Sync

```bash
# Enable via script (recommended)
./configure-scim-sync.sh prod enable

# Or manually via SSM
aws ssm put-parameter \
  --name "/scim-sync/production/sync/enabled" \
  --value "true" \
  --overwrite \
  --profile to-prd-admin
```

## Monitoring

### CloudWatch Logs

```bash
# Get function name
FUNCTION_NAME=$(aws cloudformation describe-stacks \
  --stack-name prod-scim-sync-ue2 \
  --query "Stacks[0].Outputs[?OutputKey=='ScimSyncFunctionName'].OutputValue" \
  --output text \
  --profile to-prd-admin)

# Tail logs
aws logs tail /aws/lambda/$FUNCTION_NAME \
  --follow \
  --profile to-prd-admin
```

### Manual Invocation

```bash
# Trigger manual sync
aws lambda invoke \
  --function-name $FUNCTION_NAME \
  --payload '{"source":"manual","action":"sync"}' \
  --profile to-prd-admin \
  output.json

# View result
cat output.json | jq
```

## Troubleshooting

### Common Issues

1. **Stack Not Found**
   - Ensure the stack is deployed: `AWS_PROFILE=to-prd-admin cdk list`
   - Deploy if missing: `AWS_PROFILE=to-prd-admin cdk deploy`

2. **Invalid Google Credentials**
   - Verify service account has Directory API access
   - Check domain-wide delegation is configured
   - Ensure correct admin email in service account

3. **SCIM Token Issues**
   - Tokens expire after 90 days
   - Generate new token in AWS Identity Center console
   - Update via: `./configure-scim-sync.sh prod configure`

4. **Groups Not Syncing**
   - Check group filter regex
   - Verify groups exist in Google Workspace
   - Check CloudWatch logs for specific errors

### Debug Commands

```bash
# Check EventBridge rule status
aws events describe-rule \
  --name $(aws cloudformation describe-stacks \
    --stack-name prod-scim-sync-ue2 \
    --query "Stacks[0].Outputs[?OutputKey=='SyncScheduleRuleName'].OutputValue" \
    --output text \
    --profile to-prd-admin) \
  --profile to-prd-admin

# Check Lambda function configuration
aws lambda get-function-configuration \
  --function-name $FUNCTION_NAME \
  --profile to-prd-admin

# View recent errors
aws logs filter-log-events \
  --log-group-name /aws/lambda/$FUNCTION_NAME \
  --filter-pattern "ERROR" \
  --start-time $(date -u -d '1 hour ago' +%s)000 \
  --profile to-prd-admin
```

## Updating the Stack

When changes are made to the ScimSyncStack code:

```bash
# Rebuild and redeploy
cd AppInfraCdkV1.Deploy
dotnet build
dotnet run -- --app=ScimSync --environment=Production
AWS_PROFILE=to-prd-admin cdk diff  # Review changes
AWS_PROFILE=to-prd-admin cdk deploy # Apply changes
```

## Security Notes

1. **Service Account Key**: Stored encrypted in SSM Parameter Store
2. **SCIM Token**: Stored encrypted in SSM Parameter Store
3. **IAM Permissions**: Lambda has minimal required permissions
4. **Audit Trail**: All actions logged in CloudTrail
5. **Token Rotation**: Set reminders for 90-day SCIM token renewal

## Cost Estimation

- Lambda: ~1,440 invocations/month @ 10s each = ~$0.50
- SSM Parameters: Free tier
- CloudWatch Logs: ~100MB/month = ~$0.05
- EventBridge: Free tier

**Total: ~$1/month per environment**