namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// General resource purposes for infrastructure components
/// </summary>
public enum ResourcePurpose
{
    /// <summary>
    /// Main/primary resource - default for core infrastructure
    /// </summary>
    Main,
    
    /// <summary>
    /// Web application components
    /// </summary>
    Web,
    
    /// <summary>
    /// API-specific components
    /// </summary>
    Api,
    
    /// <summary>
    /// Internal services and components
    /// </summary>
    Internal,
    
    /// <summary>
    /// Primary instance of a resource type
    /// </summary>
    Primary,
    
    /// <summary>
    /// Administrative access and components
    /// </summary>
    Admin
}