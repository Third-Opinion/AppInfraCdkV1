using AppInfraCdkV1.Core.Configuration;
using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

/// <summary>
/// Resource sizing configuration based on environment
/// Uses centralized EnvironmentSizing for consistent sizing across the application
/// </summary>
public class ResourceSizing
{
    public string InstanceType { get; set; } = "t3.micro";
    public int MinCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 3;
    public int DesiredCapacity { get; set; } = 1;
    public string DatabaseInstanceClass { get; set; } = "db.t3.micro";
    public int LambdaMemory { get; set; } = 512;
    public double TaskCpu { get; set; } = 0.5;
    public int TaskMemory { get; set; } = 1024;
    public string CacheNodeType { get; set; } = "cache.t3.micro";
    public string SearchInstanceType { get; set; } = "t3.small.search";
    public bool HighAvailability { get; set; } = false;
    public int BackupRetentionDays { get; set; } = 1;

    /// <summary>
    /// Gets production-appropriate sizing for production-class environments
    /// </summary>
    public static ResourceSizing GetProductionSizing()
    {
        var (vCpu, memory) = EnvironmentSizing.GetComputeSize(EnvironmentType.Production);
        var (minCapacity, maxCapacity, desiredCapacity) = EnvironmentSizing.GetAutoScalingConfig(EnvironmentType.Production);

        return new ResourceSizing
        {
            InstanceType = EnvironmentSizing.GetInstanceSize(EnvironmentType.Production),
            MinCapacity = minCapacity,
            MaxCapacity = maxCapacity,
            DesiredCapacity = desiredCapacity,
            DatabaseInstanceClass = EnvironmentSizing.GetDatabaseSize(EnvironmentType.Production),
            LambdaMemory = EnvironmentSizing.GetLambdaMemory(EnvironmentType.Production),
            TaskCpu = vCpu,
            TaskMemory = memory,
            CacheNodeType = EnvironmentSizing.GetCacheNodeType(EnvironmentType.Production),
            SearchInstanceType = EnvironmentSizing.GetSearchInstanceType(EnvironmentType.Production),
            HighAvailability = EnvironmentSizing.IsHighAvailabilityEnabled(EnvironmentType.Production),
            BackupRetentionDays = EnvironmentSizing.GetBackupRetentionDays(EnvironmentType.Production)
        };
    }

    /// <summary>
    /// Gets development-appropriate sizing for non-production environments
    /// </summary>
    public static ResourceSizing GetDevelopmentSizing()
    {
        var (vCpu, memory) = EnvironmentSizing.GetComputeSize(EnvironmentType.Development);
        var (minCapacity, maxCapacity, desiredCapacity) = EnvironmentSizing.GetAutoScalingConfig(EnvironmentType.Development);

        return new ResourceSizing
        {
            InstanceType = EnvironmentSizing.GetInstanceSize(EnvironmentType.Development),
            MinCapacity = minCapacity,
            MaxCapacity = maxCapacity,
            DesiredCapacity = desiredCapacity,
            DatabaseInstanceClass = EnvironmentSizing.GetDatabaseSize(EnvironmentType.Development),
            LambdaMemory = EnvironmentSizing.GetLambdaMemory(EnvironmentType.Development),
            TaskCpu = vCpu,
            TaskMemory = memory,
            CacheNodeType = EnvironmentSizing.GetCacheNodeType(EnvironmentType.Development),
            SearchInstanceType = EnvironmentSizing.GetSearchInstanceType(EnvironmentType.Development),
            HighAvailability = EnvironmentSizing.IsHighAvailabilityEnabled(EnvironmentType.Development),
            BackupRetentionDays = EnvironmentSizing.GetBackupRetentionDays(EnvironmentType.Development)
        };
    }

    /// <summary>
    /// Gets sizing appropriate for the environment type
    /// </summary>
    public static ResourceSizing GetSizingForEnvironment(AccountType accountType)
    {
        return accountType == AccountType.Production
            ? GetProductionSizing()
            : GetDevelopmentSizing();
    }

    /// <summary>
    /// Gets sizing appropriate for the specific environment
    /// </summary>
    public static ResourceSizing GetSizingForEnvironment(EnvironmentType environmentType)
    {
        var (vCpu, memory) = EnvironmentSizing.GetComputeSize(environmentType);
        var (minCapacity, maxCapacity, desiredCapacity) = EnvironmentSizing.GetAutoScalingConfig(environmentType);

        return new ResourceSizing
        {
            InstanceType = EnvironmentSizing.GetInstanceSize(environmentType),
            MinCapacity = minCapacity,
            MaxCapacity = maxCapacity,
            DesiredCapacity = desiredCapacity,
            DatabaseInstanceClass = EnvironmentSizing.GetDatabaseSize(environmentType),
            LambdaMemory = EnvironmentSizing.GetLambdaMemory(environmentType),
            TaskCpu = vCpu,
            TaskMemory = memory,
            CacheNodeType = EnvironmentSizing.GetCacheNodeType(environmentType),
            SearchInstanceType = EnvironmentSizing.GetSearchInstanceType(environmentType),
            HighAvailability = EnvironmentSizing.IsHighAvailabilityEnabled(environmentType),
            BackupRetentionDays = EnvironmentSizing.GetBackupRetentionDays(environmentType)
        };
    }

    /// <summary>
    /// Gets sizing appropriate for the specific environment name
    /// </summary>
    public static ResourceSizing GetSizingForEnvironment(string environmentName)
    {
        if (Enum.TryParse<EnvironmentType>(environmentName, out var environmentType))
            return GetSizingForEnvironment(environmentType);
        
        // Default to development sizing for unknown environments
        return GetDevelopmentSizing();
    }
}