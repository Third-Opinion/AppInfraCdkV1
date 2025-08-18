namespace AppInfraCdkV1.Core.Models;

/// <summary>
/// Configuration for ECS scheduled tasks
/// </summary>
public class ScheduledJobConfig
{
    /// <summary>
    /// Cron expression for scheduling (e.g., "cron(0 */6 * * ? *)" for every 6 hours)
    /// </summary>
    public string ScheduleExpression { get; set; } = string.Empty;
    
    /// <summary>
    /// Maximum execution time in seconds (default: 1 hour)
    /// </summary>
    public int JobTimeout { get; set; } = 3600;
    
    /// <summary>
    /// Retry policy configuration
    /// </summary>
    public RetryPolicyConfig? RetryPolicy { get; set; }
    
    /// <summary>
    /// Dead letter queue configuration for failed executions
    /// </summary>
    public DeadLetterQueueConfig? DeadLetterQueue { get; set; }
    
    /// <summary>
    /// Whether to enable automatic retries on failure
    /// </summary>
    public bool EnableRetries { get; set; } = true;
    
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Delay between retry attempts in seconds
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 300; // 5 minutes
}

/// <summary>
/// Configuration for retry policies
/// </summary>
public class RetryPolicyConfig
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Delay between retry attempts in seconds
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 300; // 5 minutes
    
    /// <summary>
    /// Whether to use exponential backoff for retry delays
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
    
    /// <summary>
    /// Maximum retry delay in seconds
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 3600; // 1 hour
}

/// <summary>
/// Configuration for dead letter queue
/// </summary>
public class DeadLetterQueueConfig
{
    /// <summary>
    /// ARN of the SQS queue to use as dead letter queue
    /// </summary>
    public string? QueueArn { get; set; }
    
    /// <summary>
    /// Maximum number of failed executions before sending to DLQ
    /// </summary>
    public int MaxReceiveCount { get; set; } = 3;
    
    /// <summary>
    /// Whether to enable dead letter queue functionality
    /// </summary>
    public bool Enabled { get; set; } = false;
}
