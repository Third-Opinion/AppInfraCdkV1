using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;


using AppInfraCdkV1.Apps.TrialFinderV2.Builders;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;
using AppInfraCdkV1.Apps.TrialFinderV2;


namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Factory for creating ECS services for TrialFinderV2
/// </summary>
public class EcsServiceFactory : Construct
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly SecretManager _secretManager;
    private readonly EcrRepositoryManager _ecrRepositoryManager;
    private readonly LoggingManager _loggingManager;
    private readonly OutputExporter _outputExporter;
    private readonly IamRoleBuilder _iamRoleBuilder;
    private readonly ContainerConfigurationService _containerConfigurationService;

    public EcsServiceFactory(
        Construct scope,
        string id,
        DeploymentContext context,
        SecretManager secretManager,
        EcrRepositoryManager ecrRepositoryManager,
        LoggingManager loggingManager,
        OutputExporter outputExporter,
        IamRoleBuilder iamRoleBuilder,
        ContainerConfigurationService containerConfigurationService) : base(scope, id)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();
        _secretManager = secretManager;
        _ecrRepositoryManager = ecrRepositoryManager;
        _loggingManager = loggingManager;
        _outputExporter = outputExporter;
        _iamRoleBuilder = iamRoleBuilder;
        _containerConfigurationService = containerConfigurationService;
    }

    /// <summary>
    /// Creates ECS services based on configuration
    /// </summary>
    public void CreateServicesAndTasks(ICluster cluster, TrialFinderV2EcsStack.AlbStackOutputs albOutputs, TrialFinderV2EcsStack.CognitoStackOutputs cognitoOutputs)
    {
        var ecsConfig = _configLoader.LoadEcsConfig(_context.Environment.Name);
        ecsConfig = _configLoader.SubstituteVariables(ecsConfig, _context);

        Console.WriteLine($"     🔍 Loaded ECS configuration with {ecsConfig.TaskDefinition?.Count ?? 0} task definition(s)");
        
        if (ecsConfig.TaskDefinition != null)
        {
            foreach (var taskDef in ecsConfig.TaskDefinition)
            {
                Console.WriteLine($"     📋 Found task definition: '{taskDef.TaskDefinitionName}' (CPU: {taskDef.Cpu}, Memory: {taskDef.Memory})");
            }
        }

        // Create web application service for main task definition
        var mainTaskDef = ecsConfig.TaskDefinition?.FirstOrDefault(t => t.TaskDefinitionName == "main");
        if (mainTaskDef != null)
        {
            Console.WriteLine($"     🚀 Creating web application service for main task definition");
            CreateWebApplicationService(cluster, albOutputs, cognitoOutputs, mainTaskDef);
        }
        else
        {
            Console.WriteLine($"     ⚠️  Main task definition not found in configuration");
        }

        // Create scheduled job for loader task definition
        var loaderTaskDef = ecsConfig.TaskDefinition?.FirstOrDefault(t => t.TaskDefinitionName == "loader");
        if (loaderTaskDef != null)
        {
            Console.WriteLine($"     ⏰ Creating scheduled job for loader task definition");
            CreateScheduledJob(cluster, cognitoOutputs, loaderTaskDef, albOutputs.EcsSecurityGroupId);
        }
        else
        {
            Console.WriteLine($"     ⚠️  Loader task definition not found in configuration");
        }
    }

    /// <summary>
    /// Create continuous web application ECS service and attach to ALB
    /// </summary>
    public void CreateWebApplicationService(ICluster cluster, TrialFinderV2EcsStack.AlbStackOutputs albOutputs, TrialFinderV2EcsStack.CognitoStackOutputs cognitoOutputs, TaskDefinitionConfig taskDef)
    {
        var logGroup = _loggingManager.CreateLogGroup("trial-finder", ResourcePurpose.Web);

        var taskCpu = taskDef.Cpu ?? 256;
        var taskMemory = taskDef.Memory ?? 512;

        var taskDefinitionName = !string.IsNullOrWhiteSpace(taskDef.TaskDefinitionName)
            ? _context.Namer.EcsTaskDefinition(taskDef.TaskDefinitionName)
            : _context.Namer.EcsTaskDefinition(ResourcePurpose.Web);

        var taskRole = _iamRoleBuilder.CreateTaskRole("WebApp");
        var executionRole = _iamRoleBuilder.CreateTaskExecutionRole(logGroup, "WebApp");

        var taskDefinition = new FargateTaskDefinition(this, taskDefinitionName, new FargateTaskDefinitionProps
        {
            Family = taskDefinitionName,
            MemoryLimitMiB = taskMemory,
            Cpu = taskCpu,
            TaskRole = taskRole,
            ExecutionRole = executionRole,
            RuntimePlatform = new RuntimePlatform
            {
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
                CpuArchitecture = CpuArchitecture.X86_64
            }
        });

        var primary = _containerConfigurationService.AddContainersFromConfiguration(taskDefinition, taskDef, logGroup, cognitoOutputs, _context);

        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "ImportedEcsSecurityGroup", albOutputs.EcsSecurityGroupId);

        var service = new FargateService(this, "TrialFinderService", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = _context.Namer.EcsService(ResourcePurpose.Web),
            TaskDefinition = taskDefinition,
            DesiredCount = 1, // Fixed: Always use 1 instance to prevent antiforgery token issues
            MinHealthyPercent = 0,
            MaxHealthyPercent = 200,
            AssignPublicIp = false,
            SecurityGroups = new[] { ecsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            EnableExecuteCommand = true
        });

        if (primary.ContainerPort > 0)
        {
            var targetGroup = ApplicationTargetGroup.FromTargetGroupAttributes(this,
                "ImportedTargetGroup", new TargetGroupAttributes
                {
                    TargetGroupArn = albOutputs.TargetGroupArn,
                    LoadBalancerArns = albOutputs.TargetGroupArn
                });

            service.AttachToApplicationTargetGroup(targetGroup);
        }

        // Configure auto-scaling based on environment
        var (minCapacity, maxCapacity, desiredCapacity) = AppInfraCdkV1.Core.Configuration.EnvironmentSizing.GetAutoScalingConfig(_context.Environment.Name);
        
        var scaling = service.AutoScaleTaskCount(new EnableScalingProps
        {
            MinCapacity = minCapacity,
            MaxCapacity = maxCapacity
        });

        // Scale up when CPU utilization is high
        scaling.ScaleOnCpuUtilization("CpuScaling", new CpuUtilizationScalingProps
        {
            TargetUtilizationPercent = 70,
            ScaleInCooldown = Duration.Seconds(60),
            ScaleOutCooldown = Duration.Seconds(60)
        });

        // Scale up when memory utilization is high
        scaling.ScaleOnMemoryUtilization("MemoryScaling", new MemoryUtilizationScalingProps
        {
            TargetUtilizationPercent = 80,
            ScaleInCooldown = Duration.Seconds(60),
            ScaleOutCooldown = Duration.Seconds(60)
        });

        // Export outputs
        _outputExporter.ExportEcsOutputs(service, taskDefinition);
        _outputExporter.ExportIamRoleOutputs(taskRole, executionRole, null, "WebApp");
        
        // Export ECR repository outputs for GitHub Actions
        _ecrRepositoryManager.ExportEcrRepositoryOutputs();
    }

    /// <summary>
    /// Create scheduled ECS task for background jobs
    /// </summary>
    public void CreateScheduledJob(ICluster cluster, TrialFinderV2EcsStack.CognitoStackOutputs cognitoOutputs, TaskDefinitionConfig taskDef, string? securityGroupId = null)
    {
        var logGroup = _loggingManager.CreateLogGroup("trial-finder-loader", ResourcePurpose.Internal);

        var taskCpu = taskDef.Cpu ?? 256;
        var taskMemory = taskDef.Memory ?? 512;

        var taskDefinitionName = !string.IsNullOrWhiteSpace(taskDef.TaskDefinitionName)
            ? _context.Namer.EcsTaskDefinition(taskDef.TaskDefinitionName)
            : _context.Namer.EcsTaskDefinition(ResourcePurpose.Internal);

        var taskRole = _iamRoleBuilder.CreateTaskRole("BackgroundJob");
        var executionRole = _iamRoleBuilder.CreateTaskExecutionRole(logGroup, "BackgroundJob");

        var taskDefinition = new FargateTaskDefinition(this, taskDefinitionName, new FargateTaskDefinitionProps
        {
            Family = taskDefinitionName,
            MemoryLimitMiB = taskMemory,
            Cpu = taskCpu,
            TaskRole = taskRole,
            ExecutionRole = executionRole,
            RuntimePlatform = new RuntimePlatform
            {
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
                CpuArchitecture = CpuArchitecture.X86_64
            }
        });

        _containerConfigurationService.AddContainersFromConfiguration(taskDefinition, taskDef, logGroup, cognitoOutputs, _context);

        // Create EventBridge rule to schedule the task using high-level CDK constructs
        var ruleName = $"{_context.Application.Name.ToLowerInvariant()}-{_context.Environment.Name}-loader-schedule";
        var constructId = $"LoaderScheduleRule-{taskDef.TaskDefinitionName}";
        var scheduleExpression = taskDef.ScheduleExpression ?? "cron(0 * * * ? *)";
        
        Console.WriteLine($"     🔧 Creating EventBridge rule construct with ID: {constructId}");
        Console.WriteLine($"     📅 Schedule expression: {scheduleExpression}");
        Console.WriteLine($"     🎯 Target: ECS Task {taskDefinitionName} on cluster {cluster.ClusterName}");
        
        // Create EventBridge rule using high-level CDK constructs
        var rule = new Rule(this, constructId, new RuleProps
        {
            RuleName = ruleName,
            Description = $"Scheduled job for {taskDefinitionName}",
            Schedule = Amazon.CDK.AWS.Events.Schedule.Expression(scheduleExpression),
            Enabled = true
        });
        
        // Create ECS task target using the proper CDK approach
        var ecsTarget = new EcsTask(new EcsTaskProps
        {
            Cluster = cluster,
            TaskDefinition = taskDefinition,
            TaskCount = 1,
            LaunchType = LaunchType.FARGATE,
            // Add security group configuration if provided
            SecurityGroups = !string.IsNullOrEmpty(securityGroupId) 
                ? new[] { SecurityGroup.FromSecurityGroupId(this, $"LoaderSecurityGroup-{taskDef.TaskDefinitionName}", securityGroupId) }
                : null
        });
        
        // Add the target to the rule - this is the key step!
        rule.AddTarget(ecsTarget);
        
        Console.WriteLine($"     ✅ Created EventBridge rule '{ruleName}' with ECS task target");
        Console.WriteLine($"     📅 Schedule: {scheduleExpression}");
        if (!string.IsNullOrEmpty(securityGroupId))
        {
            Console.WriteLine($"     🔒 Using security group: {securityGroupId}");
        }
        Console.WriteLine($"     🔍 Rule construct path: {rule.Node.Path}");
        Console.WriteLine($"     🔍 Rule construct ID: {rule.Node.Id}");

        // Export outputs for the scheduled task
        _outputExporter.ExportScheduledTaskOutputs(taskDefinition.TaskDefinitionArn, taskDefinition.Family);
        _outputExporter.ExportIamRoleOutputs(taskRole, executionRole, null, "BackgroundJob");
        
        // Export ECR repository outputs for GitHub Actions
        _ecrRepositoryManager.ExportEcrRepositoryOutputs();
    }
}




