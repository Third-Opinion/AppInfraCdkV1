using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;
using Duration = Amazon.CDK.Duration;

namespace AppInfraCdkV1.Stacks.Base;

/// <summary>
/// Base stack for TrialFinderV2 dedicated infrastructure with isolated VPC and resources
/// </summary>
public class TrialFinderV2BaseStack : Stack
{
    private readonly DeploymentContext _context;
    
    public IVpc Vpc { get; private set; } = null!;
    public Dictionary<string, ISecurityGroup> SharedSecurityGroups { get; private set; } = new();
    public ILogGroup SharedLogGroup { get; private set; } = null!;
    public DatabaseCluster? SharedDatabaseCluster { get; private set; }
    public InterfaceVpcEndpoint QuickSightApiEndpoint { get; private set; } = null!;
    public InterfaceVpcEndpoint QuickSightEmbeddingEndpoint { get; private set; } = null!;
    public SecurityGroup QuickSightSecurityGroup { get; private set; } = null!;

    public TrialFinderV2BaseStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        
        Console.WriteLine($"🏗️  Creating TrialFinderV2 base stack for {context.Environment.Name}");
        Console.WriteLine($"   Creating dedicated infrastructure using tf2 naming conventions");
        
        // Create dedicated VPC with standardized naming
        CreateDedicatedVpc();
        
        // Create dedicated security groups with tf2 prefix
        CreateDedicatedSecurityGroups();
        
        // Create dedicated logging infrastructure
        CreateDedicatedLogging();
        
        // Create dedicated database infrastructure
        CreateDedicatedDatabase();
        
        // Create dedicated QuickSight security group (after RDS security group is created)
        CreateQuickSightSecurityGroup();
        
        // Create VPC endpoints if needed
        CreateVpcEndpoints();
        
        // Export resources for application stacks
        ExportDedicatedResources();
        
        // Apply common tags
        ApplyCommonTags();
        
