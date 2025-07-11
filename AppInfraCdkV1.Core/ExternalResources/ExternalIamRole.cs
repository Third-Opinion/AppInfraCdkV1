using AppInfraCdkV1.Core.Enums;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Implementation of external IAM role
/// </summary>
public class ExternalIamRole : IExternalIamRole
{
    public string Arn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ExternalResourceType ResourceType => ExternalResourceType.IamRole;
    public bool Exists { get; set; }
    public ExternalResourceValidationResult ValidationResult { get; set; } = new();
    
    public IamPurpose Purpose { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public List<string> TrustedServices { get; set; } = new();
    public List<string> ManagedPolicyArns { get; set; } = new();
    public bool CanAssumeEcsTasks { get; set; }
    public bool HasEcsExecutionPolicy { get; set; }
    public bool HasS3Access { get; set; }
}