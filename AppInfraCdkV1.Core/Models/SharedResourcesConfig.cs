namespace AppInfraCdkV1.Core.Models;

public class SharedResourcesConfig
{
    /// <summary>
    ///     Resources that are shared across environments in the same account
    /// </summary>
    public List<string> SharedAcrossEnvironments { get; set; } = new();

    /// <summary>
    ///     Resources that are shared across all accounts
    /// </summary>
    public List<string> SharedAcrossAccounts { get; set; } = new();

    /// <summary>
    ///     VPC sharing configuration
    /// </summary>
    public VpcSharingConfig VpcSharing { get; set; } = new();

    /// <summary>
    ///     Database sharing configuration
    /// </summary>
    public DatabaseSharingConfig DatabaseSharing { get; set; } = new();
}