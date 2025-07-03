namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentSecurityConfig
{
    private bool _allowCrossEnvironmentAccess;
    private bool _requireEncryptionInTransit = true;
    private bool _requireEncryptionAtRest = true;
    private List<CrossEnvironmentAccessRule> _accessRules = new();

    /// <summary>
    ///     Whether cross-environment access is allowed
    /// </summary>
    public bool AllowCrossEnvironmentAccess
    {
        get => _allowCrossEnvironmentAccess;
        set => _allowCrossEnvironmentAccess = value;
    }

    /// <summary>
    ///     Whether encryption in transit is required
    /// </summary>
    public bool RequireEncryptionInTransit
    {
        get => _requireEncryptionInTransit;
        set => _requireEncryptionInTransit = value;
    }

    /// <summary>
    ///     Whether encryption at rest is required
    /// </summary>
    public bool RequireEncryptionAtRest
    {
        get => _requireEncryptionAtRest;
        set => _requireEncryptionAtRest = value;
    }

    /// <summary>
    ///     Allowed cross-environment access patterns
    /// </summary>
    public List<CrossEnvironmentAccessRule> AccessRules
    {
        get => _accessRules;
        set => _accessRules = value;
    }
}