# GitHub Actions Workflows for AppInfraCdkV1

This directory contains GitHub Actions workflows for deploying the AppInfraCdkV1 CDK infrastructure to different environments.

## Overview

The project has been updated to support complete infrastructure separation between TrialFinderV2 and TrialMatch applications. Each application now has its own dedicated deployment workflow that creates isolated infrastructure stacks.

## Available Workflows

### 1. Application-Specific Workflows (Recommended for Separation)

#### `deploy-trialfinder-v2-dev.yml`
- **Purpose**: Deploy TrialFinderV2 application to Development environment
- **Trigger**: Push to `develop` branch with changes to TrialFinderV2-related files
- **Stacks Deployed**:
  1. Base Infrastructure Stack (dedicated VPC, security groups, database)
  2. Application Load Balancer (ALB) Stack
  3. Amazon Cognito Stack
  4. Amazon ECS Stack
  5. Data Stack
- **Infrastructure**: Completely isolated from TrialMatch

#### `deploy-trialmatch-dev.yml`
- **Purpose**: Deploy TrialMatch application to Development environment
- **Trigger**: Push to `develop` branch with changes to TrialMatch-related files
- **Stacks Deployed**:
  1. Base Infrastructure Stack (dedicated VPC, security groups, database)
  2. Application Load Balancer (ALB) Stack
  3. Amazon Cognito Stack
  4. Amazon ECS Stack
  5. Data Stack
- **Infrastructure**: Completely isolated from TrialFinderV2

### 2. Legacy Workflow (Shared Infrastructure)

#### `deploy-dev.yml`
- **Purpose**: Deploy shared infrastructure for both applications (legacy approach)
- **Trigger**: Push to `develop` branch
- **Note**: This workflow will be deprecated once the separation is complete

## Infrastructure Separation Benefits

### Complete Isolation
- **Separate VPCs**: Each application gets its own VPC with different CIDR ranges
- **Independent Security Groups**: No cross-application access possible
- **Dedicated Databases**: Separate Aurora PostgreSQL clusters
- **Isolated VPC Endpoints**: Application-specific service endpoints
- **Independent Scaling**: Each application can scale without affecting the other

### Security Improvements
- **Network Isolation**: Applications cannot communicate at the network level
- **IAM Separation**: Different execution roles and policies per application
- **Audit Trail**: Clear resource ownership and access patterns
- **Compliance**: Better separation of concerns for regulatory requirements

### Operational Benefits
- **Independent Deployments**: Deploy one application without affecting the other
- **Fault Isolation**: Issues in one application don't impact the other
- **Resource Management**: Clear cost attribution per application
- **Team Autonomy**: Development teams can work independently

## Deployment Strategy

### Phase 1: Deploy New Base Stacks
```bash
# Deploy TrialFinderV2 base infrastructure
# This will be triggered automatically when pushing to TrialFinderV2 files

# Deploy TrialMatch base infrastructure  
# This will be triggered automatically when pushing to TrialMatch files
```

### Phase 2: Deploy Application Stacks
Each application workflow automatically deploys all required stacks in the correct order:
1. **Base Stack**: VPC, security groups, database, VPC endpoints
2. **ALB Stack**: Application Load Balancer and related resources
3. **Cognito Stack**: User authentication and authorization
4. **ECS Stack**: Container orchestration and application deployment
5. **Data Stack**: Database configuration and monitoring

### Phase 3: Validation
- Verify complete resource isolation
- Test application functionality in isolated environments
- Validate security group rules and access patterns

## Usage

### Automatic Deployment
The workflows are triggered automatically when you push changes to the `develop` branch:

- **TrialFinderV2 changes**: Triggers `deploy-trialfinder-v2-dev.yml`
- **TrialMatch changes**: Triggers `deploy-trialmatch-dev.yml`
- **Core/Shared changes**: Triggers both workflows (if needed)

### Manual Deployment
You can also trigger deployments manually from the GitHub Actions tab:

1. Go to **Actions** tab in your repository
2. Select the desired workflow
3. Click **Run workflow**
4. Choose the branch and click **Run workflow**

### Monitoring Deployment
Each workflow provides:
- **Real-time logs**: View deployment progress in real-time
- **Artifact outputs**: Download deployment outputs for verification
- **Deployment summary**: Final summary of all deployed resources
- **Error handling**: Clear error messages and rollback guidance

## Configuration

### Environment Variables
- `CDK_ENVIRONMENT`: Target environment (Development, Staging, Production)
- `AWS_REGION`: AWS region for deployment (us-east-2)
- `APP_NAME`: Application name (TrialFinderV2 or TrialMatch)

### AWS Credentials
- Uses GitHub OIDC for secure AWS authentication
- Assumes the `dev-cdk-role-ue2-github-actions` IAM role
- No long-term AWS credentials stored in the repository

### Path-Based Triggers
Each workflow only triggers when relevant files change:
- **TrialFinderV2**: `AppInfraCdkV1.Apps/TrialFinderV2/**`
- **TrialMatch**: `AppInfraCdkV1.Apps/TrialMatch/**`
- **Core**: `AppInfraCdkV1.Core/**`, `AppInfraCdkV1.Stacks/**`

## Migration from Shared Infrastructure

### Current State
- Both applications share the same VPC (`dev-shared-vpc-ue2-main`)
- Shared security groups, database, and VPC endpoints
- Single deployment workflow for all infrastructure

### Target State
- Each application has its own dedicated VPC
- Independent security groups and database clusters
- Separate deployment workflows for complete isolation

### Migration Steps
1. **Deploy new base stacks** using application-specific workflows
2. **Test application functionality** in isolated environments
3. **Validate complete separation** of resources
4. **Remove old shared infrastructure** once migration is complete
5. **Update documentation** and team processes

## Troubleshooting

### Common Issues

#### Workflow Not Triggering
- Check if file changes are in the correct paths
- Verify branch name is `develop`
- Check workflow file syntax and permissions

#### Deployment Failures
- Review CDK diff output for unexpected changes
- Check AWS credentials and permissions
- Verify environment configuration

#### Resource Conflicts
- Ensure unique naming conventions per application
- Check for existing resources with same names
- Verify CIDR ranges don't overlap

### Support
- Check workflow logs for detailed error messages
- Review CDK deployment outputs
- Consult the separation plan document for architecture details

## Future Enhancements

### Production Workflows
- Create production versions of application-specific workflows
- Add approval gates and additional security measures
- Implement blue-green deployment strategies

### Multi-Environment Support
- Support for staging environment deployments
- Environment-specific configuration management
- Cross-environment promotion workflows

### Monitoring and Alerting
- Integration with CloudWatch for deployment monitoring
- Slack/Teams notifications for deployment status
- Automated rollback on critical failures

## Conclusion

The new application-specific workflows provide a robust foundation for complete infrastructure separation between TrialFinderV2 and TrialMatch applications. This approach delivers better security, operational independence, and scalability while maintaining the reliability of the CDK deployment process.

For questions or issues, refer to the main separation plan document or contact the infrastructure team.
