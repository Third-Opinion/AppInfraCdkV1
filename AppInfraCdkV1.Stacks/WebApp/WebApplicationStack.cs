using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using AppInfraCdkV1.Core.Abstractions;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Stacks.Services;
using Constructs;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace AppInfraCdkV1.Stacks.WebApp;

public class WebApplicationStack : Stack, IApplicationStack
{
    private readonly DeploymentContext _context;
    protected readonly EnvironmentResourceProvider EnvironmentResources;

    protected WebApplicationStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        ApplicationName = context.Application.Name;

        // Initialize environment resource provider
        EnvironmentResources = new EnvironmentResourceProvider(this, context);

        // Validate naming convention before creating resources
        context.ValidateNamingContext();
        
        // Validate shared environment resources are available
        EnvironmentResources.ValidateSharedResources();

        CreateResources(context);
    }

    public string ApplicationName { get; }

    public void CreateResources(DeploymentContext context)
    {
        Console.WriteLine($"🚀 Creating application resources for {ApplicationName}...");
        
        // Get shared VPC and security groups
        var vpc = EnvironmentResources.GetSharedVpc();
        var sharedSecurityGroups = GetSharedSecurityGroups();
        
        // Create application-specific resources
        var cluster = CreateEcsCluster(vpc);
        // var database = CreateDatabase(vpc, sharedSecurityGroups.DatabaseSg);
        // var service = CreateWebService(cluster, database, sharedSecurityGroups);

        ApplyCommonTags();
        
        Console.WriteLine($"✅ Application resources created for {ApplicationName}");
    }

    /// <summary>
    /// Gets shared security groups from the environment base stack
    /// </summary>
    private SecurityGroupBundle GetSharedSecurityGroups()
    {
        return new SecurityGroupBundle(
            EnvironmentResources.GetAlbSecurityGroup(),
            EnvironmentResources.GetEcsSecurityGroup(),
            EnvironmentResources.GetRdsSecurityGroup()
        );
    }

    // Security groups are now provided by the shared environment base stack
    // Individual applications no longer create their own security groups


    private ICluster CreateEcsCluster(IVpc vpc)
    {
        var cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            Vpc = vpc,
            ClusterName = _context.Namer.EcsCluster(),
            EnableFargateCapacityProviders = true,
            ContainerInsightsV2 = ContainerInsights.ENABLED
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
            VpcSubnets = EnvironmentResources.GetIsolatedSubnetSelection(),
            SubnetGroupName = _context.Namer.Custom("dbsubnet", ResourcePurpose.Main)
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
            InstanceIdentifier = _context.Namer.RdsInstance(ResourcePurpose.Main),
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
            LogGroupName = _context.Namer.LogGroup("ecs", ResourcePurpose.Web),
            Retention = _context.Environment.IsProductionClass
                ? RetentionDays.ONE_MONTH
                : RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        var service = new ApplicationLoadBalancedFargateService(this, "Service",
            new ApplicationLoadBalancedFargateServiceProps
            {
                Cluster = cluster,
                ServiceName = _context.Namer.EcsService(ResourcePurpose.Web),
                TaskImageOptions = new ApplicationLoadBalancedTaskImageOptions
                {
                    Image = ContainerImage.FromRegistry($"{ApplicationName.ToLower()}:latest"),
                    Environment = CreateEnvironmentVariables(database),
                    LogDriver = LogDriver.AwsLogs(new AwsLogDriverProps
                    {
                        LogGroup = logGroup,
                        StreamPrefix = "ecs"
                    }),
                    TaskRole = ImportTaskRole(),
                    ExecutionRole = ImportExecutionRole()
                },
              //  DesiredCount = sizing.MinCapacity,
                PublicLoadBalancer = true,
                Protocol = ApplicationProtocol.HTTPS,
                RedirectHTTP = true,
                TaskDefinition = CreateTaskDefinition(),
                // Use shared security groups
                SecurityGroups = new[] { securityGroups.AlbSg },
                TaskSubnets = EnvironmentResources.GetPrivateSubnetSelection()
            });

        // Additional security group configuration for ECS tasks
        service.Service.Connections.AllowFromAnyIpv4(Port.Tcp(80), "Allow HTTP from ALB");
        service.Service.Connections.AllowFromAnyIpv4(Port.Tcp(443), "Allow HTTPS from ALB");
        
        return service;
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
            Family = _context.Namer.EcsTaskDefinition(ResourcePurpose.Web),
            // MemoryLimitMiB = _context.Application.Sizing.GetMemoryLimit(),
            // Cpu = _context.Application.Sizing.GetCpuLimit()
        });
    }

    /// <summary>
    /// Imports existing IAM role for ECS tasks from external resource
    /// </summary>
    private Amazon.CDK.AWS.IAM.IRole ImportTaskRole()
    {
        var roleName = _context.Namer.IamRole(IamPurpose.EcsTask);
        var roleArn = $"arn:aws:iam::{_context.Environment.AccountId}:role/{roleName}";
        
        return Amazon.CDK.AWS.IAM.Role.FromRoleArn(this, "TaskRole", roleArn);
    }

    /// <summary>
    /// Imports existing IAM role for ECS execution from external resource
    /// </summary>
    private Amazon.CDK.AWS.IAM.IRole ImportExecutionRole()
    {
        var roleName = _context.Namer.IamRole(IamPurpose.EcsExecution);
        var roleArn = $"arn:aws:iam::{_context.Environment.AccountId}:role/{roleName}";
        
        return Amazon.CDK.AWS.IAM.Role.FromRoleArn(this, "ExecutionRole", roleArn);
    }

    private void ApplyCommonTags()
    {
        var tags = _context.GetCommonTags();
        tags["AccountType"] = _context.Environment.AccountType.ToString();

        foreach (var tag in tags) Tags.SetTag(tag.Key, tag.Value);
    }
}

// Helper classes for organizing related resources

// Extension methods for resource sizing