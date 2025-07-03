namespace AppInfraCdkV1.Core.Models;

public class VpcSharingConfig
{
    private bool _isShared = false;
    private string? _sharedVpcId;
    private List<string> _sharingEnvironments = new();

    /// <summary>
    ///     Whether VPC is shared across environments
    /// </summary>
    public bool IsShared
    {
        get => _isShared;
        set => _isShared = value;
    }

    /// <summary>
    ///     Shared VPC ID if using existing VPC
    /// </summary>
    public string? SharedVpcId
    {
        get => _sharedVpcId;
        set => _sharedVpcId = value;
    }

    /// <summary>
    ///     Environments that share this VPC
    /// </summary>
    public List<string> SharingEnvironments
    {
        get => _sharingEnvironments;
        set => _sharingEnvironments = value;
    }
}