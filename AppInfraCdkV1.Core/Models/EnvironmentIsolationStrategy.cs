namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Defines how environments are isolated within the same AWS account
/// </summary>
public class EnvironmentIsolationStrategy
{
    private VpcCidrConfig _vpcCidr = new();
    private bool _useEnvironmentSpecificIamRoles = true;
    private bool _useEnvironmentSpecificKmsKeys = true;

    /// <summary>
    ///     VPC CIDR blocks for this environment (each environment gets its own VPC)
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
}