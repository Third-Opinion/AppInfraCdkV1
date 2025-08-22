using System;
using System.Collections.Generic;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using System.Linq;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Builder for creating ECS container definitions from configuration
/// </summary>
public class ContainerDefinitionBuilder
{
    private readonly DeploymentContext _context;
    private readonly EnvironmentVariableBuilder _envVarBuilder;

    public ContainerDefinitionBuilder(DeploymentContext context)
    {
        _context = context;
        _envVarBuilder = new EnvironmentVariableBuilder(context);
    }

    /// <summary>
    /// Build container definition options from configuration
    /// </summary>
    public ContainerDefinitionOptions BuildContainerDefinition(
        ContainerDefinitionConfig containerConfig,
        ContainerImage containerImage,
        ILogGroup logGroup,
        Dictionary<string, string> environmentVars,
        Amazon.CDK.AWS.ECS.HealthCheck? healthCheck,
        Amazon.CDK.AWS.ECS.PortMapping[] portMappings)
    {
        if (containerConfig.Name == null)
        {
            throw new ArgumentException("Container name cannot be null", nameof(containerConfig));
        }

        return new ContainerDefinitionOptions
        {
            Image = containerImage,
            ContainerName = containerConfig.Name,
            Cpu = containerConfig.Cpu ?? 0,
            Essential = containerConfig.Essential ?? GetDefaultEssential(containerConfig.Name),
            Environment = environmentVars,
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            }),
            HealthCheck = healthCheck
        };
    }

    /// <summary>
    /// Get default essential setting for container
    /// </summary>
    public bool GetDefaultEssential(string containerName)
    {
        // Main application containers are essential by default
        return !IsCronJobContainer(containerName);
    }

    /// <summary>
    /// Check if container is a cron job (non-essential)
    /// </summary>
    private bool IsCronJobContainer(string containerName)
    {
        // Check container name patterns for cron jobs
        var cronJobPatterns = new[] { "cron", "job", "worker", "loader", "processor" };
        return cronJobPatterns.Any(pattern => containerName.ToLower().Contains(pattern));
    }

    /// <summary>
    /// Get container-specific environment defaults
    /// </summary>
    public Dictionary<string, string> GetContainerSpecificEnvironmentDefaults(string containerName)
    {
        return _envVarBuilder.GetContainerSpecificEnvironmentDefaults(containerName);
    }

    /// <summary>
    /// Create default environment variables
    /// </summary>
    public Dictionary<string, string> CreateDefaultEnvironmentVariables(string? containerName = null)
    {
        return _envVarBuilder.CreateDefaultEnvironmentVariables(containerName);
    }

    /// <summary>
    /// Get environment variables for container
    /// </summary>
    public Dictionary<string, string> GetEnvironmentVariables(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        return _envVarBuilder.GetEnvironmentVariables(containerConfig, containerName);
    }

    /// <summary>
    /// Get container port from configuration
    /// </summary>
    public int? GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            var mainPortMapping = containerConfig.PortMappings.FirstOrDefault();
            return mainPortMapping?.ContainerPort;
        }

        // Default ports based on container name
        return containerName.ToLower() switch
        {
            "trial-match" => 8080,
            _ => 8080
        };
    }
} 