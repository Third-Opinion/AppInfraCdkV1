using System;
using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Builder for creating ECS container health checks
/// </summary>
public class HealthCheckBuilder
{
    /// <summary>
    /// Get container health check configuration
    /// </summary>
    public Amazon.CDK.AWS.ECS.HealthCheck? GetContainerHealthCheck(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        // Skip health check for non-essential containers or if explicitly disabled
        if (!(containerConfig.Essential ?? GetDefaultEssential(containerName)) || 
            containerConfig.DisableHealthCheck == true)
        {
            return null;
        }

        // Use custom health check if provided
        if (containerConfig.HealthCheck?.Command?.Count > 0)
        {
            return CreateCustomHealthCheck(containerConfig.HealthCheck, containerName);
        }

        // Use standard health check
        return GetStandardHealthCheck(containerConfig);
    }

    /// <summary>
    /// Create custom health check from configuration
    /// </summary>
    public Amazon.CDK.AWS.ECS.HealthCheck CreateCustomHealthCheck(
        HealthCheckConfig healthCheckConfig,
        string containerName)
    {
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = healthCheckConfig.Command?.ToArray() ?? new[] { "CMD-SHELL", "curl -f http://localhost:8080/health || exit 1" },
            Interval = Duration.Seconds(healthCheckConfig.Interval ?? 30),
            Timeout = Duration.Seconds(healthCheckConfig.Timeout ?? 5),
            Retries = healthCheckConfig.Retries ?? 3,
            StartPeriod = Duration.Seconds(healthCheckConfig.StartPeriod ?? 60)
        };
    }

    /// <summary>
    /// Get standard health check configuration
    /// </summary>
    public Amazon.CDK.AWS.ECS.HealthCheck GetStandardHealthCheck(ContainerDefinitionConfig? containerConfig = null)
    {
        var healthCheckPath = GetHealthCheckPath(containerConfig);
        
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[] { "CMD-SHELL", $"curl -f http://localhost:8080{healthCheckPath} || exit 1" },
            Interval = Duration.Seconds(30),
            Timeout = Duration.Seconds(5),
            Retries = 3,
            StartPeriod = Duration.Seconds(60)
        };
    }

    /// <summary>
    /// Get health check path from configuration or environment variable
    /// </summary>
    private string GetHealthCheckPath(ContainerDefinitionConfig? containerConfig)
    {
        // Check if health check path is specified in environment variables
        if (containerConfig?.Environment?.Count > 0)
        {
            var healthCheckPathVar = containerConfig.Environment
                .FirstOrDefault(e => e.Name == "HEALTH_CHECK_PATH");
            
            if (healthCheckPathVar?.Value != null)
            {
                return healthCheckPathVar.Value;
            }
        }

        // Default health check path
        return "/health";
    }

    /// <summary>
    /// Get default essential setting for container
    /// </summary>
    private bool GetDefaultEssential(string containerName)
    {
        // Main application containers are essential by default
        return !IsCronJobContainer(containerName);
    }

    /// <summary>
    /// Check if container is a cron job (non-essential)
    /// </summary>
    private bool IsCronJobContainer(string containerName)
    {
        // Non-essential containers are typically cron jobs
        var cronJobPatterns = new[] { "cron", "job", "worker", "loader", "processor" };
        return cronJobPatterns.Any(pattern => containerName.ToLower().Contains(pattern));
    }

    /// <summary>
    /// Create default health check for placeholder containers
    /// </summary>
    public Amazon.CDK.AWS.ECS.HealthCheck CreateDefaultHealthCheck()
    {
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[] { "CMD-SHELL", "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1" },
            Interval = Duration.Seconds(30),
            Timeout = Duration.Seconds(5),
            Retries = 3,
            StartPeriod = Duration.Seconds(60)
        };
    }
} 