# Adress Each of These Issues One by One. 
## Commit after each item is complete\ if there were code changes.
## Store all temporary files in the `temp` directory. and do not commit them.
## Upon completion, update this file marking the item as complete. Do not commit for changes to this file.

# CloudFormation to CDK Level 2 Migration Plan

## Overview
This plan outlines the migration of AWS resources from CloudFormation to CDK Level 2 constructs in C#. The migration follows a dependency-based order, ensuring each component is deployed before its dependents.

## Phase 0: Pre-Migration Resource Inventory

### [ ] 1. Gather All Resource IDs
Create a comprehensive inventory of existing resources:

```bash
# Create resource inventory file with timestamps
echo "=== AWS Resource Inventory - $(date) ===" > resource-inventory.txt

# VPC Resources
echo -e "\n### VPC Resources ###" >> resource-inventory.txt
aws ec2 describe-vpcs --query 'Vpcs[*].[VpcId,CidrBlock,Tags[?Key==`Name`].Value|[0]]' --output table >> resource-inventory.txt

# Subnets
echo -e "\n### Subnets ###" >> resource-inventory.txt
aws ec2 describe-subnets --query 'Subnets[*].[SubnetId,VpcId,CidrBlock,AvailabilityZone,Tags[?Key==`Name`].Value|[0]]' --output table >> resource-inventory.txt

# Route Tables
echo -e "\n### Route Tables ###" >> resource-inventory.txt
aws ec2 describe-route-tables --query 'RouteTables[*].[RouteTableId,VpcId,Tags[?Key==`Name`].Value|[0]]' --output table >> resource-inventory.txt

# Internet Gateways
echo -e "\n### Internet Gateways ###" >> resource-inventory.txt
aws ec2 describe-internet-gateways --query 'InternetGateways[*].[InternetGatewayId,Attachments[0].VpcId,Tags[?Key==`Name`].Value|[0]]' --output table >> resource-inventory.txt

# NAT Gateways
echo -e "\n### NAT Gateways ###" >> resource-inventory.txt
aws ec2 describe-nat-gateways --query 'NatGateways[*].[NatGatewayId,VpcId,SubnetId,State]' --output table >> resource-inventory.txt

# Security Groups
echo -e "\n### Security Groups ###" >> resource-inventory.txt
aws ec2 describe-security-groups --query 'SecurityGroups[*].[GroupId,GroupName,VpcId,Description]' --output table >> resource-inventory.txt

# Load Balancers, be sure this includes the listeners
echo -e "\n### Application Load Balancers ###" >> resource-inventory.txt
aws elbv2 describe-load-balancers --query 'LoadBalancers[*].[LoadBalancerArn,LoadBalancerName,DNSName,State.Code]' --output table >> resource-inventory.txt

# Target Groups
echo -e "\n### Target Groups ###" >> resource-inventory.txt
aws elbv2 describe-target-groups --query 'TargetGroups[*].[TargetGroupArn,TargetGroupName,Port,Protocol]' --output table >> resource-inventory.txt

# RDS Instances
echo -e "\n### RDS Instances ###" >> resource-inventory.txt
aws rds describe-db-instances --query 'DBInstances[*].[DBInstanceIdentifier,Engine,DBInstanceClass,MultiAZ,DBInstanceStatus]' --output table >> resource-inventory.txt

# ECS Clusters
echo -e "\n### ECS Clusters ###" >> resource-inventory.txt
aws ecs list-clusters --query 'clusterArns[*]' --output table >> resource-inventory.txt

# ECS Services (for each cluster)
echo -e "\n### ECS Services ###" >> resource-inventory.txt
for cluster in $(aws ecs list-clusters --query 'clusterArns[*]' --output text); do
    echo "Cluster: $cluster" >> resource-inventory.txt
    aws ecs list-services --cluster $cluster --query 'serviceArns[*]' --output table >> resource-inventory.txt
done
```

