using System;
using System.Collections.Generic;
using System.Linq;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Validates ECS configuration and container definitions
/// </summary>
public class ConfigurationValidator
{
    private readonly DeploymentContext _context;

    public ConfigurationValidator(DeploymentContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Validate container configuration for ECS deployment
    /// </summary>
    public void ValidateContainerConfiguration(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig == null)
        {
            throw new ArgumentNullException(nameof(containerConfig), "Container configuration cannot be null");
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException("Container name cannot be null or empty", nameof(containerName));
        }

        // Validate container name
        ValidateContainerName(containerConfig.Name, containerName);

        // Validate port mappings
        if (containerConfig.PortMappings?.Count > 0)
        {
            ValidatePortMappings(containerConfig.PortMappings, containerName);
        }

        // Validate health check configuration
        if (containerConfig.HealthCheck != null)
        {
            ValidateHealthCheckConfiguration(containerConfig.HealthCheck, containerName);
        }

        // Validate environment variables
        if (containerConfig.Environment?.Count > 0)
        {
            ValidateEnvironmentVariables(containerConfig.Environment, containerName);
        }

        // Validate secrets
        if (containerConfig.Secrets?.Count > 0)
        {
            ValidateSecretsConfiguration(containerConfig.Secrets, containerName);
        }
    }

    /// <summary>
    /// Validate container name
    /// </summary>
    private void ValidateContainerName(string? containerName, string contextName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException($"Container name is required for container '{contextName}'");
        }

        // Check for invalid characters in container name
        var invalidChars = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        if (invalidChars.Any(c => containerName.Contains(c)))
        {
            throw new ArgumentException($"Container name '{containerName}' contains invalid characters: {string.Join(", ", invalidChars)}");
        }

