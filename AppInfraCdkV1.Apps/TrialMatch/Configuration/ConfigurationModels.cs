namespace AppInfraCdkV1.Apps.TrialMatch.Configuration;

// Configuration wrapper and data classes for JSON deserialization
public class EcsTaskConfigurationWrapper
{
    public string? VpcNamePattern { get; set; }
    public EcsTaskConfiguration? EcsConfiguration { get; set; }
    public FrontendEnvironmentVariables? FrontendEnvironmentVariables { get; set; }
}

public class EcsTaskConfiguration
{
    public string ServiceName { get; set; } = string.Empty;
    public List<ServiceConfig> Services { get; set; } = new();
}

public class ServiceConfig
{
    public string ServiceName { get; set; } = string.Empty;
    public List<TaskDefinitionConfig> TaskDefinition { get; set; } = new();
}

public class TaskDefinitionConfig
{
    public string TaskDefinitionName { get; set; } = string.Empty;
    public List<ContainerDefinitionConfig>? ContainerDefinitions { get; set; }
}

public class ContainerDefinitionConfig
{
    public string? Name { get; set; }
    public string? Image { get; set; }
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

/// <summary>
/// Configuration for frontend environment variables that will be stored in Secrets Manager
/// </summary>
public class FrontendEnvironmentVariables
{
    /// <summary>
    /// Cognito User Pool ID for authentication
    /// </summary>
    public string? CognitoUserPoolId { get; set; }
    
    /// <summary>
    /// Cognito Client ID for authentication
    /// </summary>
    public string? CognitoClientId { get; set; }
    
    /// <summary>
    /// Cognito Client Secret for authentication
    /// </summary>
    public string? CognitoClientSecret { get; set; }
    
    /// <summary>
    /// Cognito Domain for authentication
    /// </summary>
    public string? CognitoDomain { get; set; }
    
    /// <summary>
    /// API URL for backend communication
    /// </summary>
    public string? ApiUrl { get; set; }
    
    /// <summary>
    /// API mode (development, staging, production)
    /// </summary>
    public string? ApiMode { get; set; }
    
    /// <summary>
    /// Get the list of environment variable names that should be configured
    /// </summary>
    public static string[] GetEnvironmentVariableNames()
    {
        return new[]
        {
            "NEXT_PUBLIC_COGNITO_USER_POOL_ID",
            "NEXT_PUBLIC_COGNITO_CLIENT_ID",
            "NEXT_PUBLIC_COGNITO_CLIENT_SECRET",
            "NEXT_PUBLIC_COGNITO_DOMAIN",
            "NEXT_PUBLIC_API_URL",
            "NEXT_PUBLIC_API_MODE"
        };
    }
}