        Console.WriteLine($"✅ TrialFinderV2 base stack created successfully");
    }

    private void CreateDedicatedVpc()
    {
        Console.WriteLine("🌐 Creating dedicated TrialFinderV2 VPC with tf2 naming...");
        
        var vpcName = _context.Namer.SharedVpc(); // Will be updated to use tf2 prefix
        
        // Create VPC with exact CIDR match for TrialFinderV2
        Vpc = new Vpc(this, "TrialFinderV2Vpc", new VpcProps
        {
            VpcName = vpcName,
            IpAddresses = IpAddresses.Cidr("10.0.0.0/16"), // Maintains current CIDR for TrialFinderV2
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
        Console.WriteLine($"   CIDR: 10.0.0.0/16 (maintains current CIDR for TrialFinderV2)");
        Console.WriteLine($"   NAT Gateways: 1 (matches existing)");
        Console.WriteLine($"   Public subnets: {Vpc.PublicSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Private subnets: {Vpc.PrivateSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Isolated subnets: {Vpc.IsolatedSubnets.Length} with /25 CIDR");
    }

    private void CreateDedicatedSecurityGroups()
    {
        Console.WriteLine("🔒 Creating dedicated TrialFinderV2 security groups with tf2 prefix...");
        
        // ALB Security Group - dedicated for TrialFinderV2
        var albSg = new SecurityGroup(this, "TrialFinderV2AlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SharedSecurityGroup("alb"), // Will be updated to use tf2 prefix
            Description = "Dedicated ALB security group for TrialFinderV2",
            AllowAllOutbound = true
        });
        
        // Allow HTTP from anywhere (matches existing)
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "Allow HTTP from anywhere");
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "Allow HTTPS from anywhere");
        
        SharedSecurityGroups["alb"] = albSg;
        
        // ECS Security Group - dedicated for TrialFinderV2
        var ecsSg = new SecurityGroup(this, "TrialFinderV2ContainerFromAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SharedSecurityGroup("ecs"), // Will be updated to use tf2 prefix
            Description = "Security group for TrialFinderV2 ECS containers allowing traffic from ALB and internal communication",
            AllowAllOutbound = false
        });
        
        // Inbound Rules
        // Allow traffic from ALB on all TCP ports
        ecsSg.AddIngressRule(albSg, Port.AllTcp(), "FromALB");
        
        // Allow container-to-container communication on port 8080 (self-reference)
        ecsSg.AddIngressRule(ecsSg, Port.Tcp(8080), "Loopback");
        
        // Outbound Rules
        // Allow HTTP for package downloads
        ecsSg.AddEgressRule(Peer.AnyIpv4(), Port.Tcp(80), "HTTP for package downloads");
        
        // Allow HTTPS for external API calls
        ecsSg.AddEgressRule(Peer.AnyIpv4(), Port.Tcp(443), "HTTPS for external API calls");
        
        // Allow traffic back to ALB for health checks
        ecsSg.AddEgressRule(albSg, Port.AllTcp(), "Health checks to ALB");
        SharedSecurityGroups["ecs"] = ecsSg;
        
        // RDS Security Group - dedicated for TrialFinderV2
        var rdsSg = new SecurityGroup(this, "TrialFinderV2RdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "tf2-rds-ec2-1", // Updated to use tf2 prefix
            Description = "Security group attached to TrialFinderV2 database to allow EC2 instances with specific security groups attached to connect to the database",
            AllowAllOutbound = false
        });
        
        // Allow PostgreSQL from ECS
        rdsSg.AddIngressRule(ecsSg, Port.Tcp(5432), "Allow PostgreSQL from TrialFinderV2 ECS");
        
        // Allow PostgreSQL traffic to RDS database
        ecsSg.AddEgressRule(rdsSg, Port.Tcp(5432), "Allow PostgreSQL traffic to TrialFinderV2 database");
        
        // Allow PostgreSQL from QuickSight (will be added after QuickSight security group is created)
        // This will be handled in CreateQuickSightSecurityGroup method
        
        SharedSecurityGroups["rds"] = rdsSg;
        
        // ECS to RDS Security Group - dedicated for TrialFinderV2
        var ecsToRdsSg = new SecurityGroup(this, "TrialFinderV2EcsToRdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = "tf2-ecs-to-rds-security-group", // Updated to use tf2 prefix
            Description = "Created by RDS management console for TrialFinderV2", // Updated description
            AllowAllOutbound = true
        });
        
        SharedSecurityGroups["ecs-to-rds"] = ecsToRdsSg;
        
        // VPC Endpoint Security Group - dedicated for TrialFinderV2
        var vpcEndpointSg = new SecurityGroup(this, "TrialFinderV2VpcEndpointSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SharedSecurityGroup("vpc-endpoints"), // Will be updated to use tf2 prefix
            Description = "Dedicated VPC endpoint security group for TrialFinderV2",
            AllowAllOutbound = false
        });
        
        // Allow HTTPS from ECS
        vpcEndpointSg.AddIngressRule(ecsSg, Port.Tcp(443), "Allow HTTPS from TrialFinderV2 ECS");
        
        // Allow HTTPS to VPC endpoints
        ecsSg.AddEgressRule(vpcEndpointSg, Port.Tcp(443), "Allow HTTPS to TrialFinderV2 VPC endpoints");
        
        SharedSecurityGroups["vpc-endpoints"] = vpcEndpointSg;
        
        Console.WriteLine($"   Created {SharedSecurityGroups.Count} dedicated security groups for TrialFinderV2");
    }

    private void CreateDedicatedLogging()
    {
        Console.WriteLine("📝 Creating dedicated TrialFinderV2 logging infrastructure...");
        
        var logGroupName = _context.Namer.SharedLogGroup(); // Will be updated to use tf2 prefix
        
        SharedLogGroup = new LogGroup(this, "TrialFinderV2SharedLogGroup", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = RetentionDays.ONE_YEAR,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
        
        Console.WriteLine($"   Log group created: {logGroupName}");
    }

    private void CreateDedicatedDatabase()
    {
        Console.WriteLine("🗄️  Creating dedicated TrialFinderV2 database infrastructure...");
        
        // Create custom database credentials secret following naming convention
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var databaseSecretName = $"/{environmentPrefix}/tf2/database-credentials";
        
        var databaseSecret = new Secret(this, "TrialFinderV2DatabaseSecret", new SecretProps
        {
            SecretName = databaseSecretName,
            Description = $"Database credentials for TrialFinderV2 PostgreSQL cluster in {_context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"username\":\"postgres\"}",
                GenerateStringKey = "password",
                PasswordLength = 32,
                ExcludeCharacters = "\"@/\\"
            }
        });
        
        // Create database subnet group for isolated subnets
        var dbSubnetGroup = new CfnDBSubnetGroup(this, "TrialFinderV2DbSubnetGroup", new CfnDBSubnetGroupProps
        {
            DbSubnetGroupName = $"tf2-{_context.Environment.Name}-db-subnet-group",
            DbSubnetGroupDescription = $"TrialFinderV2 database subnet group for {_context.Environment.Name}",
            SubnetIds = Vpc.IsolatedSubnets.Select(s => s.SubnetId).ToArray()
        });
        
        // Create database cluster
        SharedDatabaseCluster = new DatabaseCluster(this, "TrialFinderV2DatabaseCluster", new DatabaseClusterProps
        {
            ClusterIdentifier = $"tf2-{_context.Environment.Name}-database",
            Engine = DatabaseClusterEngine.AuroraPostgres(new AuroraPostgresClusterEngineProps
            {
                Version = AuroraPostgresEngineVersion.VER_17_4
            }),
            Writer = ClusterInstance.ServerlessV2("writer"),
            ServerlessV2MinCapacity = 0.5,
            ServerlessV2MaxCapacity = 128.0,
            Vpc = Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            Credentials = Credentials.FromSecret(databaseSecret),
            EnableDataApi = true, // Enable Data API for serverless access
            Backup = new BackupProps
            {
                Retention = Duration.Days(7),
                PreferredWindow = "06:00-06:30"
            },
            StorageEncrypted = true,
            DeletionProtection = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
            MonitoringInterval = Duration.Minutes(1),
            EnablePerformanceInsights = true,
            CloudwatchLogsExports = new[] { "postgresql" },
            IamAuthentication = true,
            PreferredMaintenanceWindow = "fri:05:00-fri:05:30",
            SecurityGroups = new[] { SharedSecurityGroups["rds"] }
        });
        
        Console.WriteLine($"   Database cluster created: {SharedDatabaseCluster.ClusterIdentifier}");
        Console.WriteLine($"   Subnet group: {dbSubnetGroup.DbSubnetGroupName}");
    }

    private void CreateQuickSightSecurityGroup()
    {
        Console.WriteLine("🔐 Creating dedicated TrialFinderV2 QuickSight security group...");
        
        QuickSightSecurityGroup = new SecurityGroup(this, "TrialFinderV2QuickSightSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = $"tf2-quicksight-{_context.Environment.Name}",
            Description = $"Dedicated QuickSight security group for TrialFinderV2 in {_context.Environment.Name}",
            AllowAllOutbound = false
        });
        
        // Allow PostgreSQL from QuickSight to RDS
        if (SharedSecurityGroups.ContainsKey("rds"))
        {
            SharedSecurityGroups["rds"].AddIngressRule(QuickSightSecurityGroup, Port.Tcp(5432), "Allow PostgreSQL from TrialFinderV2 QuickSight");
        }
        
        Console.WriteLine($"   QuickSight security group created: {QuickSightSecurityGroup.SecurityGroupId}");
    }

    private void CreateVpcEndpoints()
    {
        Console.WriteLine("🔗 Creating VPC endpoints for TrialFinderV2...");
        
        // QuickSight Website Interface Endpoint for secure access
        QuickSightApiEndpoint = new InterfaceVpcEndpoint(this, "TrialFinderV2QuickSightWebsiteVpcEndpoint", new InterfaceVpcEndpointProps
        {
            Vpc = Vpc,
            Service = InterfaceVpcEndpointAwsService.QUICKSIGHT_WEBSITE,
            SecurityGroups = new[] { QuickSightSecurityGroup },
            Subnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });
        
        // Note: QuickSight API and embedding functionality are accessed through the website endpoint
        // No separate embedding endpoint is needed as it's handled through the main QuickSight service
        QuickSightEmbeddingEndpoint = QuickSightApiEndpoint; // Use the same endpoint for both
        
        Console.WriteLine($"   QuickSight website endpoint created");
    }

    private void ExportDedicatedResources()
    {
        Console.WriteLine("📤 Exporting TrialFinderV2 resources for application stacks...");
        
        // Export VPC
        new CfnOutput(this, "TrialFinderV2VpcId", new CfnOutputProps
        {
            Value = Vpc.VpcId,
            ExportName = $"tf2-{_context.Environment.Name}-vpc-id",
            Description = $"TrialFinderV2 VPC ID for {_context.Environment.Name}"
        });

        // Export individual subnet IDs for ALB stack consumption
        var publicSubnets = Vpc.PublicSubnets.ToArray();
        var privateSubnets = Vpc.PrivateSubnets.ToArray();
        var isolatedSubnets = Vpc.IsolatedSubnets.ToArray();

        // Export public subnet IDs individually
        for (int i = 0; i < publicSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialFinderV2PublicSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = publicSubnets[i].SubnetId,
                ExportName = $"tf2-{_context.Environment.Name}-public-subnet-{i + 1}-id",
                Description = $"TrialFinderV2 public subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }

        // Export private subnet IDs individually
        for (int i = 0; i < privateSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialFinderV2PrivateSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = privateSubnets[i].SubnetId,
                ExportName = $"tf2-{_context.Environment.Name}-private-subnet-{i + 1}-id",
                Description = $"TrialFinderV2 private subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }

        // Export isolated subnet IDs individually
        for (int i = 0; i < isolatedSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialFinderV2IsolatedSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = isolatedSubnets[i].SubnetId,
                ExportName = $"tf2-{_context.Environment.Name}-isolated-subnet-{i + 1}-id",
                Description = $"TrialFinderV2 isolated subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }
        
        // Export security groups
        foreach (var kvp in SharedSecurityGroups)
        {
            new CfnOutput(this, $"TrialFinderV2{kvp.Key}SecurityGroupId", new CfnOutputProps
            {
                Value = kvp.Value.SecurityGroupId,
                ExportName = $"tf2-{_context.Environment.Name}-{kvp.Key}-sg-id",
                Description = $"TrialFinderV2 {kvp.Key} security group ID for {_context.Environment.Name}"
            });
        }
        
        // Export database
        if (SharedDatabaseCluster != null)
        {
            new CfnOutput(this, "TrialFinderV2DatabaseEndpoint", new CfnOutputProps
            {
                Value = SharedDatabaseCluster.ClusterEndpoint.Hostname,
                ExportName = $"tf2-{_context.Environment.Name}-db-endpoint",
                Description = $"TrialFinderV2 database endpoint for {_context.Environment.Name}"
            });
            
            new CfnOutput(this, "TrialFinderV2DatabasePort", new CfnOutputProps
            {
                Value = SharedDatabaseCluster.ClusterEndpoint.Port.ToString(),
                ExportName = $"tf2-{_context.Environment.Name}-db-port",
                Description = $"TrialFinderV2 database port for {_context.Environment.Name}"
            });
        }
        
        // Export log group
        new CfnOutput(this, "TrialFinderV2LogGroupName", new CfnOutputProps
        {
            Value = SharedLogGroup.LogGroupName,
            ExportName = $"tf2-{_context.Environment.Name}-log-group-name",
            Description = $"TrialFinderV2 log group name for {_context.Environment.Name}"
        });
        
        // Export VPC endpoints
        new CfnOutput(this, "TrialFinderV2QuickSightApiEndpointId", new CfnOutputProps
        {
            Value = QuickSightApiEndpoint.VpcEndpointId,
            ExportName = $"tf2-{_context.Environment.Name}-quicksight-api-endpoint-id",
            Description = $"TrialFinderV2 QuickSight API endpoint ID for {_context.Environment.Name}"
        });
        
        new CfnOutput(this, "TrialFinderV2QuickSightEmbeddingEndpointId", new CfnOutputProps
        {
            Value = QuickSightEmbeddingEndpoint.VpcEndpointId,
            ExportName = $"tf2-{_context.Environment.Name}-quicksight-embedding-endpoint-id",
            Description = $"TrialFinderV2 QuickSight embedding endpoint ID for {_context.Environment.Name}"
        });
        
        Console.WriteLine($"   Exported {SharedSecurityGroups.Count + 5} resources for TrialFinderV2");
    }

    private void ApplyCommonTags()
    {
        Console.WriteLine("🏷️  Applying common tags to TrialFinderV2 resources...");
        
        var tags = _context.GetCommonTags();
        tags["StackType"] = "TrialFinderV2Base";
        tags["Application"] = "TrialFinderV2";
        tags["Purpose"] = "DedicatedTrialFinderV2Infrastructure";
        
        foreach (var tag in tags)
        {
            Tags.SetTag(tag.Key, tag.Value);
        }
        
        Console.WriteLine($"   Applied common tags to all TrialFinderV2 resources");
    }
}