### [ ] 2. Export CloudFormation Stacks
```bash
# List all CloudFormation stacks
aws cloudformation list-stacks --stack-status-filter CREATE_COMPLETE UPDATE_COMPLETE --query 'StackSummaries[*].[StackName,StackStatus]' --output table

# Search resource-inventory.txt to find the stacks that are relevant to this migration. Ask for help if you are not sure which stacks to migrate.

# Export each stack template (replace with your stack names)
aws cloudformation get-template --stack-name <vpc-stack-name> --query 'TemplateBody' > vpc-stack-template.json
aws cloudformation get-template --stack-name <security-stack-name> --query 'TemplateBody' > security-stack-template.json
aws cloudformation get-template --stack-name <alb-stack-name> --query 'TemplateBody' > alb-stack-template.json
aws cloudformation get-template --stack-name <rds-stack-name> --query 'TemplateBody' > rds-stack-template.json
aws cloudformation get-template --stack-name <ecs-stack-name> --query 'TemplateBody' > ecs-stack-template.json
```

# STOP HERE for now. We will continue with the VPC migration later.

## Phase 1: VPC and Core Networking

### [ ] 3. Analyze Current VPC Configuration
```bash
# Get detailed VPC configuration
VPC_ID="<your-vpc-id>"  # Set your VPC ID

# VPC Details
aws ec2 describe-vpcs --vpc-ids $VPC_ID > vpc-details.json

# Subnet Details
aws ec2 describe-subnets --filters "Name=vpc-id,Values=$VPC_ID" > subnets-details.json

# Route Tables
aws ec2 describe-route-tables --filters "Name=vpc-id,Values=$VPC_ID" > route-tables.json

# Internet Gateway
aws ec2 describe-internet-gateways --filters "Name=attachment.vpc-id,Values=$VPC_ID" > igw.json

# NAT Gateways
aws ec2 describe-nat-gateways --filter "Name=vpc-id,Values=$VPC_ID" > nat-gateways.json

# VPC Endpoints (if any)
aws ec2 describe-vpc-endpoints --filters "Name=vpc-id,Values=$VPC_ID" > vpc-endpoints.json
```

### [ ] 4. Document VPC Requirements
- [ ] 4.1. VPC CIDR block
- [ ] 4.2. Number of Availability Zones
- [ ] 4.3. Public subnet CIDR blocks and sizes
- [ ] 4.4. Private subnet CIDR blocks and sizes
- [ ] 4.5. Number of NAT Gateways
- [ ] 4.6. VPC endpoint requirements

### [ ] 6. Create VPC CDK Stack
- [ ] 6.1. Create `VpcStack.cs` with explicit configurations
- [ ] 6.2. Define all subnets with specific CIDR masks
- [ ] 6.3. Configure NAT gateways (avoid default "one per AZ")
- [ ] 6.4. Set up route tables explicitly

### [ ] 7. Deploy VPC Stack
TBD approach - STOP here for now, we will continue with the VPC deployment later.


### [ ] 8. Validate VPC Deployment
```bash
# Get new VPC ID
NEW_VPC_ID=$(aws cloudformation describe-stacks --stack-name VpcStack --query 'Stacks[0].Outputs[?OutputKey==`VpcId`].OutputValue' --output text)

# Compare configurations
aws ec2 describe-vpcs --vpc-ids $NEW_VPC_ID
aws ec2 describe-subnets --filters "Name=vpc-id,Values=$NEW_VPC_ID"
```

---

## Phase 2: Security Groups

### [ ] 9. Export Security Group Rules
```bash
# Export all security groups with rules
aws ec2 describe-security-groups --filters "Name=vpc-id,Values=$VPC_ID" --query 'SecurityGroups[*].[GroupId,GroupName,IpPermissions,IpPermissionsEgress]' > security-groups-rules.json

# Create security group dependency map
echo "=== Security Group Dependencies ===" > sg-dependencies.txt
aws ec2 describe-security-groups --filters "Name=vpc-id,Values=$VPC_ID" --query 'SecurityGroups[*].[GroupName,IpPermissions[?UserIdGroupPairs].UserIdGroupPairs[].GroupId]' --output text >> sg-dependencies.txt
```

