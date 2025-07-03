namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Defines how environments are isolated within the same AWS account
/// </summary>
public class EnvironmentIsolationStrategy
{
    /// <summary>
    ///     Whether each environment gets its own VPC (recommended for production accounts)
    /// </summary>
    public bool UseVpcPerEnvironment { get; set; } = true;

    /// <summary>
    ///     Whether to use shared VPC with subnet-level isolation
    /// </summary>
    public bool UseSharedVpcWithSubnets { get; set; } = false;

    /// <summary>
    ///     Shared VPC ID if using shared VPC strategy
    /// </summary>
    public string? SharedVpcId { get; set; }

    /// <summary>
    ///     Subnet CIDR blocks for this environment when using VPC per environment
    /// </summary>
    public VpcCidrConfig VpcCidr { get; set; } = new();

    /// <summary>
    ///     Whether to use separate IAM roles per environment
    /// </summary>
    public bool UseEnvironmentSpecificIamRoles { get; set; } = true;

    /// <summary>
    ///     Whether to use separate KMS keys per environment
    /// </summary>
    public bool UseEnvironmentSpecificKmsKeys { get; set; } = true;

    /// <summary>
    ///     Cross-environment access rules
    /// </summary>
    public CrossEnvironmentAccess CrossEnvironmentAccess { get; set; } = new();
}