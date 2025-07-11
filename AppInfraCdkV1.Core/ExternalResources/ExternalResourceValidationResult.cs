namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Result of validating an external resource
/// </summary>
public class ExternalResourceValidationResult
{
    /// <summary>
    /// Whether the resource passed all validations
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Whether the resource exists in AWS
    /// </summary>
    public bool Exists { get; set; }
    
    /// <summary>
    /// Whether the resource name follows naming conventions
    /// </summary>
    public bool FollowsNamingConvention { get; set; }
    
    /// <summary>
    /// Whether the resource has required permissions/configuration
    /// </summary>
    public bool HasRequiredPermissions { get; set; }
    
    /// <summary>
    /// List of validation errors
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// List of validation warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Additional metadata about the resource
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Timestamp when validation was performed
    /// </summary>
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}