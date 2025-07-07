using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Configuration;

/// <summary>
/// Provides environment-based resource sizing configuration
/// </summary>
public static class EnvironmentSizing
{
    /// <summary>
    /// Gets the EC2 instance size based on environment
    /// </summary>
    public static string GetInstanceSize(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => "t3.large",
            EnvironmentType.Staging => "t3.medium",
            _ => "t3.small" // Development, Integration
        };
    }

    /// <summary>
    /// Gets the EC2 instance size based on environment name
    /// </summary>
    public static string GetInstanceSize(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetInstanceSize(environment);
        return "t3.small"; // Default to small for unknown environments
    }

    /// <summary>
    /// Gets the RDS database instance size based on environment
    /// </summary>
    public static string GetDatabaseSize(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => "db.t3.medium",
            EnvironmentType.Staging => "db.t3.small",
            _ => "db.t3.micro" // Development, Integration
        };
    }

    /// <summary>
    /// Gets the RDS database instance size based on environment name
    /// </summary>
    public static string GetDatabaseSize(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetDatabaseSize(environment);
        return "db.t3.micro"; // Default to micro for unknown environments
    }

    /// <summary>
    /// Gets the Lambda memory size in MB based on environment
    /// </summary>
    public static int GetLambdaMemory(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => 1024,
            EnvironmentType.Staging => 768,
            _ => 512 // Development, Integration
        };
    }

    /// <summary>
    /// Gets the Lambda memory size in MB based on environment name
    /// </summary>
    public static int GetLambdaMemory(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetLambdaMemory(environment);
        return 512; // Default to 512MB for unknown environments
    }

    /// <summary>
    /// Gets the ECS task compute size (vCPU, Memory) based on environment
    /// </summary>
    public static (double vCpu, int memoryMb) GetComputeSize(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => (2.0, 4096),
            EnvironmentType.Staging => (1.0, 2048),
            _ => (0.5, 1024) // Development, Integration
        };
    }

    /// <summary>
    /// Gets the ECS task compute size (vCPU, Memory) based on environment name
    /// </summary>
    public static (double vCpu, int memoryMb) GetComputeSize(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetComputeSize(environment);
        return (0.5, 1024); // Default to small for unknown environments
    }

    /// <summary>
    /// Gets the ElastiCache node type based on environment
    /// </summary>
    public static string GetCacheNodeType(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => "cache.t3.medium",
            EnvironmentType.Staging => "cache.t3.small",
            _ => "cache.t3.micro" // Development, Integration
        };
    }

    /// <summary>
    /// Gets the ElastiCache node type based on environment name
    /// </summary>
    public static string GetCacheNodeType(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetCacheNodeType(environment);
        return "cache.t3.micro"; // Default to micro for unknown environments
    }

    /// <summary>
    /// Gets the OpenSearch instance type based on environment
    /// </summary>
    public static string GetSearchInstanceType(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => "t3.medium.search",
            EnvironmentType.Staging => "t3.small.search",
            _ => "t3.small.search" // Development, Integration
        };
    }

    /// <summary>
    /// Gets the OpenSearch instance type based on environment name
    /// </summary>
    public static string GetSearchInstanceType(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetSearchInstanceType(environment);
        return "t3.small.search"; // Default to small for unknown environments
    }

    /// <summary>
    /// Gets the Auto Scaling configuration based on environment
    /// </summary>
    public static (int minCapacity, int maxCapacity, int desiredCapacity) GetAutoScalingConfig(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => (2, 10, 4),
            EnvironmentType.Staging => (1, 5, 2),
            _ => (1, 2, 1) // Development, Integration
        };
    }

    /// <summary>
    /// Gets the Auto Scaling configuration based on environment name
    /// </summary>
    public static (int minCapacity, int maxCapacity, int desiredCapacity) GetAutoScalingConfig(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetAutoScalingConfig(environment);
        return (1, 2, 1); // Default to minimal scaling for unknown environments
    }

    /// <summary>
    /// Gets whether high availability should be enabled based on environment
    /// </summary>
    public static bool IsHighAvailabilityEnabled(EnvironmentType environment)
    {
        return environment == EnvironmentType.Production || environment == EnvironmentType.Staging;
    }

    /// <summary>
    /// Gets whether high availability should be enabled based on environment name
    /// </summary>
    public static bool IsHighAvailabilityEnabled(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return IsHighAvailabilityEnabled(environment);
        return false; // Default to no HA for unknown environments
    }

    /// <summary>
    /// Gets the backup retention period in days based on environment
    /// </summary>
    public static int GetBackupRetentionDays(EnvironmentType environment)
    {
        return environment switch
        {
            EnvironmentType.Production => 30,
            EnvironmentType.Staging => 7,
            _ => 1 // Development, Integration
        };
    }

    /// <summary>
    /// Gets the backup retention period in days based on environment name
    /// </summary>
    public static int GetBackupRetentionDays(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environment))
            return GetBackupRetentionDays(environment);
        return 1; // Default to 1 day for unknown environments
    }
}