        // Check length constraints
        if (containerName.Length > 255)
        {
            throw new ArgumentException($"Container name '{containerName}' exceeds maximum length of 255 characters");
        }
    }

    /// <summary>
    /// Validate port mappings configuration
    /// </summary>
    private void ValidatePortMappings(List<PortMapping> portMappings, string containerName)
    {
        foreach (var portMapping in portMappings)
        {
            // Validate container port
            if (portMapping.ContainerPort == null || portMapping.ContainerPort <= 0)
            {
                throw new ArgumentException($"Container '{containerName}' has invalid port mapping: ContainerPort must be a positive integer");
            }

            if (portMapping.ContainerPort > 65535)
            {
                throw new ArgumentException($"Container '{containerName}' has invalid port mapping: ContainerPort must be <= 65535");
            }

            // Validate host port if specified
            if (portMapping.HostPort.HasValue)
            {
                if (portMapping.HostPort.Value <= 0 || portMapping.HostPort.Value > 65535)
                {
                    throw new ArgumentException($"Container '{containerName}' has invalid port mapping: HostPort must be between 1 and 65535");
                }
            }

            // Validate protocol
            if (!string.IsNullOrWhiteSpace(portMapping.Protocol))
            {
                ValidateProtocol(portMapping.Protocol, containerName);
            }

            // Validate app protocol
            if (!string.IsNullOrWhiteSpace(portMapping.AppProtocol))
            {
                ValidateAppProtocol(portMapping.AppProtocol, containerName);
            }
        }

        // Check for duplicate port mappings
        var duplicatePorts = portMappings
            .Where(pm => pm.ContainerPort.HasValue)
            .GroupBy(pm => pm.ContainerPort.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatePorts.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has duplicate port mappings: {string.Join(", ", duplicatePorts)}");
        }
    }

    /// <summary>
    /// Validate protocol string
    /// </summary>
    private void ValidateProtocol(string protocol, string containerName)
    {
        var validProtocols = new[] { "tcp", "udp", "TCP", "UDP" };
        if (!validProtocols.Contains(protocol))
        {
            throw new ArgumentException($"Container '{containerName}' has invalid port mapping: Protocol must be 'tcp' or 'udp', got '{protocol}'");
        }
    }

    /// <summary>
    /// Validate application protocol string
    /// </summary>
    private void ValidateAppProtocol(string appProtocol, string containerName)
    {
        var validAppProtocols = new[] { "http", "https", "grpc", "HTTP", "HTTPS", "GRPC" };
        if (!validAppProtocols.Contains(appProtocol))
        {
            throw new ArgumentException($"Container '{containerName}' has invalid port mapping: AppProtocol must be 'http', 'https', or 'grpc', got '{appProtocol}'");
        }
    }

    /// <summary>
    /// Validate health check configuration
    /// </summary>
    private void ValidateHealthCheckConfiguration(HealthCheckConfig healthCheck, string containerName)
    {
        // Validate command
        if (healthCheck.Command?.Count > 0)
        {
            if (healthCheck.Command.Count < 2)
            {
                throw new ArgumentException($"Container '{containerName}' has invalid health check: Command must have at least 2 elements (CMD-SHELL and the actual command)");
            }

            if (healthCheck.Command[0] != "CMD-SHELL")
            {
                throw new ArgumentException($"Container '{containerName}' has invalid health check: First command element must be 'CMD-SHELL'");
            }
        }

        // Validate intervals
        if (healthCheck.Interval.HasValue && healthCheck.Interval.Value <= 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid health check: Interval must be positive");
        }

        if (healthCheck.Timeout.HasValue && healthCheck.Timeout.Value <= 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid health check: Timeout must be positive");
        }

        if (healthCheck.StartPeriod.HasValue && healthCheck.StartPeriod.Value < 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid health check: StartPeriod must be non-negative");
        }

        if (healthCheck.Retries.HasValue && healthCheck.Retries.Value < 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid health check: Retries must be non-negative");
        }

        // Validate timeout vs interval
        if (healthCheck.Timeout.HasValue && healthCheck.Interval.HasValue && healthCheck.Timeout.Value >= healthCheck.Interval.Value)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid health check: Timeout must be less than Interval");
        }
    }

    /// <summary>
    /// Validate environment variables configuration
    /// </summary>
    private void ValidateEnvironmentVariables(List<EnvironmentVariable> environmentVars, string containerName)
    {
        var invalidVars = environmentVars
            .Where(ev => string.IsNullOrWhiteSpace(ev.Name) || string.IsNullOrWhiteSpace(ev.Value))
            .ToList();

        if (invalidVars.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid environment variables: Names and values cannot be null or empty");
        }

        // Check for duplicate environment variable names
        var duplicateNames = environmentVars
            .GroupBy(ev => ev.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has duplicate environment variable names: {string.Join(", ", duplicateNames)}");
        }

        // Validate environment variable names (must be valid shell variable names)
        var invalidNames = environmentVars
            .Where(ev => !IsValidEnvironmentVariableName(ev.Name))
            .Select(ev => ev.Name)
            .ToList();

        if (invalidNames.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid environment variable names: {string.Join(", ", invalidNames)}");
        }
    }

    /// <summary>
    /// Validate secrets configuration
    /// </summary>
    private void ValidateSecretsConfiguration(List<string> secrets, string containerName)
    {
        var invalidSecrets = secrets
            .Where(s => string.IsNullOrWhiteSpace(s))
            .ToList();

        if (invalidSecrets.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has invalid secrets: Secret names cannot be null or empty");
        }

        // Check for duplicate secret names
        var duplicateSecrets = secrets
            .GroupBy(s => s)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateSecrets.Count > 0)
        {
            throw new ArgumentException($"Container '{containerName}' has duplicate secret names: {string.Join(", ", duplicateSecrets)}");
        }
    }

    /// <summary>
    /// Check if environment variable name is valid
    /// </summary>
    private bool IsValidEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Must start with a letter or underscore
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // Must contain only letters, digits, and underscores
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// Validate task definition configuration
    /// </summary>
    public void ValidateTaskDefinitionConfiguration(TaskDefinitionConfig taskDefConfig, string serviceName)
    {
        if (taskDefConfig == null)
        {
            throw new ArgumentNullException(nameof(taskDefConfig), "Task definition configuration cannot be null");
        }

        if (string.IsNullOrWhiteSpace(taskDefConfig.TaskDefinitionName))
        {
            throw new ArgumentException($"TaskDefinitionName is required in the configuration for service '{serviceName}'");
        }

        // Validate container definitions if provided
        if (taskDefConfig.ContainerDefinitions?.Count > 0)
        {
            foreach (var container in taskDefConfig.ContainerDefinitions)
            {
                ValidateContainerConfiguration(container, container.Name ?? "unknown");
            }

            // Check for duplicate container names within this task definition
            var containerNames = taskDefConfig.ContainerDefinitions
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name!)
                .ToList();

            var duplicateNames = containerNames
                .GroupBy(name => name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNames.Count > 0)
            {
                throw new ArgumentException($"Duplicate container names found in task '{taskDefConfig.TaskDefinitionName}' for service '{serviceName}': {string.Join(", ", duplicateNames)}");
            }
        }
    }

    /// <summary>
    /// Validate service configuration
    /// </summary>
    public void ValidateServiceConfiguration(ServiceConfig serviceConfig)
    {
        if (serviceConfig == null)
        {
            throw new ArgumentNullException(nameof(serviceConfig), "Service configuration cannot be null");
        }

        if (string.IsNullOrWhiteSpace(serviceConfig.ServiceName))
        {
            throw new ArgumentException("ServiceName is required in the configuration");
        }

        if (serviceConfig.TaskDefinition == null || serviceConfig.TaskDefinition.Count == 0)
        {
            throw new ArgumentException($"At least one TaskDefinition configuration is required for service '{serviceConfig.ServiceName}'");
        }

        foreach (var taskDef in serviceConfig.TaskDefinition)
        {
            ValidateTaskDefinitionConfiguration(taskDef, serviceConfig.ServiceName);
        }
    }

    /// <summary>
    /// Validate complete ECS configuration
    /// </summary>
    public void ValidateEcsConfiguration(EcsTaskConfiguration config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config), "ECS configuration cannot be null");
        }

        if (config.Services == null || config.Services.Count == 0)
        {
            throw new ArgumentException("At least one Service configuration is required");
        }

        // Check for duplicate service names
        var serviceNames = config.Services
            .Where(s => !string.IsNullOrWhiteSpace(s.ServiceName))
            .Select(s => s.ServiceName)
            .ToList();

        var duplicateServiceNames = serviceNames
            .GroupBy(name => name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateServiceNames.Count > 0)
        {
            throw new ArgumentException($"Duplicate service names found: {string.Join(", ", duplicateServiceNames)}");
        }

        // Validate each service
        foreach (var service in config.Services)
        {
            ValidateServiceConfiguration(service);
        }
    }
}
