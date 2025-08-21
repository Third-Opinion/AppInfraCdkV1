using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Models;
using Constructs;
using CognitoStackOutputs = AppInfraCdkV1.Apps.TrialFinderV2.TrialFinderV2EcsStack.CognitoStackOutputs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Service for managing container configuration in ECS task definitions
/// </summary>
public class ContainerConfigurationService : Construct
{
    private readonly DeploymentContext _context;

    public ContainerConfigurationService(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Add containers from configuration with conditional logic
    /// </summary>
    public ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig? taskDefConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs,
        DeploymentContext context)
    {
        var containerDefinitions = taskDefConfig?.ContainerDefinitions;
        if (containerDefinitions == null || containerDefinitions.Count == 0)
        {
            // Fallback to placeholder container if no configuration provided
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("app", 8080);
        }

        ContainerInfo? primaryContainer = null;
        var containersProcessed = 0;

        foreach (var containerConfig in containerDefinitions)
        {
            if (string.IsNullOrWhiteSpace(containerConfig.Name))
            {
                throw new InvalidOperationException("Container name is required in configuration");
            }
            var containerName = containerConfig.Name;
            var containerPort = GetContainerPort(containerConfig, containerName);
            
            Console.WriteLine($"  📋 Adding container: {containerName}");
            Console.WriteLine($"     Image: {containerConfig.Image ?? "placeholder"}");
            Console.WriteLine($"     Essential: {containerConfig.Essential ?? true}");
            Console.WriteLine($"     Port mappings: {containerConfig.PortMappings?.Count ?? 0}");
            Console.WriteLine($"     Secrets: {containerConfig.Secrets?.Count ?? 0}");
            
            if (containerPort.HasValue)
            {
                Console.WriteLine($"     Primary port: {containerPort.Value}");
            }
            else
            {
                Console.WriteLine($"     Primary port: None (no port mappings)");
            }
            
            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, cognitoOutputs, context);
            containersProcessed++;

            // Use the first container with ports as the primary container for load balancing
            if (primaryContainer == null && containerPort.HasValue)
            {
                primaryContainer = new ContainerInfo(containerName, containerPort.Value);
                Console.WriteLine($"  ✅ Selected '{containerName}' as primary container for load balancing (port: {containerPort.Value})");
            }
        }

        // If no containers were processed at all, fall back to placeholder
        if (containersProcessed == 0)
        {
            Console.WriteLine("  ⚠️  No containers defined in configuration, adding placeholder container");
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("app", 8080);
        }

        // If containers were processed but none have ports, we can't attach to load balancer
        // Return null to indicate no primary container available
        if (primaryContainer == null)
        {
            // Use the first container name for reference, even without ports
            var firstContainerName = containerDefinitions.First().Name ?? "default-container";
            Console.WriteLine($"  ⚠️  No containers with port mappings found. Load balancer attachment will be skipped.");
            Console.WriteLine($"     Using '{firstContainerName}' as reference container (no ports)");
            return new ContainerInfo(firstContainerName, 0); // Port 0 indicates no port mapping
        }

        Console.WriteLine($"  📊 Container configuration summary:");
        Console.WriteLine($"     Total containers: {containersProcessed}");
        Console.WriteLine($"     Primary container: {primaryContainer.ContainerName} (port: {primaryContainer.ContainerPort})");

