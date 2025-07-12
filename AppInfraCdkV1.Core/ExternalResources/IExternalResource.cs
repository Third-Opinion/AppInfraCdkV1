namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Base interface for external resources that CDK depends on but doesn't create
/// </summary>
public interface IExternalResource
{
    /// <summary>
    /// The AWS resource ARN
    /// </summary>
    string Arn { get; }
    
    /// <summary>
    /// The resource name following naming conventions
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// The type of external resource
    /// </summary>
    ExternalResourceType ResourceType { get; }
    
    /// <summary>
    /// Whether the resource exists in AWS
    /// </summary>
    bool Exists { get; }
    
    /// <summary>
    /// Validation results for the resource
    /// </summary>
    ExternalResourceValidationResult ValidationResult { get; }
}