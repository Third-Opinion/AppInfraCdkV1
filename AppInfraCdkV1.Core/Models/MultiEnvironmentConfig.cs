namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Configuration for applications deployed across multiple environments
/// </summary>
public class MultiEnvironmentConfig
{
    private bool _supportsMultiEnvironmentDeployment = true;
    private Dictionary<string, EnvironmentOverride> _environmentOverrides = new();
    private SharedResourcesConfig _sharedResources = new();

    /// <summary>
    ///     Whether this application supports multi-environment deployment
    /// </summary>
    public bool SupportsMultiEnvironmentDeployment
    {
        get => _supportsMultiEnvironmentDeployment;
        set => _supportsMultiEnvironmentDeployment = value;
    }

    /// <summary>
    ///     Environment-specific overrides
    /// </summary>
    public Dictionary<string, EnvironmentOverride> EnvironmentOverrides
    {
        get => _environmentOverrides;
        set => _environmentOverrides = value;
    }

    /// <summary>
    ///     Shared resources configuration
    /// </summary>
    public SharedResourcesConfig SharedResources
    {
        get => _sharedResources;
        set => _sharedResources = value;
    }

    /// <summary>
    ///     Gets the effective configuration for a specific environment
    /// </summary>
    public EnvironmentOverride GetEffectiveConfigForEnvironment(string environmentName)
    {
        return EnvironmentOverrides.GetValueOrDefault(environmentName, new EnvironmentOverride());
    }
}