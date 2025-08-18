namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// Types of ECS tasks for different execution patterns
/// </summary>
public enum TaskType
{
    /// <summary>
    /// Web application that runs continuously and handles HTTP requests
    /// </summary>
    WebApplication,
    
    /// <summary>
    /// Scheduled job that runs on a cron schedule for batch processing
    /// </summary>
    ScheduledJob
}