        return primaryContainer;
    }

    /// <summary>
    /// Add a container based on configuration with comprehensive defaults
    /// </summary>
    public void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs,
        DeploymentContext context)
    {
        var containerName = containerConfig.Name ?? "default-container";
        
        // Determine if we should use ECR latest image, placeholder, or specified image
        ContainerImage containerImage;
        Dictionary<string, string> environmentVars;
        
        if (!string.IsNullOrWhiteSpace(containerConfig.Image) && containerConfig.Image != "placeholder")
        {
            // Use specified image
            containerImage = ContainerImage.FromRegistry(containerConfig.Image);
            environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
        }
        else if (containerConfig.Image == "placeholder" && containerConfig.Repository?.Type != null)
        {
            // Use ECR repository for placeholder images
            var repositoryName = context.Namer.EcrRepository(containerConfig.Repository.Type);
            var accountId = context.Environment.AccountId;
            var region = context.Environment.Region;
            var ecrImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:latest";
            containerImage = ContainerImage.FromRegistry(ecrImageUri);
            environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
            Console.WriteLine($"  🔄 Using ECR image for placeholder: {ecrImageUri}");
        }
        else
        {
            // Fallback to nginx placeholder image for development/testing
            containerImage = ContainerImage.FromRegistry("public.ecr.aws/docker/library/nginx:alpine");
            environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
            Console.WriteLine($"  ⚠️  No ECR repository configured, using nginx placeholder");
        }

        // Create container definition
        var containerDefinition = taskDefinition.AddContainer(containerName, new ContainerDefinitionOptions
        {
            Image = containerImage,
            Essential = containerConfig.Essential ?? true,
            MemoryLimitMiB = 512, // Use default memory value since config doesn't have memory property
            Cpu = containerConfig.Cpu ?? 256,
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = containerName
            }),
            Environment = environmentVars,
            HealthCheck = GetContainerHealthCheck(containerConfig, containerName),
            PortMappings = GetPortMappings(containerConfig, containerName)
        });

        // Add secrets if configured
        if (containerConfig.Secrets != null && containerConfig.Secrets.Count > 0)
        {
            // This will be handled by the SecretManager service
            Console.WriteLine($"  🔐 Container '{containerName}' has {containerConfig.Secrets.Count} secrets configured");
        }
    }

    /// <summary>
    /// Add a placeholder container for development/testing
    /// </summary>
    public void AddPlaceholderContainer(FargateTaskDefinition taskDefinition, ILogGroup logGroup, DeploymentContext context)
    {
        Console.WriteLine("  📦 Adding placeholder container (nginx) for development/testing");
        
        taskDefinition.AddContainer("app", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("public.ecr.aws/docker/library/nginx:alpine"),
            Essential = true,
            MemoryLimitMiB = 256,
            Cpu = 128,
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "app"
            }),
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 80,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "curl -f http://localhost/ || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            }
        });
    }

    /// <summary>
    /// Get container health check configuration
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck? GetContainerHealthCheck(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.HealthCheck == null)
        {
            return null;
        }

        var healthCheck = containerConfig.HealthCheck;
        var healthCheckPath = GetHealthCheckPath(containerConfig);
        
        if (string.IsNullOrWhiteSpace(healthCheckPath))
        {
            return null;
        }

        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[] { "CMD-SHELL", $"curl -f {healthCheckPath} || exit 1" },
            Interval = Duration.Seconds(healthCheck.Interval ?? 30),
            Timeout = Duration.Seconds(healthCheck.Timeout ?? 5),
            Retries = healthCheck.Retries ?? 3,
            StartPeriod = Duration.Seconds(healthCheck.StartPeriod ?? 60)
        };
    }

    /// <summary>
    /// Get port mappings for container
    /// </summary>
    private Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings == null || containerConfig.PortMappings.Count == 0)
        {
            return new Amazon.CDK.AWS.ECS.PortMapping[0];
        }

        return containerConfig.PortMappings.Select(pm => new Amazon.CDK.AWS.ECS.PortMapping
        {
            ContainerPort = pm.ContainerPort ?? 0,
            HostPort = pm.HostPort,
            Protocol = GetProtocol(pm.Protocol),
            Name = GeneratePortMappingName(containerName, pm.ContainerPort ?? 0, pm.Protocol ?? "tcp"),
            AppProtocol = GetAppProtocol(pm.AppProtocol)
        }).ToArray();
    }

    /// <summary>
    /// Get environment variables for container
    /// </summary>
    private Dictionary<string, string> GetEnvironmentVariables(ContainerDefinitionConfig containerConfig, DeploymentContext context, string containerName)
    {
        var envVars = new Dictionary<string, string>();

        // Add container-specific environment variables
        if (containerConfig.Environment != null)
        {
            foreach (var envVar in containerConfig.Environment)
            {
                if (!string.IsNullOrWhiteSpace(envVar.Name) && !string.IsNullOrWhiteSpace(envVar.Value))
                {
                    envVars[envVar.Name] = envVar.Value;
                }
            }
        }

        // Add common environment variables
        envVars["ENVIRONMENT"] = context.Environment.Name;
        envVars["APPLICATION"] = context.Application.Name;
        envVars["CONTAINER_NAME"] = containerName;

        return envVars;
    }

    /// <summary>
    /// Check if container is a cron job container
    /// </summary>
    private bool IsCronJobContainer(ContainerDefinitionConfig containerConfig, string containerName)
    {
        // Current configuration doesn't support scheduled jobs
        return false;
    }

    /// <summary>
    /// Get health check path for container
    /// </summary>
    private string GetHealthCheckPath(ContainerDefinitionConfig? containerConfig)
    {
        // Current configuration doesn't have Path property
        // Default health check paths based on common web applications
        return "/health";
    }

    /// <summary>
    /// Generate port mapping name
    /// </summary>
    private string GeneratePortMappingName(string containerName, int containerPort, string protocol)
    {
        return $"{containerName}-{containerPort}-{protocol}";
    }

    /// <summary>
    /// Get protocol from string
    /// </summary>
    private Amazon.CDK.AWS.ECS.Protocol GetProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return Protocol.TCP;
        }

        return protocol.ToLower() switch
        {
            "tcp" => Protocol.TCP,
            "udp" => Protocol.UDP,
            _ => Protocol.TCP
        };
    }

    /// <summary>
    /// Get app protocol from string
    /// </summary>
    private Amazon.CDK.AWS.ECS.AppProtocol? GetAppProtocol(string? appProtocol)
    {
        if (string.IsNullOrWhiteSpace(appProtocol))
        {
            return null;
        }

        return appProtocol.ToLower() switch
        {
            "http" => AppProtocol.Http,
            "http2" => AppProtocol.Http2,
            "grpc" => AppProtocol.Grpc,
            _ => null
        };
    }

    /// <summary>
    /// Get container port from configuration
    /// </summary>
    private int? GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings == null || containerConfig.PortMappings.Count == 0)
        {
            return null;
        }

        // Return the first container port for load balancer attachment
        return containerConfig.PortMappings.First().ContainerPort;
    }

    /// <summary>
    /// Get log retention based on account type
    /// </summary>
    private RetentionDays GetLogRetention(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => RetentionDays.ONE_YEAR,
            AccountType.NonProduction => RetentionDays.THREE_MONTHS,
            AccountType.Sandbox => RetentionDays.ONE_MONTH,
            _ => RetentionDays.ONE_MONTH
        };
    }
}

/// <summary>
/// Information about a container for load balancer attachment
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
