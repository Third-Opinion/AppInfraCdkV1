using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Stacks.Base;

/// <summary>
/// Base stack for shared environment resources that recreates existing vpc-085a37ab90d4186ac infrastructure
/// </summary>
public class EnvironmentBaseStack : Stack
{
    private readonly DeploymentContext _context;
    
    public IVpc Vpc { get; private set; } = null!;
    public Dictionary<string, ISecurityGroup> SharedSecurityGroups { get; private set; } = new();
    public ILogGroup SharedLogGroup { get; private set; } = null!;

    public EnvironmentBaseStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        
        Console.WriteLine($"🏗️  Creating environment base stack for {context.Environment.Name}");
        Console.WriteLine($"   Recreating infrastructure to match existing VPC vpc-085a37ab90d4186ac");
        
        // Create shared VPC matching existing hello-world-vpc
        CreateSharedVpc();
        
        // Create shared security groups matching existing ones
        CreateSharedSecurityGroups();
        
        // Create shared logging infrastructure
        CreateSharedLogging();
        
        // Create VPC endpoints if needed
        CreateVpcEndpoints();
        
        // Export resources for application stacks
        ExportSharedResources();
        
        // Apply common tags
        ApplyCommonTags();
        
        Console.WriteLine($"✅ Environment base stack created successfully");
    }

    private void CreateSharedVpc()
    {
        Console.WriteLine("🌐 Creating shared VPC to match existing vpc-085a37ab90d4186ac...");
        
        var vpcName = "hello-world-vpc"; // Match existing VPC name
        
        // Create VPC with exact CIDR match
        Vpc = new Vpc(this, "SharedVpc", new VpcProps
        {
            VpcName = vpcName,
            IpAddresses = IpAddresses.Cidr("10.0.0.0/16"), // Exact match to existing
            MaxAzs = 3, // us-east-2a, us-east-2b, us-east-2c
            NatGateways = 1, // Only 1 NAT gateway in existing setup
            SubnetConfiguration = new[]
            {
                // Public subnets matching existing configuration
                new SubnetConfiguration
                {
                    Name = "public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 20, // /20 subnets to match existing (10.0.0.0/20, 10.0.16.0/20)
                    Reserved = false
                },
                // Private subnets matching existing configuration  
                new SubnetConfiguration
                {
                    Name = "private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    CidrMask = 20, // /20 subnets to match existing (10.0.128.0/20, 10.0.144.0/20)
                    Reserved = false
                },
                // Isolated subnets for RDS matching existing RDS subnets
                new SubnetConfiguration
                {
                    Name = "isolated",
                    SubnetType = SubnetType.PRIVATE_ISOLATED,
                    CidrMask = 25, // /25 subnets to match existing RDS subnets
                    Reserved = false
                }
            },
            EnableDnsHostnames = true,
            EnableDnsSupport = true
        });
        
        Console.WriteLine($"   VPC created: {vpcName}");
        Console.WriteLine($"   CIDR: 10.0.0.0/16 (matches existing)");
        Console.WriteLine($"   NAT Gateways: 1 (matches existing)");
        Console.WriteLine($"   Public subnets: {Vpc.PublicSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Private subnets: {Vpc.PrivateSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Isolated subnets: {Vpc.IsolatedSubnets.Length} with /25 CIDR");
    }

    private void CreateSharedSecurityGroups()
    {
        Console.WriteLine("🔒 Creating shared security groups to match existing ones...");
        
        // ALB Security Group - matches sg-04e0ab70e85194e27 (AlbSecurityGroup)
        var albSg = new SecurityGroup(this, "AlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "AlbSecurityGroup", // Match existing name
            Description = "Inbound traffic Port 80 from Anywhere", // Match existing description
            AllowAllOutbound = true
        });
        
        // Allow HTTP from anywhere (matches existing)
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "Allow HTTP from anywhere");
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "Allow HTTPS from anywhere");
        
        SharedSecurityGroups["alb"] = albSg;
        
        // ECS Security Group - matches sg-05787d59ddec14f04 (ContainerFromAlbSecurityGroup)
        var ecsSg = new SecurityGroup(this, "ContainerFromAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "ContainerFromAlbSecurityGroup", // Match existing name
            Description = "Inbound traffic from the AlbSecurityGroup", // Match existing description
            AllowAllOutbound = true
        });
        
        // Allow traffic from ALB (matches existing)
        ecsSg.AddIngressRule(albSg, Port.AllTcp(), "Allow traffic from ALB");
        
        SharedSecurityGroups["ecs"] = ecsSg;
        
        // RDS Security Group - matches sg-070060ee6c22a9fd7 (rds-ec2-1)
        var rdsSg = new SecurityGroup(this, "RdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "rds-ec2-1", // Match existing name
            Description = "Security group attached to database to allow EC2 instances with specific security groups attached to connect to the database",
            AllowAllOutbound = false
        });
        
        // Allow PostgreSQL from ECS
        rdsSg.AddIngressRule(ecsSg, Port.Tcp(5432), "Allow PostgreSQL from ECS");
        
        SharedSecurityGroups["rds"] = rdsSg;
        
        // ECS to RDS Security Group - matches sg-0e1f1808e2e77aea1 (ecs-to-rds-security-group)
        var ecsToRdsSg = new SecurityGroup(this, "EcsToRdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "ecs-to-rds-security-group", // Match existing name
            Description = "Created by RDS management console", // Match existing description
            AllowAllOutbound = true
        });
        
        SharedSecurityGroups["ecs-to-rds"] = ecsToRdsSg;
        
        // VPC Endpoint Security Group - matches sg-056af56a32e37dce2 (vpc-endpoint-from-ecs-security-group)
        var vpcEndpointSg = new SecurityGroup(this, "VpcEndpointSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "vpc-endpoint-from-ecs-security-group", // Match existing name
            Description = "All 443 from ECS SG to VPC endpoints", // Match existing description
            AllowAllOutbound = false
        });
        
        // Allow HTTPS from ECS to VPC endpoints
        vpcEndpointSg.AddIngressRule(ecsSg, Port.Tcp(443), "Allow HTTPS from ECS to VPC endpoints");
        
        SharedSecurityGroups["vpc-endpoints"] = vpcEndpointSg;
        
        // Test Security Group - matches sg-0f94ecfad0e02e821 (dev-test-trail-finder-v2-security-group)
        var testSg = new SecurityGroup(this, "TestSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "dev-test-trail-finder-v2-security-group", // Match existing name
            Description = "All access to linux host for testing", // Match existing description
            AllowAllOutbound = true
        });
        
        // This would need specific rules based on testing requirements
        SharedSecurityGroups["test"] = testSg;
        
        Console.WriteLine($"   Created {SharedSecurityGroups.Count} shared security groups matching existing ones");
    }

    private void CreateSharedLogging()
    {
        Console.WriteLine("📊 Creating shared logging infrastructure...");
        
        SharedLogGroup = new LogGroup(this, "SharedLogGroup", new LogGroupProps
        {
            LogGroupName = _context.Namer.LogGroup("shared", ResourcePurpose.Main),
            Retention = _context.Environment.IsProductionClass 
                ? RetentionDays.ONE_MONTH 
                : RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
        
        Console.WriteLine($"   Shared log group created: {SharedLogGroup.LogGroupName}");
    }

    private void CreateVpcEndpoints()
    {
        Console.WriteLine("🔗 Creating VPC endpoints for AWS services...");
        
        // S3 Gateway Endpoint for S3 access without NAT gateway
        var s3Endpoint = new GatewayVpcEndpoint(this, "S3VpcEndpoint", new GatewayVpcEndpointProps
        {
            Vpc = Vpc,
            Service = GatewayVpcEndpointAwsService.S3,
            Subnets = new[] 
            { 
                new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
                new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED }
            }
        });
        
        // DynamoDB Gateway Endpoint
        var dynamoDbEndpoint = new GatewayVpcEndpoint(this, "DynamoDbVpcEndpoint", new GatewayVpcEndpointProps
        {
            Vpc = Vpc,
            Service = GatewayVpcEndpointAwsService.DYNAMODB,
            Subnets = new[] 
            { 
                new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
                new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED }
            }
        });
        
        // ECR Interface Endpoints for container image pulling
        var ecrApiEndpoint = new InterfaceVpcEndpoint(this, "EcrApiVpcEndpoint", new InterfaceVpcEndpointProps
        {
            Vpc = Vpc,
            Service = InterfaceVpcEndpointAwsService.ECR,
            SecurityGroups = new[] { SharedSecurityGroups["vpc-endpoints"] },
            Subnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });
        
        var ecrDkrEndpoint = new InterfaceVpcEndpoint(this, "EcrDkrVpcEndpoint", new InterfaceVpcEndpointProps
        {
            Vpc = Vpc,
            Service = InterfaceVpcEndpointAwsService.ECR_DOCKER,
            SecurityGroups = new[] { SharedSecurityGroups["vpc-endpoints"] },
            Subnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });
        
        // CloudWatch Logs Interface Endpoint
        var logsEndpoint = new InterfaceVpcEndpoint(this, "LogsVpcEndpoint", new InterfaceVpcEndpointProps
        {
            Vpc = Vpc,
            Service = InterfaceVpcEndpointAwsService.CLOUDWATCH_LOGS,
            SecurityGroups = new[] { SharedSecurityGroups["vpc-endpoints"] },
            Subnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });
        
        Console.WriteLine("   Created VPC endpoints for S3, DynamoDB, ECR, and CloudWatch Logs");
    }

    private void ExportSharedResources()
    {
        Console.WriteLine("📤 Exporting shared resources...");
        
        // Export VPC ID
        new CfnOutput(this, "VpcId", new CfnOutputProps
        {
            Value = Vpc.VpcId,
            ExportName = $"{_context.Environment.Name}-vpc-id",
            Description = "VPC ID for shared environment VPC"
        });
        
        // Export VPC CIDR
        new CfnOutput(this, "VpcCidr", new CfnOutputProps
        {
            Value = Vpc.VpcCidrBlock,
            ExportName = $"{_context.Environment.Name}-vpc-cidr",
            Description = "CIDR block for shared environment VPC"
        });
        
        // Export subnet IDs
        var publicSubnetIds = string.Join(",", Vpc.PublicSubnets.Select(s => s.SubnetId));
        new CfnOutput(this, "PublicSubnetIds", new CfnOutputProps
        {
            Value = publicSubnetIds,
            ExportName = $"{_context.Environment.Name}-public-subnet-ids",
            Description = "Public subnet IDs"
        });
        
        var privateSubnetIds = string.Join(",", Vpc.PrivateSubnets.Select(s => s.SubnetId));
        new CfnOutput(this, "PrivateSubnetIds", new CfnOutputProps
        {
            Value = privateSubnetIds,
            ExportName = $"{_context.Environment.Name}-private-subnet-ids",
            Description = "Private subnet IDs"
        });
        
        var isolatedSubnetIds = string.Join(",", Vpc.IsolatedSubnets.Select(s => s.SubnetId));
        new CfnOutput(this, "IsolatedSubnetIds", new CfnOutputProps
        {
            Value = isolatedSubnetIds,
            ExportName = $"{_context.Environment.Name}-isolated-subnet-ids",
            Description = "Isolated subnet IDs for databases"
        });
        
        // Export security group IDs
        foreach (var (name, sg) in SharedSecurityGroups)
        {
            new CfnOutput(this, $"{name}SecurityGroupId", new CfnOutputProps
            {
                Value = sg.SecurityGroupId,
                ExportName = $"{_context.Environment.Name}-sg-{name}-id",
                Description = $"Security group ID for {name}"
            });
        }
        
        // Export shared log group name
        new CfnOutput(this, "SharedLogGroupName", new CfnOutputProps
        {
            Value = SharedLogGroup.LogGroupName,
            ExportName = $"{_context.Environment.Name}-shared-log-group-name",
            Description = "Shared log group name"
        });
        
        Console.WriteLine($"   Exported resources with prefix: {_context.Environment.Name}-*");
    }

    private void ApplyCommonTags()
    {
        var tags = _context.GetCommonTags();
        tags["StackType"] = "EnvironmentBase";
        tags["Shared"] = "true";
        tags["VpcId"] = "vpc-085a37ab90d4186ac-recreation";
        tags["Purpose"] = "SharedInfrastructure";
        
        foreach (var tag in tags)
        {
            Tags.SetTag(tag.Key, tag.Value);
        }
    }
}