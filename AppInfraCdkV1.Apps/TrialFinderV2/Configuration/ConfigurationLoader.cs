using System.Text.Json;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Configuration;

/// <summary>
/// Loads and manages JSON configuration files for TrialFinderV2 infrastructure
/// </summary>
public class ConfigurationLoader
{
    private readonly string _configDirectory;
    
    public ConfigurationLoader(string? configDirectory = null)
    {
        _configDirectory = configDirectory ?? GetDefaultConfigDirectory();
    }
    
    /// <summary>
    /// Load environment-specific ECS configuration from JSON file
    /// </summary>
    public EcsTaskConfiguration LoadEcsConfig(string environmentName)
    {
        var config = LoadFullConfig(environmentName);
        return config.EcsConfiguration ?? throw new InvalidOperationException("Invalid ECS configuration format");
    }
    
    /// <summary>
    /// Load complete configuration including VPC name pattern
    /// </summary>
    public EcsTaskConfigurationWrapper LoadFullConfig(string environmentName)
    {
        var configPath = Path.Combine(_configDirectory, $"{environmentName.ToLowerInvariant()}.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found for environment '{environmentName}': {configPath}");
        }
        
        var jsonContent = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<EcsTaskConfigurationWrapper>(jsonContent, GetJsonOptions());
        
        if (config?.EcsConfiguration == null)
        {
            throw new InvalidOperationException("Invalid ECS configuration format");
        }
        
        // Validate the ECS configuration
        ValidateConfiguration(config.EcsConfiguration);
        
        // Validate base stack configuration
        ValidateBaseStackConfiguration(config);
        
        return config;
    }
    
    /// <summary>
    /// Validate the ECS configuration for common issues
    /// </summary>
    public void ValidateConfiguration(EcsTaskConfiguration config)
    {
        if (config.TaskDefinition == null || config.TaskDefinition.Count == 0)
        {
            throw new InvalidOperationException("At least one TaskDefinition configuration is required");
        }
        
        
        foreach (var taskDef in config.TaskDefinition)
        {
            if (string.IsNullOrWhiteSpace(taskDef.TaskDefinitionName))
            {
                throw new InvalidOperationException("TaskDefinitionName is required in the configuration");
            }
            
            var containerDefinitions = taskDef.ContainerDefinitions;
            
            if (containerDefinitions == null || containerDefinitions.Count == 0)
            {
                // This is allowed - will fallback to default container
                continue;
            }
            
            // Validate each container
            foreach (var container in containerDefinitions)
            {
                ValidateContainerDefinition(container);
            }
            
            // Check for duplicate container names within this task definition
            var containerNames = containerDefinitions
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name!)
                .ToList();
                
            var duplicateNames = containerNames
                .GroupBy(name => name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
                
            if (duplicateNames.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate container names found in task '{taskDef.TaskDefinitionName}': {string.Join(", ", duplicateNames)}");
            }
        }
    }
    
    /// <summary>
    /// Validate the base stack configuration requirements
    /// </summary>
    public void ValidateBaseStackConfiguration(EcsTaskConfigurationWrapper config)
    {
        if (config.UseDedicatedBaseStack)
        {
            if (string.IsNullOrWhiteSpace(config.BaseStackType))
            {
                throw new InvalidOperationException("BaseStackType is required when UseDedicatedBaseStack is true");
            }
            
            var validBaseStackTypes = new[] { "TrialMatch", "TrialFinderV2" };
            if (!validBaseStackTypes.Contains(config.BaseStackType))
            {
                throw new InvalidOperationException($"Invalid BaseStackType '{config.BaseStackType}'. Must be one of: {string.Join(", ", validBaseStackTypes)}");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(config.BaseStackType))
            {
                throw new InvalidOperationException("BaseStackType should not be specified when UseDedicatedBaseStack is false");
            }
        }
    }
    
    /// <summary>
    /// Validate individual container definition
    /// </summary>
    private void ValidateContainerDefinition(ContainerDefinitionConfig container)
    {
        if (string.IsNullOrWhiteSpace(container.Name))
        {
            throw new InvalidOperationException("Container name is required");
        }
        
        if (string.IsNullOrWhiteSpace(container.Image))
        {
            throw new InvalidOperationException($"Container '{container.Name}' requires an image");
        }
        
        // Allow "placeholder" as a valid image value for development/testing
        if (container.Image.Equals("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            // This is valid - placeholder images are used for development/testing
        }
        
        // Validate repository configuration if present
        if (container.Repository != null)
        {
            ValidateRepositoryConfiguration(container.Repository, container.Name);
        }
        
        // Validate port mappings
        if (container.PortMappings?.Count > 0)
        {
            foreach (var portMapping in container.PortMappings)
            {
                if (portMapping.ContainerPort == null || portMapping.ContainerPort <= 0)
                {
                    throw new InvalidOperationException($"Container '{container.Name}' has invalid port mapping: ContainerPort must be a positive integer");
                }
                
                if (portMapping.ContainerPort > 65535)
                {
                    throw new InvalidOperationException($"Container '{container.Name}' has invalid port mapping: ContainerPort must be <= 65535");
                }
                
                if (string.IsNullOrWhiteSpace(portMapping.Protocol))
                {
                    throw new InvalidOperationException($"Container '{container.Name}' has invalid port mapping: Protocol is required");
                }
                
                var validProtocols = new[] { "tcp", "udp", "TCP", "UDP" };
                if (!validProtocols.Contains(portMapping.Protocol))
                {
                    throw new InvalidOperationException($"Container '{container.Name}' has invalid port mapping: Protocol must be 'tcp' or 'udp', got '{portMapping.Protocol}'");
                }
            }
        }
        
    }

    /// <summary>
    /// Validate repository configuration
    /// </summary>
    private void ValidateRepositoryConfiguration(ContainerRepositoryConfig repository, string containerName)
    {
        if (string.IsNullOrWhiteSpace(repository.Type))
        {
            throw new InvalidOperationException($"Container '{containerName}' repository configuration requires a type");
        }
        
        if (string.IsNullOrWhiteSpace(repository.Description))
        {
            throw new InvalidOperationException($"Container '{containerName}' repository configuration requires a description");
        }
        
        // Validate removal policy if specified
        if (!string.IsNullOrWhiteSpace(repository.RemovalPolicy))
        {
            var validRemovalPolicies = new[] { "RETAIN", "DESTROY", "SNAPSHOT" };
            if (!validRemovalPolicies.Contains(repository.RemovalPolicy))
            {
                throw new InvalidOperationException($"Container '{containerName}' repository has invalid removal policy: '{repository.RemovalPolicy}'. Must be one of: {string.Join(", ", validRemovalPolicies)}");
            }
        }
    }
    
    /// <summary>
    /// Substitute variables in configuration with actual values
    /// </summary>
    public T SubstituteVariables<T>(T config, DeploymentContext context) where T : class
    {
        var jsonString = JsonSerializer.Serialize(config, GetJsonOptions());
        
        // Replace common variables
        jsonString = jsonString.Replace("${ENVIRONMENT}", context.Environment.Name);
        jsonString = jsonString.Replace("${ACCOUNT_TYPE}", context.Environment.AccountType.ToString());
        jsonString = jsonString.Replace("${APP_VERSION}", context.Application.Version);
        jsonString = jsonString.Replace("${AWS_REGION}", context.Environment.Region);
        jsonString = jsonString.Replace("${SERVICE_NAME}", context.Namer.EcsService(Core.Enums.ResourcePurpose.Web));
        jsonString = jsonString.Replace("${TASK_DEFINITION_FAMILY}", context.Namer.EcsTaskDefinition(Core.Enums.ResourcePurpose.Web));
        jsonString = jsonString.Replace("${LOG_GROUP_NAME}", context.Namer.LogGroup("trial-finder", Core.Enums.ResourcePurpose.Web));
        
        return JsonSerializer.Deserialize<T>(jsonString, GetJsonOptions()) 
               ?? throw new InvalidOperationException("Failed to deserialize substituted configuration");
    }
    
    private static string GetDefaultConfigDirectory()
    {
        // Get the directory where this assembly is located
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        
        // Navigate to the TrialFinderV2 config directory from the assembly location
        // This handles both development (bin/Debug/net8.0) and deployment scenarios
        var currentDir = assemblyDirectory ?? Directory.GetCurrentDirectory();
        
        // Look for config directory in common locations, prioritizing application-specific paths
        var possiblePaths = new[]
        {
            Path.Combine(currentDir, "TrialFinderV2", "config"),
            Path.Combine(currentDir, "AppInfraCdkV1.Apps", "TrialFinderV2", "config"),
            Path.Combine(currentDir, "..", "..", "..", "AppInfraCdkV1.Apps", "TrialFinderV2", "config"),
            Path.Combine(Directory.GetCurrentDirectory(), "AppInfraCdkV1.Apps", "TrialFinderV2", "config")
        };
        
        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        
        // Default fallback - ensure we use application-specific config directory
        return Path.Combine(currentDir, "TrialFinderV2", "config");
    }
    
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }
}

