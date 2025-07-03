namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Defines how environments are isolated within the same AWS account
/// </summary>
public class EnvironmentIsolationStrategy
{
    private bool _useVpcPerEnvironment = true;
    private bool _useSharedVpcWithSubnets = false;
    private string? _sharedVpcId;
    private VpcCidrConfig _vpcCidr = new();
    private bool _useEnvironmentSpecificIamRoles = true;
    private bool _useEnvironmentSpecificKmsKeys = true;
    private CrossEnvironmentAccess _crossEnvironmentAccess = new();

    /// <summary>
    ///     Whether each environment gets its own VPC (recommended for production accounts)
    /// </summary>
    public bool UseVpcPerEnvironment
    {
        get => _useVpcPerEnvironment;
        set => _useVpcPerEnvironment = value;
    }

    /// <summary>
    ///     Whether to use shared VPC with subnet-level isolation
    /// </summary>
    public bool UseSharedVpcWithSubnets
    {
        get => _useSharedVpcWithSubnets;
        set => _useSharedVpcWithSubnets = value;
    }

    /// <summary>
    ///     Shared VPC ID if using shared VPC strategy
    /// </summary>
    public string? SharedVpcId
    {
        get => _sharedVpcId;
        set => _sharedVpcId = value;
    }

    /// <summary>
    ///     Subnet CIDR blocks for this environment when using VPC per environment
    /// </summary>
    public VpcCidrConfig VpcCidr
    {
        get => _vpcCidr;
        set => _vpcCidr = value;
    }

    /// <summary>
    ///     Whether to use separate IAM roles per environment
    /// </summary>
    public bool UseEnvironmentSpecificIamRoles
    {
        get => _useEnvironmentSpecificIamRoles;
        set => _useEnvironmentSpecificIamRoles = value;
    }

    /// <summary>
    ///     Whether to use separate KMS keys per environment
    /// </summary>
    public bool UseEnvironmentSpecificKmsKeys
    {
        get => _useEnvironmentSpecificKmsKeys;
        set => _useEnvironmentSpecificKmsKeys = value;
    }

    /// <summary>
    ///     Cross-environment access rules
    /// </summary>
    public CrossEnvironmentAccess CrossEnvironmentAccess
    {
        get => _crossEnvironmentAccess;
        set => _crossEnvironmentAccess = value;
    }
}