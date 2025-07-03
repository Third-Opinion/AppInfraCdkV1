namespace AppInfraCdkV1.Core.Models;

public class VpcSharingConfig
{
    /// <summary>
    ///     Whether VPC is shared across environments
    /// </summary>
    public bool IsShared { get; set; } = false;

    /// <summary>
    ///     Shared VPC ID if using existing VPC
    /// </summary>
    public string? SharedVpcId { get; set; }

    /// <summary>
    ///     Environments that share this VPC
    /// </summary>
    public List<string> SharingEnvironments { get; set; } = new();
}