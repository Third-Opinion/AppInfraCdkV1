namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentSecurityConfig
{
    /// <summary>
    ///     Whether cross-environment access is allowed
    /// </summary>
    public bool AllowCrossEnvironmentAccess { get; set; } = false;

    /// <summary>
    ///     Whether encryption in transit is required
    /// </summary>
    public bool RequireEncryptionInTransit { get; set; } = true;

    /// <summary>
    ///     Whether encryption at rest is required
    /// </summary>
    public bool RequireEncryptionAtRest { get; set; } = true;

    /// <summary>
    ///     Allowed cross-environment access patterns
    /// </summary>
    public List<CrossEnvironmentAccessRule> AccessRules { get; set; } = new();
}