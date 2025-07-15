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
        
        return config;
    }
    
    /// <summary>
    /// Validate the ECS configuration for common issues
    /// </summary>
    public void ValidateConfiguration(EcsTaskConfiguration config)
    {
        if (config.TaskDefinition == null)
        {
            throw new InvalidOperationException("TaskDefinition configuration is required");
        }
        
        if (string.IsNullOrWhiteSpace(config.TaskDefinition.TaskDefinitionName))
        {
            throw new InvalidOperationException("TaskDefinitionName is required in the configuration");
        }
        
        var containerDefinitions = config.TaskDefinition.ContainerDefinitions;
        
        // If deployTestContainer is true, validation of container definitions is skipped
        if (config.DeployTestContainer)
        {
            return;
        }
        
        if (containerDefinitions == null || containerDefinitions.Count == 0)
        {
            // This is allowed - will fallback to default container
            return;
        }
        
        var activeContainers = containerDefinitions.Where(c => c.Skip != true).ToList();
        if (activeContainers.Count == 0)
        {
            throw new InvalidOperationException("At least one container must not be skipped");
        }
        
        // Validate each active container
        foreach (var container in activeContainers)
        {
            ValidateContainerDefinition(container);
        }
        
        // Check for duplicate container names
        var containerNames = activeContainers
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
            throw new InvalidOperationException($"Duplicate container names found: {string.Join(", ", duplicateNames)}");
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
            }
        }
        
        // Validate health check
        if (container.HealthCheck != null)
        {
            if (container.HealthCheck.Command == null || container.HealthCheck.Command.Count == 0)
            {
                throw new InvalidOperationException($"Container '{container.Name}' health check requires a command");
            }
            
            if (container.HealthCheck.Interval < 5 || container.HealthCheck.Interval > 300)
            {
                throw new InvalidOperationException($"Container '{container.Name}' health check interval must be between 5 and 300 seconds");
            }
            
            if (container.HealthCheck.Timeout < 2 || container.HealthCheck.Timeout > 60)
            {
                throw new InvalidOperationException($"Container '{container.Name}' health check timeout must be between 2 and 60 seconds");
            }
            
            if (container.HealthCheck.Retries < 1 || container.HealthCheck.Retries > 10)
            {
                throw new InvalidOperationException($"Container '{container.Name}' health check retries must be between 1 and 10");
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
    public string? VpcNamePattern { get; set; }
    public EcsTaskConfiguration? EcsConfiguration { get; set; }
}

public class EcsTaskConfiguration
{
    public TaskDefinitionConfig? TaskDefinition { get; set; }
    public bool DeployTestContainer { get; set; } = false;
}

public class TaskDefinitionConfig
{
    public string? TaskDefinitionName { get; set; }
    public List<ContainerDefinitionConfig>? ContainerDefinitions { get; set; }
}

public class ContainerDefinitionConfig
{
    public string? Name { get; set; }
    public string? Image { get; set; }
    public int? Cpu { get; set; }
    public bool? Skip { get; set; }
    public bool? Essential { get; set; }
    public List<PortMapping>? PortMappings { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
    public HealthCheckConfig? HealthCheck { get; set; }
}

public class PortMapping
{
    public string? Name { get; set; }
    public int? ContainerPort { get; set; }
    public int? HostPort { get; set; }
    public string? Protocol { get; set; }
    public string? AppProtocol { get; set; }
}

public class HealthCheckConfig
{
    public List<string>? Command { get; set; }
    public int? Interval { get; set; }
    public int? Timeout { get; set; }
    public int? Retries { get; set; }
    public int? StartPeriod { get; set; }
}

public class EnvironmentVariable
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}