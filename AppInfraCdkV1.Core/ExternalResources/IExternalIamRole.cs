using AppInfraCdkV1.Core.Enums;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// External IAM Role that CDK depends on but doesn't create
/// </summary>
public interface IExternalIamRole : IExternalResource
{
    /// <summary>
    /// The purpose of the IAM role
    /// </summary>
    IamPurpose Purpose { get; }
    
    /// <summary>
    /// The role name (without ARN prefix)
    /// </summary>
    string RoleName { get; }
    
    /// <summary>
    /// Services that can assume this role
    /// </summary>
    List<string> TrustedServices { get; }
    
    /// <summary>
    /// Managed policies attached to this role
    /// </summary>
    List<string> ManagedPolicyArns { get; }
    
    /// <summary>
    /// Whether the role can be assumed by ECS tasks
    /// </summary>
    bool CanAssumeEcsTasks { get; }
    
    /// <summary>
    /// Whether the role has the ECS execution policy
    /// </summary>
    bool HasEcsExecutionPolicy { get; }
    
    /// <summary>
    /// Whether the role has S3 access permissions
    /// </summary>
    bool HasS3Access { get; }
}