### [ ] 10. Document Security Requirements
- [ ] 10.1. ALB Security Group rules (inbound/outbound)
- [ ] 10.2. ECS Security Group rules
- [ ] 10.3. RDS Security Group rules
- [ ] 10.4. Inter-group dependencies

### [ ] 11. Create Security Groups CDK Stack
- [ ] 11.1. Create `SecurityGroupsStack.cs`
- [ ] 11.2. Define ALB security group (HTTP/HTTPS from internet)
- [ ] 11.3. Define ECS security group (from ALB only)
- [ ] 11.4. Define RDS security group (from ECS only)
- [ ] 11.5. Explicitly set all rules (no defaults)

### [ ] 12. Deploy Security Groups
```bash
cdk deploy SecurityGroupsStack
```

### [ ] 13. Validate Security Groups
```bash
# List new security groups
aws ec2 describe-security-groups --filters "Name=tag:aws:cloudformation:stack-name,Values=SecurityGroupsStack" --query 'SecurityGroups[*].[GroupId,GroupName]' --output table

# Verify rules match requirements
aws ec2 describe-security-groups --filters "Name=tag:aws:cloudformation:stack-name,Values=SecurityGroupsStack" > new-security-groups.json
```

---

## Phase 3: Application Load Balancer

### [ ] 14. Export ALB Configuration
```bash
# Get ALB details
ALB_ARN="<your-alb-arn>"  # Set your ALB ARN

# ALB Configuration
aws elbv2 describe-load-balancers --load-balancer-arns $ALB_ARN > alb-config.json

# Target Groups
aws elbv2 describe-target-groups --load-balancer-arn $ALB_ARN > target-groups.json

# Listeners
aws elbv2 describe-listeners --load-balancer-arn $ALB_ARN > listeners.json

# Listener Rules
for listener in $(aws elbv2 describe-listeners --load-balancer-arn $ALB_ARN --query 'Listeners[*].ListenerArn' --output text); do
    aws elbv2 describe-rules --listener-arn $listener > listener-rules-$listener.json
done
```

### [ ] 15. Document ALB Requirements
- [ ] 15.1. Internet-facing or internal
- [ ] 15.2. Subnets for ALB
- [ ] 15.3. Target group configuration
- [ ] 15.4. Health check settings
- [ ] 15.5. Listener configuration
- [ ] 15.6. SSL/TLS requirements (if any)

### [ ] 16. Create ALB CDK Stack
- [ ] 16.1. Create `AlbStack.cs`
- [ ] 16.2. Configure ALB with explicit settings
- [ ] 16.3. Define target groups with health checks
- [ ] 16.4. Set up listeners and rules
- [ ] 16.5. Configure deregistration delay

### [ ] 17. Deploy ALB Stack
```bash
cdk deploy AlbStack
```

### [ ] 18. Validate ALB Deployment
```bash
# Get new ALB DNS
NEW_ALB_DNS=$(aws cloudformation describe-stacks --stack-name AlbStack --query 'Stacks[0].Outputs[?OutputKey==`AlbDns`].OutputValue' --output text)

# Test ALB health
curl -I http://$NEW_ALB_DNS/health
```

---

## Phase 4: RDS Database

### [ ] 19. Export RDS Configuration
```bash
# Get RDS instance details
DB_INSTANCE_ID="<your-db-instance-id>"  # Set your DB instance ID

# RDS Instance Configuration
aws rds describe-db-instances --db-instance-identifier $DB_INSTANCE_ID > rds-instance.json

# Subnet Groups
aws rds describe-db-subnet-groups --db-subnet-group-name $(aws rds describe-db-instances --db-instance-identifier $DB_INSTANCE_ID --query 'DBInstances[0].DBSubnetGroup.DBSubnetGroupName' --output text) > db-subnet-group.json

# Parameter Groups
aws rds describe-db-parameter-groups --db-parameter-group-name $(aws rds describe-db-instances --db-instance-identifier $DB_INSTANCE_ID --query 'DBInstances[0].DBParameterGroups[0].DBParameterGroupName' --output text) > db-parameter-group.json

# Get current parameters
aws rds describe-db-parameters --db-parameter-group-name $(aws rds describe-db-instances --db-instance-identifier $DB_INSTANCE_ID --query 'DBInstances[0].DBParameterGroups[0].DBParameterGroupName' --output text) > db-parameters.json
```

