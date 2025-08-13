using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.EC2;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Apps.TrialMatch.Builders;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Enums;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Services;

/// <summary>
/// Factory for creating ECS services in the TrialMatch application
/// 
/// This service handles:
/// - ECS service creation from configuration
/// - Task definition creation using TaskDefinitionBuilder
/// - Container configuration using ContainerDefinitionBuilder
/// - Health check configuration using HealthCheckBuilder
/// - Port mapping configuration using PortMappingBuilder
/// - Service attachment to ALB target groups
/// </summary>
public class EcsServiceFactory : Construct
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly SecretManager _secretManager;
    private readonly EcrRepositoryManager _ecrRepositoryManager;
    private readonly LoggingManager _loggingManager;
    private readonly OutputExporter _outputExporter;
    private readonly TaskDefinitionBuilder _taskDefinitionBuilder;
    private readonly ContainerDefinitionBuilder _containerDefinitionBuilder;
    private readonly HealthCheckBuilder _healthCheckBuilder;
    private readonly PortMappingBuilder _portMappingBuilder;
    private readonly EnvironmentVariableBuilder _environmentVariableBuilder;

    public EcsServiceFactory(Construct scope, string id, DeploymentContext context, SecretManager secretManager, EcrRepositoryManager ecrRepositoryManager, LoggingManager loggingManager, OutputExporter outputExporter) : base(scope, id)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();
        _secretManager = secretManager;
        _ecrRepositoryManager = ecrRepositoryManager;
        _loggingManager = loggingManager;
        _outputExporter = outputExporter;
        
        // Initialize builders
        _taskDefinitionBuilder = new TaskDefinitionBuilder(this, "TaskDefinitionBuilder", context);
        _containerDefinitionBuilder = new ContainerDefinitionBuilder(context);
        _healthCheckBuilder = new HealthCheckBuilder();
        _portMappingBuilder = new PortMappingBuilder();
        _environmentVariableBuilder = new EnvironmentVariableBuilder(context);
    }

    /// <summary>
    /// Create ECS services from configuration
    /// </summary>
    public void CreateEcsServices(ICluster cluster, AlbStackOutputs albOutputs, DeploymentContext context)
    {
        // Load ECS configuration
        var ecsConfig = _configLoader.LoadEcsConfig(context.Environment.Name);
        
        if (ecsConfig.Services == null || ecsConfig.Services.Count == 0)
        {
            throw new InvalidOperationException("No services configured in ECS configuration");
        }

        foreach (var serviceConfig in ecsConfig.Services)
        {
            CreateEcsService(cluster, albOutputs, context, serviceConfig);
        }
    }

    /// <summary>
    /// Create a single ECS service with containers from configuration
    /// </summary>
    public void CreateEcsService(ICluster cluster, AlbStackOutputs albOutputs, DeploymentContext context, ServiceConfig serviceConfig)
    {
        // Create log group for the service using LoggingManager
        var logGroup = _loggingManager.CreateLogGroup(serviceConfig.ServiceName);

        // Create task definition using TaskDefinitionBuilder
        var taskDefinition = _taskDefinitionBuilder.CreateFargateTaskDefinition(
            this,
            serviceConfig.ServiceName,
            cluster,
            _taskDefinitionBuilder.CreateTaskRole(serviceConfig.ServiceName),
            _taskDefinitionBuilder.CreateExecutionRole(logGroup, serviceConfig.ServiceName),
            logGroup);

        // Add containers from configuration
        var containerInfo = AddContainersFromConfiguration(taskDefinition, serviceConfig.TaskDefinition.FirstOrDefault(), logGroup, context);

        // Create security group for ECS tasks
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, $"EcsSecurityGroup-{serviceConfig.ServiceName}", albOutputs.EcsSecurityGroupId);

        // Create ECS service with deployment-friendly settings
        var service = new FargateService(this, $"TrialMatchService-{serviceConfig.ServiceName}", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = $"{context.Namer.EcsService(ResourcePurpose.Web)}-{serviceConfig.ServiceName.Replace("trial-match-", "")}",
            TaskDefinition = taskDefinition,
            DesiredCount = context.Environment.AccountType == AccountType.Production ? 2 : 1,
            MinHealthyPercent = 0, // Allow all tasks to be replaced during deployment
            MaxHealthyPercent = 200,
            AssignPublicIp = false,
            SecurityGroups = new[] { ecsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            EnableExecuteCommand = true
        });

        // Only attach to target group if primary container has a valid port
        if (containerInfo.ContainerPort > 0)
        {
            Console.WriteLine($"🔗 Attaching ECS service '{serviceConfig.ServiceName}' to ALB target group");
            Console.WriteLine($"   Container: {containerInfo.ContainerName}");
            Console.WriteLine($"   Port: {containerInfo.ContainerPort}");
            
            // Import target group from ALB stack and attach service
            var targetGroupArn = serviceConfig.ServiceName.Contains("api") ? albOutputs.ApiTargetGroupArn : albOutputs.FrontendTargetGroupArn;
            var targetGroup = ApplicationTargetGroup.FromTargetGroupAttributes(this,
                $"ImportedTargetGroup-{serviceConfig.ServiceName}", new TargetGroupAttributes
                {
                    TargetGroupArn = targetGroupArn,
                    LoadBalancerArns = targetGroupArn // This will be overridden by the actual target group
                });

            // Register ECS service with target group using explicit container and port
            service.AttachToApplicationTargetGroup(targetGroup);
            Console.WriteLine($"✅ ECS service '{serviceConfig.ServiceName}' attached to target group successfully");
        }
        else
        {
            Console.WriteLine($"⚠️  Skipping ALB target group attachment for service '{serviceConfig.ServiceName}' - no container with valid port found");
        }

        // Export task definition outputs using OutputExporter service
        _outputExporter.ExportTaskDefinitionOutputs(taskDefinition, service, serviceConfig.ServiceName, containerInfo);
    }

    /// <summary>
    /// Add containers from configuration to task definition
    /// </summary>
    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig? taskDefConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        var containerDefinitions = taskDefConfig?.ContainerDefinitions ?? new List<ContainerDefinitionConfig>();
        
        if (containerDefinitions.Count == 0)
        {
            // Fallback to default container if no configuration provided
            AddDefaultContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("trial-match", 8080);
        }

        ContainerInfo? mainContainerInfo = null;

        foreach (var containerConfig in containerDefinitions)
        {
            if (containerConfig.Name == null) continue;

            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, context);

            // Track the main container (first essential container)
            if (mainContainerInfo == null && (containerConfig.Essential ?? _containerDefinitionBuilder.GetDefaultEssential(containerConfig.Name)))
            {
                var containerPort = _containerDefinitionBuilder.GetContainerPort(containerConfig, containerConfig.Name) ?? 8080;
                mainContainerInfo = new ContainerInfo(containerConfig.Name, containerPort);
            }
        }

        // Ensure we have at least one container
        if (mainContainerInfo == null)
        {
            AddDefaultContainer(taskDefinition, logGroup, context);
            mainContainerInfo = new ContainerInfo("trial-match", 8080);
        }

        return mainContainerInfo;
    }

    /// <summary>
    /// Add a configured container to the task definition
    /// </summary>
    private void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        if (containerConfig.Name == null) return;

        Console.WriteLine($"\n  📦 Adding container '{containerConfig.Name}':");
        Console.WriteLine($"     Image: {containerConfig.Image ?? "placeholder"}");
        Console.WriteLine($"     CPU: {containerConfig.Cpu ?? 0}");
        Console.WriteLine($"     Essential: {containerConfig.Essential ?? _containerDefinitionBuilder.GetDefaultEssential(containerConfig.Name)}");
        Console.WriteLine($"     Port Mappings: {containerConfig.PortMappings?.Count ?? 0}");
        Console.WriteLine($"     Environment Variables: {containerConfig.Environment?.Count ?? 0}");
        Console.WriteLine($"     Secrets: {containerConfig.Secrets?.Count ?? 0}");

        // Determine if we should use ECR latest image, placeholder, or specified image
        ContainerImage containerImage;
        Dictionary<string, string> environmentVars;
        
        if (string.IsNullOrWhiteSpace(containerConfig.Image) || containerConfig.Image == "placeholder")
        {
            // Check if ECR repository has latest image first
            var ecrImageUri = _ecrRepositoryManager.GetLatestEcrImageUri(containerConfig.Name, context);
            if (!string.IsNullOrEmpty(ecrImageUri))
            {
                // Use latest image from ECR repository
                containerImage = ContainerImage.FromRegistry(ecrImageUri);
                var repository = _ecrRepositoryManager.GetRepository(containerConfig.Name);
                environmentVars = _environmentVariableBuilder.CreateEcrEnvironmentVariables(containerConfig.Name, ecrImageUri, repository?.RepositoryName ?? "unknown", containerConfig);
                Console.WriteLine($"     🚀 Using latest ECR image for container '{containerConfig.Name}': {ecrImageUri}");
            }
            else
            {
                // Fall back to placeholder image
                var placeholderRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"PlaceholderRepository-{containerConfig.Name}",
                    "thirdopinion/infra/deploy-placeholder");
                containerImage = ContainerImage.FromEcrRepository(placeholderRepository, "latest");
                
                // Add placeholder-specific environment variables
                environmentVars = _environmentVariableBuilder.CreatePlaceholderEnvironmentVariables(containerConfig.Name);
                Console.WriteLine($"     📦 Using placeholder image for container '{containerConfig.Name}' (no latest ECR image found)");
            }
        }
        else
        {
            // Use specified image
            containerImage = ContainerImage.FromRegistry(containerConfig.Image);
            environmentVars = _environmentVariableBuilder.CreateSpecifiedImageEnvironmentVariables(containerConfig.Name, containerConfig.Image, containerConfig);
            Console.WriteLine($"     🎯 Using specified image for container '{containerConfig.Name}': {containerConfig.Image}");
        }

        // Build container definition using ContainerDefinitionBuilder
        var containerOptions = _containerDefinitionBuilder.BuildContainerDefinition(
            containerConfig,
            containerImage,
            logGroup,
            environmentVars,
            _healthCheckBuilder.GetContainerHealthCheck(containerConfig, containerConfig.Name),
            _portMappingBuilder.GetPortMappings(containerConfig, containerConfig.Name));

        // Add port mappings only if they exist in configuration
        var portMappings = _portMappingBuilder.GetPortMappings(containerConfig, containerConfig.Name);
        if (portMappings.Length > 0)
        {
            containerOptions.PortMappings = portMappings;
            Console.WriteLine($"     Added {portMappings.Length} port mapping(s) to container '{containerConfig.Name}'");
        }
        else
        {
            Console.WriteLine($"     No port mappings configured for container '{containerConfig.Name}'");
        }

        taskDefinition.AddContainer(containerConfig.Name, containerOptions);
    }

    /// <summary>
    /// Add default container when no configuration is provided
    /// </summary>
    private void AddDefaultContainer(FargateTaskDefinition taskDefinition, ILogGroup logGroup, DeploymentContext context)
    {
        Console.WriteLine("  📦 Adding default container 'trial-match'");
        
        var containerOptions = new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("thirdopinion/infra/deploy-placeholder:latest"),
            ContainerName = "trial-match",
            Cpu = 0,
            Essential = true,
            Environment = _environmentVariableBuilder.CreateDefaultEnvironmentVariables("trial-match"),
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            }),
            HealthCheck = _healthCheckBuilder.GetStandardHealthCheck(null),
            PortMappings = _portMappingBuilder.CreateDefaultPortMappings()
        };

        taskDefinition.AddContainer("trial-match", containerOptions);
    }



}

/// <summary>
/// Container information for ECS services
/// </summary>
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

/// <summary>
/// ALB stack outputs
/// </summary>
public class AlbStackOutputs
{
    public string ApiTargetGroupArn { get; set; } = "";
    public string FrontendTargetGroupArn { get; set; } = "";
    public string EcsSecurityGroupId { get; set; } = "";
} 