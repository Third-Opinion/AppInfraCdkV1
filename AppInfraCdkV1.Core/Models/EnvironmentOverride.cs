namespace AppInfraCdkV1.Core.Models;

public class EnvironmentOverride
{
    private ResourceSizing? _sizingOverride;
    private SecurityConfig? _securityOverride;
    private Dictionary<string, object> _settings = new();
    private bool _enableEnhancedMonitoring = false;
    private int _backupRetentionDays = 7;

    /// <summary>
    ///     Resource sizing overrides for this environment
    /// </summary>
    public ResourceSizing? SizingOverride
    {
        get => _sizingOverride;
        set => _sizingOverride = value;
    }

    /// <summary>
    ///     Security configuration overrides
    /// </summary>
    public SecurityConfig? SecurityOverride
    {
        get => _securityOverride;
        set => _securityOverride = value;
    }

    /// <summary>
    ///     Environment-specific settings
    /// </summary>
    public Dictionary<string, object> Settings
    {
        get => _settings;
        set => _settings = value;
    }

    /// <summary>
    ///     Whether to enable enhanced monitoring for this environment
    /// </summary>
    public bool EnableEnhancedMonitoring
    {
        get => _enableEnhancedMonitoring;
        set => _enableEnhancedMonitoring = value;
    }

    /// <summary>
    ///     Backup retention period in days
    /// </summary>
    public int BackupRetentionDays
    {
        get => _backupRetentionDays;
        set => _backupRetentionDays = value;
    }
}