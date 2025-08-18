using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using AppInfraCdkV1.Apps.TrialFinderV2.Builders;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Factory for creating ECS services and scheduled tasks for TrialFinderV2
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

    public EcsServiceFactory(
        Construct scope,
        string id,
        DeploymentContext context,
        SecretManager secretManager,
        EcrRepositoryManager ecrRepositoryManager,
        LoggingManager loggingManager,
        OutputExporter outputExporter) : base(scope, id)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();
        _secretManager = secretManager;
        _ecrRepositoryManager = ecrRepositoryManager;
        _loggingManager = loggingManager;
        _outputExporter = outputExporter;
        _iamRoleBuilder = new IamRoleBuilder(this, "IamRoleBuilder", context);
    }

    /// <summary>
    /// Creates ECS services and scheduled tasks based on configuration
    /// </summary>
    public void CreateServicesAndTasks(ICluster cluster, AlbStackOutputs albOutputs, CognitoStackOutputs cognitoOutputs)
    {
        var ecsConfig = _configLoader.LoadEcsConfig(_context.Environment.Name);
        ecsConfig = _configLoader.SubstituteVariables(ecsConfig, _context);

        foreach (var taskDef in ecsConfig.TaskDefinition)
        {
            if (taskDef.IsScheduledJob)
            {
                CreateLoaderScheduledTask(cluster, taskDef, cognitoOutputs);
            }
            else
            {
                CreateWebApplicationService(cluster, albOutputs, cognitoOutputs, taskDef);
            }
        }
    }

    /// <summary>
    /// Create continuous web application ECS service and attach to ALB
    /// </summary>
    public void CreateWebApplicationService(ICluster cluster, AlbStackOutputs albOutputs, CognitoStackOutputs cognitoOutputs, TaskDefinitionConfig taskDef)
    {
        var logGroup = _loggingManager.CreateLogGroup("trial-finder", ResourcePurpose.Web);

        var taskCpu = taskDef.Cpu ?? 256;
        var taskMemory = taskDef.Memory ?? 512;

        var taskDefinitionName = !string.IsNullOrWhiteSpace(taskDef.TaskDefinitionName)
            ? _context.Namer.EcsTaskDefinition(taskDef.TaskDefinitionName)
            : _context.Namer.EcsTaskDefinition(ResourcePurpose.Web);

        var taskRole = _iamRoleBuilder.CreateTaskRole();
        var executionRole = _iamRoleBuilder.CreateTaskExecutionRole(logGroup);

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

        var primary = AddContainersFromConfiguration(taskDefinition, taskDef, logGroup, cognitoOutputs);

        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "ImportedEcsSecurityGroup", albOutputs.EcsSecurityGroupId);

        var service = new FargateService(this, "TrialFinderService", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = _context.Namer.EcsService(ResourcePurpose.Web),
            TaskDefinition = taskDefinition,
            DesiredCount = _context.Environment.AccountType == AccountType.Production ? 2 : 1,
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

        Console.WriteLine($"📈 Auto-scaling configured for {_context.Application.Name}: {minCapacity}-{maxCapacity} tasks, target: {desiredCapacity}");

        _outputExporter.ExportAllOutputs(service, taskDefinition, taskRole, executionRole);
    }

    /// <summary>
    /// Create an ECS scheduled task using EventBridge Rule
    /// </summary>
    public void CreateLoaderScheduledTask(ICluster cluster, TaskDefinitionConfig taskDef, CognitoStackOutputs cognitoOutputs)
    {
        var logGroup = _loggingManager.CreateLogGroup("trial-finder-loader", ResourcePurpose.Web);

        var taskCpu = taskDef.Cpu ?? 256;
        var taskMemory = taskDef.Memory ?? 512;
        var scheduleExpression = taskDef.ScheduleExpression ?? "cron(0 */6 * * * *)";
        var jobTimeout = taskDef.JobTimeout ?? 3600; // Default to 1 hour

        var taskDefinitionName = !string.IsNullOrWhiteSpace(taskDef.TaskDefinitionName)
            ? _context.Namer.EcsTaskDefinition(taskDef.TaskDefinitionName)
            : _context.Namer.EcsTaskDefinition("loader");

        var taskRole = _iamRoleBuilder.CreateTaskRole();
        var executionRole = _iamRoleBuilder.CreateTaskExecutionRole(logGroup);

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

        // configure containers (no ports/health checks expected)
        AddContainersFromConfiguration(taskDefinition, taskDef, logGroup, cognitoOutputs);

        // Security group selection: reuse web ECS SG if present via import in caller, or run in default private subnets
        // For scheduled tasks, we typically don't attach to ALB, so security group is optional. We'll run in private subnets.

        var rule = new Rule(this, $"LoaderScheduleRule-{taskDefinitionName}", new RuleProps
        {
            Schedule = Amazon.CDK.AWS.Events.Schedule.Expression(scheduleExpression),
            Description = $"Runs {_context.Application.Name} loader task on schedule",
            Enabled = true
        });

        // Configure retry policy if specified
        if (taskDef.RetryPolicy != null)
        {
            Console.WriteLine($"🔄 Configuring retry policy for {taskDefinitionName}: {taskDef.RetryPolicy.MaxRetryAttempts} attempts, {taskDef.RetryPolicy.RetryDelaySeconds}s delay");
        }

        // Configure dead letter queue if enabled
        if (taskDef.DeadLetterQueue?.Enabled == true)
        {
            Console.WriteLine($"📬 Dead letter queue enabled for {taskDefinitionName}: {taskDef.DeadLetterQueue.MaxReceiveCount} max receive count");
        }

        var taskTarget = new EcsTask(new EcsTaskProps
        {
            Cluster = cluster,
            TaskDefinition = taskDefinition,
            SubnetSelection = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS }
        });

        // Log job timeout configuration
        Console.WriteLine($"⏱️  Job timeout configured for {taskDefinitionName}: {jobTimeout} seconds");

        rule.AddTarget(taskTarget);

        _outputExporter.ExportScheduledTaskOutputs(taskDefinition.TaskDefinitionArn, taskDefinition.Family);
        _outputExporter.ExportIamRoleOutputs(taskRole, executionRole);
    }

    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig taskDefConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs)
    {
        var containerDefinitions = taskDefConfig.ContainerDefinitions;
        if (containerDefinitions == null || containerDefinitions.Count == 0)
        {
            AddPlaceholderContainer(taskDefinition, logGroup);
            return new ContainerInfo("app", 8080);
        }

        ContainerInfo? primary = null;
        foreach (var containerConfig in containerDefinitions)
        {
            if (string.IsNullOrWhiteSpace(containerConfig.Name))
            {
                throw new InvalidOperationException("Container name is required in configuration");
            }

            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, cognitoOutputs);

            if (primary == null)
            {
                var port = GetPrimaryPort(containerConfig);
                primary = new ContainerInfo(containerConfig.Name, port ?? 0);
            }
        }

        return primary ?? new ContainerInfo("app", 0);
    }

    private void AddPlaceholderContainer(FargateTaskDefinition taskDefinition, ILogGroup logGroup)
    {
        var placeholderRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, "PlaceholderRepository",
            "thirdopinion/infra/deploy-placeholder");

        taskDefinition.AddContainer("app", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(placeholderRepository, "latest"),
            Essential = true,
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 8080,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            Environment = CreateDefaultEnvironmentVariables("app"),
            Logging = _loggingManager.SetupLoggingDriver(logGroup, "app"),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            }
        });
    }

    private void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs)
    {
        var containerName = containerConfig.Name ?? "app";

        ContainerImage image;
        Dictionary<string, string> env;

        if (string.IsNullOrWhiteSpace(containerConfig.Image) || containerConfig.Image == "placeholder")
        {
            var ecrImageUri = _ecrRepositoryManager.GetLatestEcrImageUri(containerName, _context);
            if (!string.IsNullOrEmpty(ecrImageUri))
            {
                image = ContainerImage.FromRegistry(ecrImageUri);
                env = CreateDefaultEnvironmentVariables(containerName);
                env["IMAGE_SOURCE"] = "ecr";
            }
            else
            {
                var placeholderRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"PlaceholderRepository-{containerName}",
                    "thirdopinion/infra/deploy-placeholder");
                image = ContainerImage.FromEcrRepository(placeholderRepository, "latest");
                env = CreateDefaultEnvironmentVariables(containerName);
                env["IMAGE_SOURCE"] = "placeholder";
            }
        }
        else
        {
            image = ContainerImage.FromRegistry(containerConfig.Image);
            env = CreateDefaultEnvironmentVariables(containerName);
            env["IMAGE_SOURCE"] = "specified";
        }

        // merge env from config
        if (containerConfig.Environment?.Count > 0)
        {
            foreach (var e in containerConfig.Environment.Where(e => !string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.Value)))
            {
                env[e.Name!] = e.Value!;
            }
        }

        var options = new ContainerDefinitionOptions
        {
            Image = image,
            ContainerName = containerName,
            Cpu = containerConfig.Cpu ?? 0,
            Essential = containerConfig.Essential ?? true,
            Environment = env,
            Secrets = _secretManager.GetContainerSecrets(containerConfig.Secrets, cognitoOutputs, _context),
            Logging = _loggingManager.SetupLoggingDriver(logGroup, containerName)
        };

        var portMappings = GetPortMappings(containerConfig, containerName);
        if (portMappings.Length > 0)
        {
            options.PortMappings = portMappings;
        }

        var healthCheck = GetHealthCheck(containerConfig);
        if (healthCheck != null)
        {
            options.HealthCheck = healthCheck;
        }

        taskDefinition.AddContainer(containerName, options);
    }

    private Dictionary<string, string> CreateDefaultEnvironmentVariables(string containerName)
    {
        return new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = _context.Environment.Name,
            ["AWS_REGION"] = _context.Environment.Region,
            ["PORT"] = "8080",
            ["APP_NAME"] = _context.Application.Name,
            ["CONTAINER_NAME"] = containerName
        };
    }

    private int? GetPrimaryPort(ContainerDefinitionConfig containerConfig)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            var preferred = containerConfig.PortMappings.FirstOrDefault(pm => pm.ContainerPort == 8080);
            if (preferred?.ContainerPort != null) return preferred.ContainerPort;
            var first = containerConfig.PortMappings.FirstOrDefault(pm => pm.ContainerPort != null);
            return first?.ContainerPort;
        }
        return null;
    }

    private Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            return containerConfig.PortMappings
                .Where(pm => pm.ContainerPort.HasValue)
                .Select(pm => new Amazon.CDK.AWS.ECS.PortMapping
                {
                    Name = $"{containerName}-{pm.ContainerPort}-{(pm.Protocol ?? "tcp").ToLowerInvariant()}",
                    ContainerPort = pm.ContainerPort!.Value,
                    HostPort = pm.HostPort,
                    Protocol = (pm.Protocol ?? "tcp").ToUpperInvariant() == "UDP" ? Amazon.CDK.AWS.ECS.Protocol.UDP : Amazon.CDK.AWS.ECS.Protocol.TCP,
                    AppProtocol = string.Equals(pm.AppProtocol, "grpc", StringComparison.OrdinalIgnoreCase)
                        ? AppProtocol.Grpc
                        : AppProtocol.Http
                })
                .ToArray();
        }
        return Array.Empty<Amazon.CDK.AWS.ECS.PortMapping>();
    }

    private Amazon.CDK.AWS.ECS.HealthCheck? GetHealthCheck(ContainerDefinitionConfig containerConfig)
    {
        if (containerConfig.DisableHealthCheck == true)
        {
            return null;
        }

        if (containerConfig.HealthCheck?.Disabled == true)
        {
            return null;
        }

        // If no ports defined, treat as headless job and skip health check
        if (containerConfig.PortMappings == null || containerConfig.PortMappings.Count == 0)
        {
            return null;
        }

        // Use custom command if provided
        if (containerConfig.HealthCheck?.Command != null && containerConfig.HealthCheck.Command.Count > 0)
        {
            return new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = containerConfig.HealthCheck.Command.ToArray(),
                Interval = Duration.Seconds(containerConfig.HealthCheck.Interval ?? 30),
                Timeout = Duration.Seconds(containerConfig.HealthCheck.Timeout ?? 5),
                Retries = containerConfig.HealthCheck.Retries ?? 3,
                StartPeriod = Duration.Seconds(containerConfig.HealthCheck.StartPeriod ?? 60)
            };
        }

        // Default HTTP health check on /health
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[] { "CMD-SHELL", "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1" },
            Interval = Duration.Seconds(30),
            Timeout = Duration.Seconds(5),
            Retries = 3,
            StartPeriod = Duration.Seconds(60)
        };
    }
}

public class AlbStackOutputs
{
    public string TargetGroupArn { get; set; } = "";
    public string EcsSecurityGroupId { get; set; } = "";
}

public class ContainerInfo
{
    public string ContainerName { get; }
    public int ContainerPort { get; }

    public ContainerInfo(string containerName, int containerPort)
    {
        ContainerName = containerName;
        ContainerPort = containerPort;
    }
}


