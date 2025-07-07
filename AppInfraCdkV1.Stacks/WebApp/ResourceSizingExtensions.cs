using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Stacks.WebApp;

/// <summary>
/// Extension methods for ResourceSizing to provide convenience methods
/// </summary>
public static class ResourceSizingExtensions
{
    /// <summary>
    /// Gets the memory limit for ECS tasks based on the task memory setting
    /// </summary>
    public static int GetMemoryLimit(this ResourceSizing sizing)
    {
        return sizing.TaskMemory;
    }

    /// <summary>
    /// Gets the CPU limit for ECS tasks based on the task CPU setting
    /// </summary>
    public static int GetCpuLimit(this ResourceSizing sizing)
    {
        // Convert vCPU to CPU units (1 vCPU = 1024 CPU units)
        return (int)(sizing.TaskCpu * 1024);
    }

    /// <summary>
    /// Gets whether multi-AZ deployment should be enabled based on high availability setting
    /// </summary>
    public static bool IsMultiAzEnabled(this ResourceSizing sizing)
    {
        return sizing.HighAvailability;
    }

    /// <summary>
    /// Gets the number of availability zones to use based on high availability setting
    /// </summary>
    public static int GetAvailabilityZoneCount(this ResourceSizing sizing)
    {
        return sizing.HighAvailability ? 3 : 2;
    }

    /// <summary>
    /// Gets whether deletion protection should be enabled (typically for production)
    /// </summary>
    public static bool IsDeletionProtectionEnabled(this ResourceSizing sizing)
    {
        return sizing.HighAvailability; // Production environments have deletion protection
    }
}