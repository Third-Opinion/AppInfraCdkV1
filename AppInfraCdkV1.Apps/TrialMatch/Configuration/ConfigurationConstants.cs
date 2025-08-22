namespace AppInfraCdkV1.Apps.TrialMatch.Configuration;

/// <summary>
/// Configuration constants for the TrialMatch application
/// This file centralizes magic numbers and common values used throughout the codebase
/// </summary>
public static class ConfigurationConstants
{
    /// <summary>
    /// Default port numbers for services
    /// </summary>
    public static class Ports
    {
        /// <summary>
        /// Default HTTP port for web applications
        /// </summary>
        public const int HttpPort = 80;
        
        /// <summary>
        /// Default HTTPS port for web applications
        /// </summary>
        public const int HttpsPort = 443;
        
        /// <summary>
        /// Default API port for backend services
        /// </summary>
        public const int ApiPort = 8080;
        
        /// <summary>
        /// Default health check port
        /// </summary>
        public const int HealthCheckPort = 8080;
    }
    
    /// <summary>
    /// Timeout values in seconds
    /// </summary>
    public static class Timeouts
    {
        /// <summary>
        /// Default health check interval in seconds
        /// </summary>
        public const int HealthCheckInterval = 30;
        
        /// <summary>
        /// Default health check timeout in seconds
        /// </summary>
        public const int HealthCheckTimeout = 5;
        
        /// <summary>
        /// Default health check retry count
        /// </summary>
        public const int HealthCheckRetries = 3;
        
        /// <summary>
        /// Default health check start period in seconds
        /// </summary>
        public const int HealthCheckStartPeriod = 60;
        
        /// <summary>
        /// Default session duration for IAM roles in hours
        /// </summary>
        public const int DefaultSessionDurationHours = 1;
    }
    
    /// <summary>
    /// Resource sizing constants
    /// </summary>
    public static class ResourceSizing
    {
        /// <summary>
        /// Default CPU units for ECS tasks (0 = unlimited)
        /// </summary>
        public const int DefaultCpuUnits = 0;
        
        /// <summary>
        /// Default memory in MiB for ECS tasks
        /// </summary>
        public const int DefaultMemoryMiB = 512;
        
        /// <summary>
        /// Default log retention days
        /// </summary>
        public const int DefaultLogRetentionDays = 30;
    }
    
    /// <summary>
    /// Secret generation constants
    /// </summary>
    public static class SecretGeneration
    {
        /// <summary>
        /// Default password length for generated secrets
        /// </summary>
        public const int DefaultPasswordLength = 32;
        
        /// <summary>
        /// Characters to exclude from generated passwords
        /// </summary>
        public const string ExcludedCharacters = "\"@/\\";
        
        /// <summary>
        /// Default secret template for basic secrets
        /// </summary>
        public const string DefaultSecretTemplate = "{{\"secretName\":\"{0}\",\"managedBy\":\"CDK\",\"environment\":\"{1}\"}}";
    }
    
    /// <summary>
    /// Logging constants
    /// </summary>
    public static class Logging
    {
        /// <summary>
        /// Default log driver for ECS containers
        /// </summary>
        public const string DefaultLogDriver = "awslogs";
        
        /// <summary>
        /// Default log mode for ECS containers
        /// </summary>
        public const string DefaultLogMode = "non-blocking";
        
        /// <summary>
        /// Default log buffer size
        /// </summary>
        public const string DefaultLogBufferSize = "25m";
        
        /// <summary>
        /// Default log stream prefix
        /// </summary>
        public const string DefaultLogStreamPrefix = "ecs";
    }
    
    /// <summary>
    /// Health check commands
    /// </summary>
    public static class HealthCheckCommands
    {
        /// <summary>
        /// Default health check command for web applications
        /// </summary>
        public static readonly string[] DefaultWebHealthCheck = { "CMD-SHELL", "/bin/sh -c 'wget -q -O - http://localhost:80/ || exit 1'" };
        
        /// <summary>
        /// Default health check command for API services
        /// </summary>
        public static readonly string[] DefaultApiHealthCheck = { "CMD-SHELL", "/bin/sh -c 'wget -q -O - http://localhost:8080/health || exit 1'" };
    }
}
