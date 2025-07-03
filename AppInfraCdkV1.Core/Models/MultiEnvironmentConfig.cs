namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Configuration for applications deployed across multiple environments
/// </summary>
public class MultiEnvironmentConfig
{
    /// <summary>
    ///     Whether this application supports multi-environment deployment
    /// </summary>
    public bool SupportsMultiEnvironmentDeployment { get; set; } = true;

    /// <summary>
    ///     Environment-specific overrides
    /// </summary>
    public Dictionary<string, EnvironmentOverride> EnvironmentOverrides { get; set; } = new();

    /// <summary>
    ///     Shared resources configuration
    /// </summary>
    public SharedResourcesConfig SharedResources { get; set; } = new();

    /// <summary>
    ///     Gets the effective configuration for a specific environment
    /// </summary>
    public EnvironmentOverride GetEffectiveConfigForEnvironment(string environmentName)
    {
        return EnvironmentOverrides.GetValueOrDefault(environmentName, new EnvironmentOverride());
    }
}