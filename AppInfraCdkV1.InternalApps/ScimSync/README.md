# SCIM Sync Stack - Google Workspace to AWS Identity Center

## Overview

The SCIM Sync Stack implements automated user and group synchronization from Google Workspace to AWS IAM Identity Center using the [slashdevops/idp-scim-sync](https://github.com/slashdevops/idp-scim-sync) solution.

## Architecture

```
Google Workspace Directory
         ↓
    Lambda Function
   (idp-scim-sync)
         ↓
  AWS Identity Center
    (via SCIM API)
```

## Features

- **Automated Synchronization**: EventBridge scheduled rule triggers sync every 15-30 minutes
- **Secure Configuration**: SSM Parameter Store for sensitive configuration data
- **Multi-Environment Support**: Separate configurations for dev, staging, and production
- **Comprehensive Logging**: CloudWatch Logs for monitoring and troubleshooting
- **Group Filtering**: Regex-based filtering to sync specific Google Workspace groups
- **Three-Tier Disable**: Multiple levels to disable sync (SSM parameter, EventBridge rule, Lambda concurrency)

## Components

### 1. Lambda Function
- Uses `slashdevops/idp-scim-sync` container image from ECR
- 512 MB memory allocation
- 5-minute timeout for sync operations
- Environment-specific naming convention

### 2. EventBridge Schedule
- Production: Every 15 minutes
- Non-Production: Every 30 minutes
- Can be disabled via AWS Console or CLI

### 3. SSM Parameters
Configuration stored in `/scim-sync/{environment}/`:
- `google/service-account-key`: Google Workspace service account JSON (SecureString)
- `google/domain`: Google Workspace domain name
- `aws/identity-center-scim-endpoint`: AWS Identity Center SCIM endpoint URL
- `aws/identity-center-scim-token`: SCIM access token (SecureString)
- `sync/enabled`: Enable/disable flag
- `sync/group-filters`: Regex pattern for group filtering
- `sync/frequency-minutes`: Sync frequency

### 4. IAM Permissions
- **Lambda Execution Role**: 
  - SSM Parameter Store read access
  - CloudWatch Logs write access
  - Identity Center SCIM API permissions
- **Google Workspace**: Service account with Directory API read access

## Deployment

### Prerequisites

1. **Google Workspace Setup**:
   - Create a service account in Google Cloud Console
   - Enable Directory API
   - Grant domain-wide delegation with scope: `https://www.googleapis.com/auth/admin.directory.group.readonly`
   - Download service account JSON key

2. **AWS Identity Center Setup**:
   - Enable automatic provisioning in Identity Center
   - Generate SCIM endpoint URL and access token
   - Note: Token expires after 90 days

3. **Build Tools**:
   - .NET 8.0 SDK
   - AWS CDK CLI (`npm install -g aws-cdk`)
   - AWS CLI configured with appropriate profiles

### Deployment Steps

1. **Deploy the Stack**:
```bash
cd AppInfraCdkV1.Tools/scripts
./deploy-scim-sync.sh dev deploy
```

2. **Configure SSM Parameters**:
```bash
./deploy-scim-sync.sh dev update-config
```
You'll be prompted for:
- Google Workspace domain
- Path to service account JSON file
- AWS Identity Center SCIM endpoint
- SCIM access token
- Group filter regex (optional)
- Sync frequency (optional)

3. **Test the Deployment**:
```bash
./deploy-scim-sync.sh dev test
```

4. **Monitor Logs**:
```bash
aws logs tail /aws/lambda/dev-scim-lambda-ue2-internal --follow --profile to-dev-admin
```

## Configuration

### Group Filtering

Use regex patterns to control which groups are synchronized:

- `.*` - Sync all groups (default)
- `^aws-.*` - Sync groups starting with "aws-"
- `(dev|test|prod)-.*` - Sync groups starting with dev-, test-, or prod-
- `^(?!temp-).*` - Sync all groups except those starting with "temp-"

### Disable Synchronization

Three methods to disable sync:

1. **SSM Parameter** (Recommended):
```bash
aws ssm put-parameter \
  --name "/scim-sync/development/sync/enabled" \
  --value "false" \
  --overwrite \
  --profile to-dev-admin
```

2. **EventBridge Rule**:
```bash
aws events disable-rule \
  --name dev-scim-events-rule-ue2-internal \
  --profile to-dev-admin
```

3. **Lambda Concurrency** (Emergency):
```bash
aws lambda put-function-concurrency \
  --function-name dev-scim-lambda-ue2-internal \
  --reserved-concurrent-executions 0 \
  --profile to-dev-admin
```

## Monitoring

### CloudWatch Metrics
- Lambda invocations
- Lambda errors
- Lambda duration
- Lambda throttles

### CloudWatch Logs
Log group: `/aws/lambda/{environment}-scim-lambda-{region}-internal`

Key log patterns to monitor:
- `ERROR` - Sync failures
- `WARNING` - Partial sync issues
- `INFO` - Normal operations
- `Group created` - New groups synchronized
- `User created` - New users synchronized

### Alarms (Optional)
Consider creating CloudWatch alarms for:
- Lambda error rate > 5%
- Lambda duration > 4 minutes
- Lambda throttles > 0
- No successful invocations in 1 hour

## Troubleshooting

### Common Issues

1. **Invalid Google Credentials**:
   - Verify service account JSON is correct
   - Check domain-wide delegation is configured
   - Ensure Directory API is enabled

2. **SCIM Token Expired**:
   - Generate new token in AWS Identity Center
   - Update SSM parameter with new token
   - Tokens expire after 90 days

3. **Groups Not Syncing**:
   - Check group filter regex pattern
   - Verify Google Workspace groups exist
   - Review CloudWatch logs for specific errors

4. **Rate Limiting**:
   - AWS Identity Center has API rate limits
   - Reduce sync frequency if hitting limits
   - Consider implementing pagination for large directories

### Debug Commands

```bash
# View Lambda environment variables
aws lambda get-function-configuration \
  --function-name dev-scim-lambda-ue2-internal \
  --profile to-dev-admin

# Check SSM parameters
aws ssm get-parameters-by-path \
  --path "/scim-sync/development" \
  --recursive \
  --with-decryption \
  --profile to-dev-admin

# Manually invoke Lambda
aws lambda invoke \
  --function-name dev-scim-lambda-ue2-internal \
  --payload '{"source":"manual","action":"sync"}' \
  --profile to-dev-admin \
  output.json

# View recent errors
aws logs filter-log-events \
  --log-group-name /aws/lambda/dev-scim-lambda-ue2-internal \
  --filter-pattern "ERROR" \
  --start-time $(date -u -d '1 hour ago' +%s)000 \
  --profile to-dev-admin
```

## Security Considerations

1. **Service Account Key**: 
   - Stored encrypted in SSM Parameter Store
   - Never commit to version control
   - Rotate periodically

2. **SCIM Token**:
   - Stored encrypted in SSM Parameter Store
   - Expires after 90 days
   - Set calendar reminder for renewal

3. **Network Security**:
   - Lambda runs in AWS managed VPC
   - Outbound HTTPS only to Google APIs and AWS services
   - No inbound connections

4. **Audit Trail**:
   - CloudTrail logs all API calls
   - CloudWatch Logs retention: 30 days
   - Consider longer retention for production

## Cost Estimation

Monthly costs (approximate):
- Lambda invocations: ~1,440 invocations × $0.20/1M = $0.00
- Lambda duration: ~1,440 × 10s × 512MB = ~$0.50
- CloudWatch Logs: ~100MB × $0.50/GB = $0.05
- SSM Parameters: Free tier
- EventBridge: Free tier

**Total: ~$1/month per environment**

## References

- [slashdevops/idp-scim-sync](https://github.com/slashdevops/idp-scim-sync)
- [AWS Identity Center SCIM](https://docs.aws.amazon.com/singlesignon/latest/developerguide/scim.html)
- [Google Workspace Directory API](https://developers.google.com/admin-sdk/directory)
- [AWS CDK Documentation](https://docs.aws.amazon.com/cdk/latest/guide/)