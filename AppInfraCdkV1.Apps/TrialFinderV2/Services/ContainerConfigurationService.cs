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
    private readonly SecretManager _secretManager;

    public ContainerConfigurationService(Construct scope, string id, DeploymentContext context, SecretManager secretManager) : base(scope, id)
    {
        _context = context;
        _secretManager = secretManager;
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

        // Collect all secret names from all containers first and build mapping once
        Console.WriteLine("🔐 Collecting all secret names and building mapping...");
        CollectAllSecretsAndBuildMapping(taskDefConfig, context);

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
    /// Collect all secret names from all containers and build mapping once
    /// </summary>
    private void CollectAllSecretsAndBuildMapping(TaskDefinitionConfig? taskDefConfig, DeploymentContext context)
    {
        var allSecretNames = new HashSet<string>();
        
        // Collect secrets from container definitions
        var containerDefinitions = taskDefConfig?.ContainerDefinitions;
        if (containerDefinitions != null)
        {
            foreach (var containerConfig in containerDefinitions)
            {
                if (containerConfig.Secrets != null)
                {
                    foreach (var secretName in containerConfig.Secrets)
                    {
                        allSecretNames.Add(secretName);
                    }
                }
            }
        }
        
        // Add any hardcoded secrets (like test-secret)
        allSecretNames.Add("test-secret");
        
        // Build mapping for all collected secrets
        Console.WriteLine($"   Found {allSecretNames.Count} unique secret(s) across all containers");
        _secretManager.BuildSecretNameMapping(allSecretNames.ToList());
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

        // Get container secrets from SecretManager
        var containerSecrets = GetContainerSecrets(containerConfig.Secrets, cognitoOutputs, context);

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
            Secrets = containerSecrets,
            HealthCheck = GetContainerHealthCheck(containerConfig, containerName),
            PortMappings = GetPortMappings(containerConfig, containerName)
        });

        // Log secrets configuration
        if (containerConfig.Secrets != null && containerConfig.Secrets.Count > 0)
        {
            Console.WriteLine($"  🔐 Container '{containerName}' has {containerConfig.Secrets.Count} secrets configured");
            foreach (var secretName in containerConfig.Secrets)
            {
                Console.WriteLine($"     - {secretName}");
            }
        }
    }

    /// <summary>
    /// Get container secrets from SecretManager
    /// </summary>
    private Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();
        
        if (secretNames?.Count > 0)
        {
            Console.WriteLine($"     🔐 Processing {secretNames.Count} secret(s):");
            foreach (var envVarName in secretNames)
            {
                // Use the mapping to get the secret name, or fall back to the original name
                var secretName = _secretManager.GetSecretNameFromEnvVar(envVarName);
                
                var fullSecretName = _secretManager.BuildSecretName(secretName, context);
                var secret = _secretManager.GetOrCreateSecret(secretName, fullSecretName, cognitoOutputs, context);
                
                // Use the original environment variable name from the configuration
                secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
                
                Console.WriteLine($"        - Environment variable '{envVarName}' -> Secret '{secretName}'");
                Console.WriteLine($"          Full secret path: {secret.SecretArn}");
            }
        }
        
        return secrets;
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
        
        // Use the command from configuration if provided, otherwise fall back to default
        string[] command;
        if (healthCheck.Command?.Count > 0)
        {
            // Use the exact command from configuration
            command = healthCheck.Command.ToArray();
        }
        else
        {
            // Fall back to default health check using curl
            var healthCheckPath = GetHealthCheckPath(containerConfig);
            if (string.IsNullOrWhiteSpace(healthCheckPath))
            {
                return null;
            }
            command = new[] { "CMD-SHELL", $"curl -f {healthCheckPath} || exit 1" };
        }

        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = command,
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

        // Start with default environment variables (like in legacy version)
        var defaults = CreateDefaultEnvironmentVariables(context);
        foreach (var kvp in defaults)
        {
            envVars[kvp.Key] = kvp.Value;
        }

        // Add container-specific defaults
        var containerDefaults = GetContainerSpecificEnvironmentDefaults(containerName, context);
        foreach (var kvp in containerDefaults)
        {
            envVars[kvp.Key] = kvp.Value;
        }

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
    /// Create default environment variables (like in legacy version)
    /// </summary>
    private Dictionary<string, string> CreateDefaultEnvironmentVariables(DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = "1.0.0", // Static version to prevent unnecessary redeployments
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/health",
            ["AWS_REGION"] = context.Environment.Region,
            ["AWS_ACCOUNT_ID"] = context.Environment.AccountId
        };
    }

    /// <summary>
    /// Get container-specific default environment variables (like in legacy version)
    /// </summary>
    private Dictionary<string, string> GetContainerSpecificEnvironmentDefaults(string containerName, DeploymentContext context)
    {
        // Add ASPNETCORE_ENVIRONMENT for all containers (assuming they are .NET applications)
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name)
        };
    }

    /// <summary>
    /// Map deployment environment to ASP.NET Core environment (like in legacy version)
    /// </summary>
    private string GetAspNetCoreEnvironment(string environmentName)
    {
        return environmentName.ToLowerInvariant() switch
        {
            "development" => "Development",
            "staging" => "Staging",
            "production" => "Production",
            "integration" => "Integration",
            _ => "Development"
        };
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
