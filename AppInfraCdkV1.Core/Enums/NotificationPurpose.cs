namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// SNS topic purposes for notifications and messaging
/// </summary>
public enum NotificationPurpose
{
    /// <summary>
    /// General notifications
    /// </summary>
    General,
    
    /// <summary>
    /// Clinical trial updates and changes
    /// </summary>
    TrialUpdates,
    
    /// <summary>
    /// System alerts and monitoring
    /// </summary>
    SystemAlerts,
    
    /// <summary>
    /// User-facing notifications
    /// </summary>
    UserNotifications
}