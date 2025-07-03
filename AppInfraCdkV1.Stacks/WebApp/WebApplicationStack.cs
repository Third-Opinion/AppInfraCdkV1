using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using AppInfraCdkV1.Core.Abstractions;
using AppInfraCdkV1.Core.Models;
using Constructs;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace AppInfraCdkV1.Stacks.WebApp;

public class WebApplicationStack : Stack, IApplicationStack
{
    private readonly DeploymentContext _context;

    protected WebApplicationStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        ApplicationName = context.Application.Name;

        // Validate naming convention before creating resources
        context.ValidateNamingContext();

        CreateResources(context);
    }

    public string ApplicationName { get; }

    public void CreateResources(DeploymentContext context)
    {
        //var vpc = CreateOrImportVpc();
       // SecurityGroupBundle securityGroups = CreateSecurityGroups(vpc);
       // var cluster = CreateEcsCluster(vpc);
       // var database = CreateDatabase(vpc, securityGroups.DatabaseSg);
       // S3BucketBundle s3Buckets = CreateS3Buckets();
       // var service = CreateWebService(cluster, database, securityGroups);

        // Apply cross-environment access rules if configured
        // ConfigureCrossEnvironmentAccess(vpc, securityGroups);

        ApplyCommonTags();
    }

    private IVpc CreateOrImportVpc()
    {
        var vpcName = _context.Namer.Vpc();

        // Check if we should use a shared VPC
        if (_context.Environment.IsolationStrategy.UseSharedVpcWithSubnets &&
            !string.IsNullOrEmpty(_context.Environment.IsolationStrategy.SharedVpcId))
        {
            Console.WriteLine(
                $"Using shared VPC: {_context.Environment.IsolationStrategy.SharedVpcId}");
            return Vpc.FromLookup(this, "SharedVpc", new VpcLookupOptions
            {
                VpcId = _context.Environment.IsolationStrategy.SharedVpcId
            });
        }

        // Try to find existing VPC for this environment
        try
        {
            return Vpc.FromLookup(this, "Vpc", new VpcLookupOptions
            {
                VpcName = vpcName
            });
        }
        catch
        {
            // If VPC doesn't exist, create it with environment-specific CIDR
            Console.WriteLine(
                $"Creating new VPC: {vpcName} with CIDR: {_context.Environment.IsolationStrategy.VpcCidr.PrimaryCidr}");

            return new Vpc(this, "Vpc", new VpcProps
            {
                IpAddresses = IpAddresses.Cidr(_context.Environment.IsolationStrategy.VpcCidr?.PrimaryCidr ?? "10.0.0.0/16"),
                MaxAzs = 2,
                NatGateways = _context.Environment.IsProductionClass ? 2 : 1,
                SubnetConfiguration = new[]
                {
                    new SubnetConfiguration
                    {
                        Name = "Public",
                        SubnetType = SubnetType.PUBLIC,
                        CidrMask = 24
                    },
                    new SubnetConfiguration
                    {
                        Name = "Private",
                        SubnetType = SubnetType.PRIVATE_ISOLATED,
                        CidrMask = 24
                    },
                    new SubnetConfiguration
                    {
                        Name = "Isolated",
                        SubnetType = SubnetType.PRIVATE_ISOLATED,
                        CidrMask = 24
                    }
                }
            });
        }
    }

    private SecurityGroupBundle CreateSecurityGroups(IVpc vpc)
    {
        var albSg = new SecurityGroup(this, "AlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForAlb("web"),
            Description
                = $"Security group for {_context.Environment.Name} web application load balancer",
            AllowAllOutbound = true
        });

        // Configure ALB access based on environment
        ConfigureAlbSecurityGroup(albSg);

        var ecsSg = new SecurityGroup(this, "EcsSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForEcs("web"),
            Description
                = $"Security group for {_context.Environment.Name} web application containers",
            AllowAllOutbound = true
        });

        // Allow traffic from ALB to ECS
        ecsSg.AddIngressRule(albSg, Port.AllTcp(), "Allow traffic from ALB");

        var dbSg = new SecurityGroup(this, "DatabaseSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = _context.Namer.SecurityGroupForRds("main"),
            Description
                = $"Security group for {_context.Environment.Name} main application database",
            AllowAllOutbound = false
        });

        // Allow database access from ECS
        dbSg.AddIngressRule(ecsSg, Port.Tcp(5432), "Allow PostgreSQL access from ECS");

        return new SecurityGroupBundle(albSg, ecsSg, dbSg);
    }

    private void ConfigureAlbSecurityGroup(ISecurityGroup albSg)
    {
        // Allow HTTP and HTTPS traffic
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.AllTcp(), "Allow HTTP traffic");
        albSg.AddIngressRule(Peer.AnyIpv4(), Port.AllTcp(), "Allow HTTPS traffic");

        // Add environment-specific access rules
        var allowedCidrs = _context.Application.Security.AllowedCidrBlocks;
        foreach (var cidr in allowedCidrs)
            albSg.AddIngressRule(Peer.Ipv4(cidr), Port.AllTcp(), $"Allow access from {cidr}");
    }

    private void ConfigureCrossEnvironmentAccess(IVpc vpc, SecurityGroupBundle securityGroups)
    {
        var crossEnvAccess = _context.Environment.IsolationStrategy.CrossEnvironmentAccess;

        // Configure cross-environment access if allowed
        if (crossEnvAccess.CanAccessEnvironments.Any())
            foreach (var targetEnv in crossEnvAccess.CanAccessEnvironments)
                Console.WriteLine($"Configuring access to environment: {targetEnv}");
        // In a real implementation, you would configure VPC peering,
        // Transit Gateway, or other connectivity mechanisms here
    }

    private ICluster CreateEcsCluster(IVpc vpc)
    {
        var cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            Vpc = vpc,
            ClusterName = _context.Namer.EcsCluster(),
            EnableFargateCapacityProviders = true
        });

        // Add environment-specific cluster configuration
        if (_context.Environment.IsProductionClass)
            cluster.AddCapacity("DefaultAutoScalingGroup", new AddCapacityOptions
            {
                InstanceType = new InstanceType("t3.medium"),
                MinCapacity = 1,
                MaxCapacity = 5
            });

        return cluster;
    }

    private IDatabaseInstance CreateDatabase(IVpc vpc, ISecurityGroup securityGroup)
    {
        var subnetGroup = new SubnetGroup(this, "DbSubnetGroup", new SubnetGroupProps
        {
            Description
                = $"Subnet group for {ApplicationName} {_context.Environment.Name} database",
            Vpc = vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SubnetGroupName = _context.Namer.Custom("dbsubnet", "main")
        });

        var backupRetention = _context.Application.MultiEnvironment
            .GetEffectiveConfigForEnvironment(_context.Environment.Name)
            .BackupRetentionDays;

        return new DatabaseInstance(this, "Database", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.VER_17_3
            }),
            InstanceIdentifier = _context.Namer.RdsInstance("main"),
            // InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3,
            //     GetInstanceSizeFromClass(_context.Application.Sizing.DatabaseInstanceClass)),
            Vpc = vpc,
            SubnetGroup = subnetGroup,
            SecurityGroups = new[] { securityGroup },
            DatabaseName = _context.Application.Name.ToLower().Replace("v", ""),
            BackupRetention = Duration.Days(backupRetention),
            DeleteAutomatedBackups = !_context.Environment.IsProductionClass,
            DeletionProtection = _context.Environment.IsProductionClass,
            RemovalPolicy = _context.Environment.IsProductionClass
                ? RemovalPolicy.RETAIN
                : RemovalPolicy.DESTROY,
            EnablePerformanceInsights = _context.Environment.IsProductionClass,
            MonitoringInterval
                = _context.Environment.IsProductionClass ? Duration.Seconds(60) : null
        });
    }

    private InstanceSize GetInstanceSizeFromClass(string instanceClass)
    {
        return instanceClass switch
        {
            "db.t3.micro" => InstanceSize.MICRO,
            "db.t3.small" => InstanceSize.SMALL,
            "db.t3.medium" => InstanceSize.MEDIUM,
            "db.t3.large" => InstanceSize.LARGE,
            _ => InstanceSize.MICRO
        };
    }

    // private S3BucketBundle CreateS3Buckets()
    // {
    //     var appBucket = new Bucket(this, "AppBucket", new BucketProps
    //     {
    //         BucketName = _context.Namer.S3Bucket("app"),
    //         Versioned = _context.Environment.IsProductionClass,
    //         RemovalPolicy = _context.Environment.IsProductionClass
    //             ? RemovalPolicy.RETAIN
    //             : RemovalPolicy.DESTROY,
    //         AutoDeleteObjects = !_context.Environment.IsProductionClass,
    //         Encryption = BucketEncryption.S3_MANAGED
    //     });
    //
    //
    //     return new S3BucketBundle(appBucket);
    // }

    private ApplicationLoadBalancedFargateService CreateWebService(ICluster cluster,
        IDatabaseInstance database,
        SecurityGroupBundle securityGroups)
    {
        var sizing = _context.Application.Sizing;

        // Create log group with proper naming
        var logGroup = new LogGroup(this, "ServiceLogGroup", new LogGroupProps
        {
            LogGroupName = _context.Namer.LogGroup("ecs", "web"),
            Retention = _context.Environment.IsProductionClass
                ? RetentionDays.ONE_MONTH
                : RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        return new ApplicationLoadBalancedFargateService(this, "Service",
            new ApplicationLoadBalancedFargateServiceProps
            {
                Cluster = cluster,
                ServiceName = _context.Namer.EcsService("web"),
                TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
                {
                    Image = ContainerImage.FromRegistry($"{ApplicationName.ToLower()}:latest"),
                    Environment = CreateEnvironmentVariables(database),
                    LogDriver = LogDriver.AwsLogs(new AwsLogDriverProps
                    {
                        LogGroup = logGroup,
                        StreamPrefix = "ecs"
                    }),
                    TaskRole = CreateTaskRole(),
                    ExecutionRole = CreateExecutionRole()
                },
              //  DesiredCount = sizing.MinCapacity,
                PublicLoadBalancer = true,
                Protocol = ApplicationProtocol.HTTPS,
                RedirectHTTP = true,
                TaskDefinition = CreateTaskDefinition()
            });
    }

    private Dictionary<string, string> CreateEnvironmentVariables(IDatabaseInstance database)
    {
        var envVars = new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = _context.Environment.Name,
            ["ACCOUNT_TYPE"] = _context.Environment.AccountType.ToString(),
            ["DATABASE_HOST"] = database.InstanceEndpoint.Hostname,
            ["DATABASE_NAME"] = _context.Application.Name.ToLower().Replace("v", ""),
            ["APP_VERSION"] = _context.Application.Version
        };

        // Add application-specific settings as environment variables
        foreach (var setting in _context.Application.Settings)
            envVars[$"APP_{setting.Key.ToUpper()}"] = setting.Value.ToString() ?? "";


        return envVars;
    }

    private FargateTaskDefinition CreateTaskDefinition()
    {
        return new FargateTaskDefinition(this, "TaskDefinition", new FargateTaskDefinitionProps
        {
            Family = _context.Namer.EcsTaskDefinition("web"),
            // MemoryLimitMiB = _context.Application.Sizing.GetMemoryLimit(),
            // Cpu = _context.Application.Sizing.GetCpuLimit()
        });
    }

    private Amazon.CDK.AWS.IAM.IRole CreateTaskRole()
    {
        return new Amazon.CDK.AWS.IAM.Role(this, "TaskRole", new Amazon.CDK.AWS.IAM.RoleProps
        {
            RoleName = _context.Namer.IamRole("ecs-task"),
            AssumedBy = new Amazon.CDK.AWS.IAM.ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description
                = $"ECS task role for {ApplicationName} {_context.Environment.Name} web service"
        });
    }

    private Amazon.CDK.AWS.IAM.IRole CreateExecutionRole()
    {
        return new Amazon.CDK.AWS.IAM.Role(this, "ExecutionRole", new Amazon.CDK.AWS.IAM.RoleProps
        {
            RoleName = _context.Namer.IamRole("ecs-execution"),
            AssumedBy = new Amazon.CDK.AWS.IAM.ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                Amazon.CDK.AWS.IAM.ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AmazonECSTaskExecutionRolePolicy")
            },
            Description
                = $"ECS execution role for {ApplicationName} {_context.Environment.Name} web service"
        });
    }

    private void ApplyCommonTags()
    {
        var tags = _context.GetCommonTags();
        tags["AccountType"] = _context.Environment.AccountType.ToString();
        tags["IsolationStrategy"] = _context.Environment.IsolationStrategy.UseVpcPerEnvironment
            ? "VpcPerEnvironment"
            : "SharedVpc";

        foreach (var tag in tags) Tags.SetTag(tag.Key, tag.Value);
    }
}

// Helper classes for organizing related resources

// Extension methods for resource sizing