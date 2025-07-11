using AppInfraCdkV1.Core.Enums;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Defines a requirement for an external resource that must exist before CDK deployment
/// </summary>
public class ExternalResourceRequirement
{
    /// <summary>
    /// The type of external resource required
    /// </summary>
    public ExternalResourceType ResourceType { get; set; }
    
    /// <summary>
    /// The purpose of the resource (for naming convention)
    /// </summary>
    public object Purpose { get; set; } = null!;
    
    /// <summary>
    /// The expected ARN of the resource
    /// </summary>
    public string ExpectedArn { get; set; } = string.Empty;
    
    /// <summary>
    /// The expected name of the resource
    /// </summary>
    public string ExpectedName { get; set; } = string.Empty;
    
    /// <summary>
    /// Validation rules that must pass for this resource
    /// </summary>
    public List<string> ValidationRules { get; set; } = new();
    
    /// <summary>
    /// Whether this resource is required for deployment
    /// </summary>
    public bool IsRequired { get; set; } = true;
    
    /// <summary>
    /// Description of what this resource is used for
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Tags that should be present on the resource
    /// </summary>
    public Dictionary<string, string> ExpectedTags { get; set; } = new();
    
    /// <summary>
    /// Environment-specific requirements
    /// </summary>
    public Dictionary<string, object> EnvironmentSpecificRequirements { get; set; } = new();
}