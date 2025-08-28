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
/// Base stack for TrialMatch dedicated infrastructure with isolated VPC and resources
/// </summary>
public class TrialMatchBaseStack : Stack
{
    private readonly DeploymentContext _context;
    
    public IVpc Vpc { get; private set; } = null!;
    public Dictionary<string, ISecurityGroup> SharedSecurityGroups { get; private set; } = new();
    public ILogGroup SharedLogGroup { get; private set; } = null!;
    public DatabaseCluster? SharedDatabaseCluster { get; private set; }
    public InterfaceVpcEndpoint QuickSightApiEndpoint { get; private set; } = null!;
    public InterfaceVpcEndpoint QuickSightEmbeddingEndpoint { get; private set; } = null!;
    public SecurityGroup QuickSightSecurityGroup { get; private set; } = null!;

    public TrialMatchBaseStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        
        Console.WriteLine($"🏗️  Creating TrialMatch base stack for {context.Environment.Name}");
        Console.WriteLine($"   Creating dedicated infrastructure using tm naming conventions");
        
        // Create dedicated VPC with standardized naming
        CreateDedicatedVpc();
        
        // Create dedicated security groups with tm prefix
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
        
        Console.WriteLine($"✅ TrialMatch base stack created successfully");
    }

    private void CreateDedicatedVpc()
    {
        Console.WriteLine("🌐 Creating dedicated TrialMatch VPC with tm naming...");
        
        var vpcName = _context.Namer.Vpc(ResourcePurpose.Main); // Fixed: Use proper TrialMatch VPC naming
        
        // Create VPC with new CIDR for TrialMatch
        Vpc = new Vpc(this, "TrialMatchVpc", new VpcProps
        {
            VpcName = vpcName,
            IpAddresses = IpAddresses.Cidr("10.1.0.0/16"), // New CIDR for TrialMatch
            MaxAzs = 3, // us-east-2a, us-east-2b, us-east-2c
            NatGateways = 1, // Only 1 NAT gateway in existing setup
            SubnetConfiguration = new[]
            {
                // Public subnets matching existing configuration
                new SubnetConfiguration
                {
                    Name = "public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 20, // /20 subnets to match existing pattern
                    Reserved = false
                },
                // Private subnets matching existing configuration  
                new SubnetConfiguration
                {
                    Name = "private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    CidrMask = 20, // /20 subnets to match existing pattern
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
        Console.WriteLine($"   CIDR: 10.1.0.0/16 (new CIDR for TrialMatch)");
        Console.WriteLine($"   NAT Gateways: 1 (matches existing)");
        Console.WriteLine($"   Public subnets: {Vpc.PublicSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Private subnets: {Vpc.PrivateSubnets.Length} with /20 CIDR");
        Console.WriteLine($"   Isolated subnets: {Vpc.IsolatedSubnets.Length} with /25 CIDR");
    }

    private void CreateDedicatedSecurityGroups()
    {
        Console.WriteLine("🔒 Creating dedicated TrialMatch security groups with tm prefix...");
        
        // ALB Security Group - dedicated for TrialMatch
        var albSg = new SecurityGroup(this, "TrialMatchAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForAlb(ResourcePurpose.Web), // Fixed: Use proper TrialMatch ALB security group naming
            Description = "Dedicated ALB security group for TrialMatch",
            AllowAllOutbound = true
        });
        
        // Allow HTTP from anywhere (matches existing)
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "Allow HTTP from anywhere");
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "Allow HTTPS from anywhere");
        
        SharedSecurityGroups["alb"] = albSg;
        
        // ECS Security Group - dedicated for TrialMatch
        var ecsSg = new SecurityGroup(this, "TrialMatchContainerFromAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForEcs(ResourcePurpose.Web), // Fixed: Use proper TrialMatch ECS security group naming
            Description = "Security group for TrialMatch ECS containers allowing traffic from ALB and internal communication",
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
        
        // RDS Security Group - dedicated for TrialMatch
        var rdsSg = new SecurityGroup(this, "TrialMatchRdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForRds(ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
            Description = "Security group attached to TrialMatch database to allow EC2 instances with specific security groups attached to connect to the database",
            AllowAllOutbound = false
        });
        
        // Allow PostgreSQL from ECS
        rdsSg.AddIngressRule(ecsSg, Port.Tcp(5432), "Allow PostgreSQL from TrialMatch ECS");
        
        // Allow PostgreSQL traffic to RDS database
        ecsSg.AddEgressRule(rdsSg, Port.Tcp(5432), "Allow PostgreSQL traffic to TrialMatch database");
        
        // Allow PostgreSQL from QuickSight (will be added after QuickSight security group is created)
        // This will be handled in CreateQuickSightSecurityGroup method
        
        SharedSecurityGroups["rds"] = rdsSg;
        
        // ECS to RDS Security Group - dedicated for TrialMatch
        var ecsToRdsSg = new SecurityGroup(this, "TrialMatchEcsToRdsSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.Custom("ecs-to-rds", ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
            Description = "Created by RDS management console for TrialMatch", // Updated description
            AllowAllOutbound = true
        });
        
        SharedSecurityGroups["ecs-to-rds"] = ecsToRdsSg;
        
        // VPC Endpoint Security Group - dedicated for TrialMatch
        var vpcEndpointSg = new SecurityGroup(this, "TrialMatchVpcEndpointSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.Custom("vpc-endpoints", ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
            Description = "Dedicated VPC endpoint security group for TrialMatch",
            AllowAllOutbound = false
        });
        
        // Allow HTTPS from ECS
        vpcEndpointSg.AddIngressRule(ecsSg, Port.Tcp(443), "Allow HTTPS from TrialMatch ECS");
        
        // Allow HTTPS to VPC endpoints
        ecsSg.AddEgressRule(vpcEndpointSg, Port.Tcp(443), "Allow HTTPS to TrialMatch VPC endpoints");
        
        SharedSecurityGroups["vpc-endpoints"] = vpcEndpointSg;
        
        Console.WriteLine($"   Created {SharedSecurityGroups.Count} dedicated security groups for TrialMatch");
    }

    private void CreateDedicatedLogging()
    {
        Console.WriteLine("📝 Creating dedicated TrialMatch logging infrastructure...");
        
        var logGroupName = _context.Namer.LogGroup("shared", ResourcePurpose.Main); // Fixed: Use proper TrialMatch log group naming
        
        SharedLogGroup = new LogGroup(this, "TrialMatchSharedLogGroup", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = RetentionDays.ONE_YEAR,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
        
        Console.WriteLine($"   Log group created: {logGroupName}");
    }

    private void CreateDedicatedDatabase()
    {
        Console.WriteLine("🗄️  Creating dedicated TrialMatch database infrastructure...");
        
        // Create custom database credentials secret following naming convention
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var databaseSecretName = _context.Namer.SecretsManager(ResourcePurpose.Internal); // Fixed: Use correct ResourcePurpose enum value
        
        var databaseSecret = new Secret(this, "TrialMatchDatabaseSecret", new SecretProps
        {
            SecretName = databaseSecretName,
            Description = $"Database credentials for TrialMatch PostgreSQL cluster in {_context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"username\":\"postgres\"}",
                GenerateStringKey = "password",
                PasswordLength = 32,
                ExcludeCharacters = "\"@/\\"
            }
        });
        
        // Create database subnet group for isolated subnets
        var dbSubnetGroup = new CfnDBSubnetGroup(this, "TrialMatchDbSubnetGroup", new CfnDBSubnetGroupProps
        {
            DbSubnetGroupName = _context.Namer.Custom("db-subnet-group", ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
            DbSubnetGroupDescription = $"TrialMatch database subnet group for {_context.Environment.Name}",
            SubnetIds = Vpc.IsolatedSubnets.Select(s => s.SubnetId).ToArray()
        });
        
        // Create database cluster
        SharedDatabaseCluster = new DatabaseCluster(this, "TrialMatchDatabaseCluster", new DatabaseClusterProps
        {
            ClusterIdentifier = _context.Namer.RdsInstance(ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
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
        Console.WriteLine("🔐 Creating dedicated TrialMatch QuickSight security group...");
        
        QuickSightSecurityGroup = new SecurityGroup(this, "TrialMatchQuickSightSecurityGroup", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = _context.Namer.Custom("quicksight", ResourcePurpose.Internal), // Fixed: Use correct ResourcePurpose enum value
            Description = $"Dedicated QuickSight security group for TrialMatch in {_context.Environment.Name}",
            AllowAllOutbound = false
        });
        
        // Allow PostgreSQL from QuickSight to RDS
        if (SharedSecurityGroups.ContainsKey("rds"))
        {
            SharedSecurityGroups["rds"].AddIngressRule(QuickSightSecurityGroup, Port.Tcp(5432), "Allow PostgreSQL from TrialMatch QuickSight");
        }
        
        Console.WriteLine($"   QuickSight security group created: {QuickSightSecurityGroup.SecurityGroupId}");
    }

    private void CreateVpcEndpoints()
    {
        Console.WriteLine("🔗 Creating VPC endpoints for TrialMatch...");
        
        // QuickSight Website Interface Endpoint for secure access
        QuickSightApiEndpoint = new InterfaceVpcEndpoint(this, "TrialMatchQuickSightWebsiteVpcEndpoint", new InterfaceVpcEndpointProps
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
        Console.WriteLine("📤 Exporting TrialMatch resources for application stacks...");
        
        // Export VPC
        new CfnOutput(this, "TrialMatchVpcId", new CfnOutputProps
        {
            Value = Vpc.VpcId,
            ExportName = $"tm-{_context.Environment.Name}-vpc-id",
            Description = $"TrialMatch VPC ID for {_context.Environment.Name}"
        });

        // Export individual subnet IDs for ALB stack consumption
        var publicSubnets = Vpc.PublicSubnets.ToArray();
        var privateSubnets = Vpc.PrivateSubnets.ToArray();
        var isolatedSubnets = Vpc.IsolatedSubnets.ToArray();

        // Export public subnet IDs individually
        for (int i = 0; i < publicSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialMatchPublicSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = publicSubnets[i].SubnetId,
                ExportName = $"tm-{_context.Environment.Name}-public-subnet-{i + 1}-id",
                Description = $"TrialMatch public subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }

        // Export private subnet IDs individually
        for (int i = 0; i < privateSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialMatchPrivateSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = privateSubnets[i].SubnetId,
                ExportName = $"tm-{_context.Environment.Name}-private-subnet-{i + 1}-id",
                Description = $"TrialMatch private subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }

        // Export isolated subnet IDs individually
        for (int i = 0; i < isolatedSubnets.Length; i++)
        {
            new CfnOutput(this, $"TrialMatchIsolatedSubnet{i + 1}Id", new CfnOutputProps
            {
                Value = isolatedSubnets[i].SubnetId,
                ExportName = $"tm-{_context.Environment.Name}-isolated-subnet-{i + 1}-id",
                Description = $"TrialMatch isolated subnet {i + 1} ID for {_context.Environment.Name}"
            });
        }
        
        // Export security groups
        foreach (var kvp in SharedSecurityGroups)
        {
            new CfnOutput(this, $"TrialMatch{kvp.Key}SecurityGroupId", new CfnOutputProps
            {
                Value = kvp.Value.SecurityGroupId,
                ExportName = $"tm-{_context.Environment.Name}-{kvp.Key}-sg-id",
                Description = $"TrialMatch {kvp.Key} security group ID for {_context.Environment.Name}"
            });
        }
        
        // Export database
        if (SharedDatabaseCluster != null)
        {
            new CfnOutput(this, "TrialMatchDatabaseEndpoint", new CfnOutputProps
            {
                Value = SharedDatabaseCluster.ClusterEndpoint.Hostname,
                ExportName = $"tm-{_context.Environment.Name}-db-endpoint",
                Description = $"TrialMatch database endpoint for {_context.Environment.Name}"
            });
            
            new CfnOutput(this, "TrialMatchDatabasePort", new CfnOutputProps
            {
                Value = SharedDatabaseCluster.ClusterEndpoint.Port.ToString(),
                ExportName = $"tm-{_context.Environment.Name}-db-port",
                Description = $"TrialMatch database port for {_context.Environment.Name}"
            });
        }
        
        // Export log group
        new CfnOutput(this, "TrialMatchLogGroupName", new CfnOutputProps
        {
            Value = SharedLogGroup.LogGroupName,
            ExportName = $"tm-{_context.Environment.Name}-log-group-name",
            Description = $"TrialMatch log group name for {_context.Environment.Name}"
        });
        
        // Export VPC endpoints
        new CfnOutput(this, "TrialMatchQuickSightApiEndpointId", new CfnOutputProps
        {
            Value = QuickSightApiEndpoint.VpcEndpointId,
            ExportName = $"tm-{_context.Environment.Name}-quicksight-api-endpoint-id",
            Description = $"TrialMatch QuickSight API endpoint ID for {_context.Environment.Name}"
        });
        
        new CfnOutput(this, "TrialMatchQuickSightEmbeddingEndpointId", new CfnOutputProps
        {
            Value = QuickSightEmbeddingEndpoint.VpcEndpointId,
            ExportName = $"tm-{_context.Environment.Name}-quicksight-embedding-endpoint-id",
            Description = $"TrialMatch QuickSight embedding endpoint ID for {_context.Environment.Name}"
        });
        
        Console.WriteLine($"   Exported {SharedSecurityGroups.Count + 5} resources for TrialMatch");
    }

    private void ApplyCommonTags()
    {
        Console.WriteLine("🏷️  Applying common tags to TrialMatch resources...");
        
        var tags = _context.GetCommonTags();
        tags["StackType"] = "TrialMatchBase";
        tags["Application"] = "TrialMatch";
        tags["Purpose"] = "DedicatedTrialMatchInfrastructure";
        
        foreach (var tag in tags)
        {
            Tags.SetTag(tag.Key, tag.Value);
        }
        
        Console.WriteLine($"   Applied common tags to all TrialMatch resources");
    }
}
