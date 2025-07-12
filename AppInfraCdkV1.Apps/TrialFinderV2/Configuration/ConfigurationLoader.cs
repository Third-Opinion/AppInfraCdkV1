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
    public EcsConfiguration LoadEcsConfig(string environmentName)
    {
        var configPath = Path.Combine(_configDirectory, $"{environmentName.ToLowerInvariant()}.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found for environment '{environmentName}': {configPath}");
        }
        
        var jsonContent = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<EcsConfigurationWrapper>(jsonContent, GetJsonOptions());
        
        return config?.EcsConfiguration ?? throw new InvalidOperationException("Invalid ECS configuration format");
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
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        return Path.Combine(assemblyDirectory ?? ".", "config");
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
public class EcsConfigurationWrapper  
{
    public EcsConfiguration? EcsConfiguration { get; set; }
}

public class EcsConfiguration
{
    public TaskDefinitionConfig? TaskDefinition { get; set; }
}

public class TaskDefinitionConfig
{
    public List<ContainerDefinitionConfig>? ContainerDefinitions { get; set; }
}

public class ContainerDefinitionConfig
{
    public string? Name { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
}

public class EnvironmentVariable
{
    public string? Name { get; set; }
    public string? Value { get; set; }
}