### [ ] 20. Document RDS Requirements
- [ ] 20.1. Engine type and version
- [ ] 20.2. Instance class
- [ ] 20.3. Storage type and size
- [ ] 20.4. Multi-AZ configuration
- [ ] 20.5. Backup retention period
- [ ] 20.6. Maintenance window
- [ ] 20.7. Parameter group settings
- [ ] 20.8. Security group

### [ ] 21. Create RDS CDK Stack
- [ ] 21.1. Create `RdsStack.cs`
- [ ] 21.2. Define subnet group explicitly
- [ ] 21.3. Create parameter group with all custom parameters
- [ ] 21.4. Configure instance with all explicit settings
- [ ] 21.5. Set backup and maintenance windows

### [ ] 22. Deploy RDS Stack
```bash
cdk deploy RdsStack
```

### [ ] 23. Validate RDS Deployment
```bash
# Get new RDS endpoint
NEW_DB_ENDPOINT=$(aws cloudformation describe-stacks --stack-name RdsStack --query 'Stacks[0].Outputs[?OutputKey==`DbEndpoint`].OutputValue' --output text)

# Verify configuration
aws rds describe-db-instances --db-instance-identifier <new-db-instance-id> --query 'DBInstances[0].[DBInstanceClass,AllocatedStorage,MultiAZ,BackupRetentionPeriod]'
```

---

## Phase 5: ECS Cluster and Services

### [ ] 24. Export ECS Configuration
```bash
# Get ECS details
CLUSTER_NAME="<your-cluster-name>"  # Set your cluster name
SERVICE_NAME="<your-service-name>"  # Set your service name

# Cluster Configuration
aws ecs describe-clusters --clusters $CLUSTER_NAME > ecs-cluster.json

# Service Configuration
aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME > ecs-service.json

# Task Definition
TASK_DEF=$(aws ecs describe-services --cluster $CLUSTER_NAME --services $SERVICE_NAME --query 'services[0].taskDefinition' --output text)
aws ecs describe-task-definition --task-definition $TASK_DEF > task-definition.json

# Container Insights settings
aws ecs describe-clusters --clusters $CLUSTER_NAME --query 'clusters[0].settings'

# Auto-scaling configuration
aws application-autoscaling describe-scalable-targets --service-namespace ecs --resource-ids service/$CLUSTER_NAME/$SERVICE_NAME > ecs-autoscaling.json
```

### [ ] 25. Document ECS Requirements
- [ ] 25.1. Cluster configuration
- [ ] 25.2. Task CPU and memory
- [ ] 25.3. Container definitions
- [ ] 25.4. Environment variables
- [ ] 25.5. Service desired count
- [ ] 25.6. Auto-scaling policies
- [ ] 25.7. Capacity provider strategy

### [ ] 26. Create ECS CDK Stack
- [ ] 26.1. Create `EcsStack.cs`
- [ ] 26.2. Define cluster with container insights
- [ ] 26.3. Create task definition with explicit resources
- [ ] 26.4. Configure containers with health checks
- [ ] 26.5. Set up service with capacity providers
- [ ] 26.6. Configure auto-scaling

### [ ] 27. Deploy ECS Stack
```bash
cdk deploy EcsStack
```

### [ ] 28. Validate ECS Deployment
```bash
# Check service status
aws ecs describe-services --cluster <new-cluster-name> --services <new-service-name> --query 'services[0].[serviceName,status,desiredCount,runningCount]'

# Verify tasks are running
aws ecs list-tasks --cluster <new-cluster-name> --service-name <new-service-name>

# Check ALB target health
aws elbv2 describe-target-health --target-group-arn <target-group-arn>
```

