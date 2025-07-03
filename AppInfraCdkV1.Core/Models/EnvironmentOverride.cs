namespace AppInfraCdkV1.Core.Models;

public class EnvironmentOverride
{
    /// <summary>
    ///     Resource sizing overrides for this environment
    /// </summary>
    public ResourceSizing? SizingOverride { get; set; }

    /// <summary>
    ///     Security configuration overrides
    /// </summary>
    public SecurityConfig? SecurityOverride { get; set; }

    /// <summary>
    ///     Feature flags for this environment
    /// </summary>
    public Dictionary<string, bool> FeatureFlags { get; set; } = new();

    /// <summary>
    ///     Environment-specific settings
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();

    /// <summary>
    ///     Whether to enable enhanced monitoring for this environment
    /// </summary>
    public bool EnableEnhancedMonitoring { get; set; } = false;

    /// <summary>
    ///     Backup retention period in days
    /// </summary>
    public int BackupRetentionDays { get; set; } = 7;
}