// Configuration wrapper and data classes for JSON deserialization
public class EcsTaskConfigurationWrapper  
{
    public bool UseDedicatedBaseStack { get; set; } = false;
    public string? BaseStackType { get; set; }
    public EcsTaskConfiguration? EcsConfiguration { get; set; }
}

public class EcsTaskConfiguration
{
    public string ServiceName { get; set; } = string.Empty;
    public List<TaskDefinitionConfig> TaskDefinition { get; set; } = new();
}

public class TaskDefinitionConfig
{
    public string TaskDefinitionName { get; set; } = string.Empty;
    public int? Cpu { get; set; }
    public int? Memory { get; set; }
    public List<ContainerDefinitionConfig>? ContainerDefinitions { get; set; }
}

public class ContainerRepositoryConfig
{
    public string? Type { get; set; }
    public string? Description { get; set; }
    public bool? ImageScanOnPush { get; set; }
    public string? RemovalPolicy { get; set; }
}

public class ContainerDefinitionConfig
{
    public string? Name { get; set; }
    public string? Image { get; set; }
    public ContainerRepositoryConfig? Repository { get; set; }
    public int? Cpu { get; set; }
    public bool? Essential { get; set; }
    public List<PortMapping>? PortMappings { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
    public List<string>? Secrets { get; set; }
    public HealthCheckConfig? HealthCheck { get; set; }
    public bool? DisableHealthCheck { get; set; }
}

public class PortMapping
{
    public int? ContainerPort { get; set; }
    public int? HostPort { get; set; }
    public string? Protocol { get; set; }
    public string? AppProtocol { get; set; }
}

public class EnvironmentVariable
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}

public class HealthCheckConfig
{
    public List<string>? Command { get; set; }
    public int? Interval { get; set; }
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
    public int? StartPeriod { get; set; }
    public bool? Disabled { get; set; }
}