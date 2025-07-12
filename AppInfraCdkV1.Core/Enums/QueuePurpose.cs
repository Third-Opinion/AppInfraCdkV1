namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// SQS queue purposes for message processing
/// </summary>
public enum QueuePurpose
{
    /// <summary>
    /// Main processing queue
    /// </summary>
    Processing,
    
    /// <summary>
    /// Dead letter queue for failed messages
    /// </summary>
    DeadLetter,
    
    /// <summary>
    /// High priority/urgent processing queue
    /// </summary>
    Urgent
}