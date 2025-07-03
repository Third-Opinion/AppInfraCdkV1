namespace AppInfraCdkV1.Core.Models;

public class SharedResourcesConfig
{
    private List<string> _sharedAcrossEnvironments = new();
    private List<string> _sharedAcrossAccounts = new();
    private VpcSharingConfig _vpcSharing = new();
    private DatabaseSharingConfig _databaseSharing = new();

    /// <summary>
    ///     Resources that are shared across environments in the same account
    /// </summary>
    public List<string> SharedAcrossEnvironments
    {
        get => _sharedAcrossEnvironments;
        set => _sharedAcrossEnvironments = value;
    }

    /// <summary>
    ///     Resources that are shared across all accounts
    /// </summary>
    public List<string> SharedAcrossAccounts
    {
        get => _sharedAcrossAccounts;
        set => _sharedAcrossAccounts = value;
    }

    /// <summary>
    ///     VPC sharing configuration
    /// </summary>
    public VpcSharingConfig VpcSharing
    {
        get => _vpcSharing;
        set => _vpcSharing = value;
    }

    /// <summary>
    ///     Database sharing configuration
    /// </summary>
    public DatabaseSharingConfig DatabaseSharing
    {
        get => _databaseSharing;
        set => _databaseSharing = value;
    }
}