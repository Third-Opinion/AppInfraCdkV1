# Environment Base Stack Deployment Guide

## Overview

The Environment Base Stack recreates the existing VPC infrastructure (vpc-085a37ab90d4186ac) using AWS CDK, providing shared infrastructure resources that multiple application stacks can use within the same environment. This ensures compatibility with existing resources while enabling CDK-managed infrastructure.

## Architecture

```
Environment Base Stack (Recreating vpc-085a37ab90d4186ac)
├── Shared VPC (10.0.0.0/16) - follows naming convention (e.g., "dev-tfv2-vpc-ue2-main")
│   ├── Public Subnets (/20) - us-east-2a, us-east-2b
│   ├── Private Subnets (/20) - us-east-2a, us-east-2b  
│   ├── Isolated Subnets (/25) - us-east-2a, us-east-2b, us-east-2c
│   ├── Internet Gateway - hello-world-igw
│   └── NAT Gateway (1) - in us-east-2a public subnet
├── Security Groups (Matching Existing)
│   ├── AlbSecurityGroup (sg-04e0ab70e85194e27)
│   ├── ContainerFromAlbSecurityGroup (sg-05787d59ddec14f04)
│   ├── rds-ec2-1 (sg-070060ee6c22a9fd7)
│   ├── ecs-to-rds-security-group (sg-0e1f1808e2e77aea1)
│   ├── vpc-endpoint-from-ecs-security-group (sg-056af56a32e37dce2)
│   └── dev-test-trail-finder-v2-security-group (sg-0f94ecfad0e02e821)
├── VPC Endpoints
│   ├── S3 Gateway Endpoint
│   ├── DynamoDB Gateway Endpoint
│   ├── ECR API Interface Endpoint
│   ├── ECR Docker Interface Endpoint
│   └── CloudWatch Logs Interface Endpoint
└── Shared Log Group
```

## Deployment Order

**CRITICAL**: Deploy base stacks BEFORE application stacks.

1. **Environment Base Stack** (`EnvironmentBaseStack`)
2. **Application Stacks** (`WebApplicationStack`, etc.)

## Deployment Commands

### Development Environment

```bash
# Deploy base stack first
aws-cdk deploy EnvironmentBaseStack-dev --profile to-dev-admin

# Deploy application stacks (after base stack)
aws-cdk deploy WebApplicationStack-dev --profile to-dev-admin
```

### Production Environment

```bash
# Deploy base stack first  
aws-cdk deploy EnvironmentBaseStack-prd --profile to-prd-admin

# Deploy application stacks (after base stack)
aws-cdk deploy WebApplicationStack-prd --profile to-prd-admin
```

## Resource Exports

The base stack exports these CloudFormation values for application stacks:

| Export Name | Description |
|-------------|-------------|
| `{env}-vpc-id` | VPC ID |
| `{env}-vpc-cidr` | VPC CIDR block |
| `{env}-public-subnet-ids` | Comma-separated public subnet IDs |
| `{env}-private-subnet-ids` | Comma-separated private subnet IDs |
| `{env}-isolated-subnet-ids` | Comma-separated isolated subnet IDs |
| `{env}-sg-alb-id` | ALB security group ID |
| `{env}-sg-ecs-id` | ECS security group ID |
| `{env}-sg-rds-id` | RDS security group ID |
| `{env}-sg-bastion-id` | Bastion security group ID |
| `{env}-shared-log-group-name` | Shared log group name |

## Validation

Application stacks automatically validate shared resources exist during deployment. If validation fails:

1. Ensure base stack is deployed
2. Check CloudFormation exports exist
3. Verify naming conventions match

## Important Notes

### VPC Infrastructure Recreation
This base stack **recreates** the existing VPC infrastructure (vpc-085a37ab90d4186ac) using CDK. The new VPC will have the same configuration but different resource IDs.

### Existing Resource Compatibility
- CIDR blocks match exactly: 10.0.0.0/16
- Subnet configurations match existing layout
- Security group names and rules replicate current setup
- NAT Gateway configuration matches (1 gateway in us-east-2a)

### Migration Considerations
1. **Parallel Infrastructure**: The new base stack creates parallel infrastructure
2. **Application Migration**: Applications can be migrated gradually to use new resources
3. **DNS/Routing**: Update any hardcoded resource references
4. **Testing**: Thoroughly test applications with new infrastructure before decommissioning old resources

## Troubleshooting

### Base Stack Deployment Fails
- Check IAM permissions for VPC/security group creation
- Verify CIDR block doesn't conflict with existing VPCs (10.0.0.0/16 is already in use)
- Ensure availability zones are available in region
- Consider deploying to different region initially for testing

### Application Stack Can't Find Shared Resources
- Confirm base stack deployment completed successfully
- Check CloudFormation exports in AWS console
- Verify environment name matches between stacks
- Ensure EnvironmentResourceProvider is correctly configured

### Security Group Rules
Recreated security groups match existing ones:
- **AlbSecurityGroup**: HTTP/HTTPS from internet
- **ContainerFromAlbSecurityGroup**: Traffic from ALB only  
- **rds-ec2-1**: PostgreSQL from ECS containers
- **ecs-to-rds-security-group**: Additional RDS access patterns
- **vpc-endpoint-from-ecs-security-group**: HTTPS to VPC endpoints
- **dev-test-trail-finder-v2-security-group**: Testing access (configure as needed)

## Environment-Specific Configuration

### Development
- Single NAT gateway for cost savings
- Shorter log retention (1 week)
- No deletion protection

### Production
- NAT gateways in all AZs for high availability
- Extended log retention (1 month)  
- Deletion protection enabled
- Performance insights for RDS

## Next Steps

After successful base stack deployment:
1. Deploy application stacks
2. Configure bastion security group rules if needed
3. Set up monitoring and alerting
4. Configure backup policies for production