---

## Phase 6: Integration and Final Validation

### [ ] 29. Create Main CDK Application
- [ ] 29.1. Create `Program.cs` to orchestrate all stacks
- [ ] 29.2. Define stack dependencies
- [ ] 29.3. Configure environment settings
- [ ] 29.4. Set up cross-stack references

### [ ] 30. Deploy Complete Stack
```bash
# Deploy all stacks with dependencies
cdk deploy --all

# Or deploy individually in order
cdk deploy VpcStack
cdk deploy SecurityGroupsStack
cdk deploy AlbStack
cdk deploy RdsStack
cdk deploy EcsStack
```

### [ ] 31. End-to-End Validation
```bash
# Test application endpoint
curl -v http://$NEW_ALB_DNS

# Check ECS to RDS connectivity
aws ecs execute-command --cluster <cluster-name> --task <task-id> --container <container-name> --interactive --command "/bin/sh"
# Inside container: test database connection

# Verify logs
aws logs tail /aws/ecs/<cluster-name> --follow
```

### [ ] 32. Performance Testing
- [ ] 32.1. Load test the new infrastructure
- [ ] 32.2. Compare metrics with original setup
- [ ] 32.3. Verify auto-scaling behavior
- [ ] 32.4. Check failover scenarios

---

## Post-Migration Checklist

### [ ] 33. Configuration Validation
- [ ] 33.1. All CIDR blocks match original
- [ ] 33.2. Security group rules are identical
- [ ] 33.3. RDS parameters match original
- [ ] 33.4. ECS task definitions have same resources
- [ ] 33.5. Auto-scaling policies are configured
- [ ] 33.6. Backup retention matches
- [ ] 33.7. Monitoring and alarms are set up

### [ ] 34. Testing Checklist
- [ ] 34.1. Application responds on ALB endpoint
- [ ] 34.2. Database connectivity works
- [ ] 34.3. Auto-scaling triggers correctly
- [ ] 34.4. Failover testing completed
- [ ] 34.5. Performance benchmarks met
- [ ] 34.6. Security scan passed

### [ ] 35. Documentation
- [ ] 35.1. Architecture diagram updated
- [ ] 35.2. Runbook created
- [ ] 35.3. Deployment guide documented
- [ ] 35.4. Rollback procedures defined
- [ ] 35.5. Team trained on CDK

---

## Rollback Procedure

### [ ] 36. Prepare for Rollback (if needed)
- [ ] 36.1. Take RDS snapshot
- [ ] 36.2. Export ECS task definitions
- [ ] 36.3. Document any manual changes
- [ ] 36.4. Ensure traffic is redirected

### [ ] 37. Execute Rollback (if needed)
If issues arise, rollback in reverse order:

```bash
# Destroy stacks in reverse order
cdk destroy EcsStack
cdk destroy RdsStack
cdk destroy AlbStack
cdk destroy SecurityGroupsStack
cdk destroy VpcStack
```

---

## Best Practices Applied

1. **No CDK Defaults**: Every value explicitly defined
2. **Atomic Deployments**: Each stack independently deployable
3. **Clear Dependencies**: Proper stack ordering enforced
4. **Validation Gates**: Testing after each phase
5. **Rollback Ready**: Each component can be rolled back
6. **Version Control**: All CDK code in Git
7. **Documentation**: Every decision documented

---

## Troubleshooting Guide

### Common Issues

**VPC Creation Fails**
- Check CIDR conflicts
- Verify AZ availability
- Ensure sufficient EIP quota for NAT

**Security Group Errors**
- Verify group dependencies
- Check for circular references
- Ensure ports are correct

**ALB Target Health Issues**
- Verify security group rules
- Check health check endpoint
- Ensure ECS tasks have correct ports

**RDS Connection Problems**
- Verify subnet group configuration
- Check security group rules
- Ensure parameter group compatibility

**ECS Task Failures**
- Check task role permissions
- Verify container health checks
- Ensure environment variables set
- Check CloudWatch logs

---