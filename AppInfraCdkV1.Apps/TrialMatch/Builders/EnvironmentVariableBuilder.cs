using System;
using System.Collections.Generic;
using System.Linq;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Builder for creating and managing environment variables for ECS containers
/// </summary>
public class EnvironmentVariableBuilder
{
    private readonly DeploymentContext _context;

    public EnvironmentVariableBuilder(DeploymentContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get environment variables for container from configuration
    /// </summary>
    public Dictionary<string, string> GetEnvironmentVariables(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        var envVars = new Dictionary<string, string>();

        // Add container-specific environment variables from configuration
        if (containerConfig.Environment?.Count > 0)
        {
            foreach (var envVar in containerConfig.Environment)
            {
                if (envVar.Name != null && envVar.Value != null)
                {
                    envVars[envVar.Name] = envVar.Value;
                }
            }
        }

        // Add default environment variables
        var defaultEnvVars = GetContainerSpecificEnvironmentDefaults(containerName);
        foreach (var kvp in defaultEnvVars)
        {
            if (!envVars.ContainsKey(kvp.Key))
            {
                envVars[kvp.Key] = kvp.Value;
            }
        }

        return envVars;
    }

    /// <summary>
    /// Get container-specific environment defaults
    /// </summary>
    public Dictionary<string, string> GetContainerSpecificEnvironmentDefaults(string containerName)
    {
        // Determine port based on container name
        var port = containerName.ToLower().Contains("frontend") ? "80" : "8080";
        
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(_context.Environment.Name),
            ["ENVIRONMENT"] = _context.Environment.Name,
            ["ACCOUNT_TYPE"] = _context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = _context.Application.Version,
            ["AWS_REGION"] = _context.Environment.Region,
            ["PORT"] = port,
            ["HEALTH_CHECK_PATH"] = "/health"
        };
    }

    /// <summary>
    /// Create default environment variables for a container
    /// </summary>
    public Dictionary<string, string> CreateDefaultEnvironmentVariables(string? containerName = null)
    {
        // Determine port based on container name
        var port = containerName?.ToLower().Contains("frontend") == true ? "80" : "8080";
        
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(_context.Environment.Name),
            ["ENVIRONMENT"] = _context.Environment.Name,
            ["ACCOUNT_TYPE"] = _context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = _context.Application.Version,
            ["AWS_REGION"] = _context.Environment.Region,
            ["PORT"] = port,
            ["HEALTH_CHECK_PATH"] = "/health",
            ["DEPLOYMENT_TYPE"] = "placeholder", // Default value, will be overridden
            ["IMAGE_SOURCE"] = "placeholder", // Default value, will be overridden
            ["ECR_REPOSITORY"] = "", // Default value, will be overridden
            ["MANAGED_BY"] = "CDK",
            ["APP_NAME"] = _context.Application.Name
        };
    }

    /// <summary>
    /// Create environment variables for ECR deployment
    /// </summary>
    public Dictionary<string, string> CreateEcrEnvironmentVariables(string containerName, string ecrImageUri, string repositoryName, ContainerDefinitionConfig? containerConfig = null)
    {
        var envVars = CreateDefaultEnvironmentVariables(containerName);
        
        // Add container-specific environment variables from configuration if provided
        if (containerConfig?.Environment?.Count > 0)
        {
            foreach (var envVar in containerConfig.Environment)
            {
                if (envVar.Name != null && envVar.Value != null)
                {
                    envVars[envVar.Name] = envVar.Value;
                }
            }
        }
        
        // Override with ECR-specific values
        envVars["DEPLOYMENT_TYPE"] = "ecr-latest";
        envVars["IMAGE_SOURCE"] = "ecr";
        envVars["ECR_REPOSITORY"] = repositoryName;
        
        return envVars;
    }

    /// <summary>
    /// Create environment variables for placeholder deployment
    /// </summary>
    public Dictionary<string, string> CreatePlaceholderEnvironmentVariables(string containerName)
    {
        var envVars = CreateDefaultEnvironmentVariables(containerName);
        
        // Override with placeholder-specific values
        envVars["DEPLOYMENT_TYPE"] = "placeholder";
        envVars["IMAGE_SOURCE"] = "placeholder";
        envVars["MANAGED_BY"] = "CDK";
        envVars["APP_NAME"] = _context.Application.Name;
        envVars["APP_VERSION"] = "1.0.0"; // Static version to prevent unnecessary redeployments
        
        return envVars;
    }

    /// <summary>
    /// Create environment variables for specified image deployment
    /// </summary>
    public Dictionary<string, string> CreateSpecifiedImageEnvironmentVariables(string containerName, string imageUri, ContainerDefinitionConfig? containerConfig = null)
    {
        var envVars = CreateDefaultEnvironmentVariables(containerName);
        
        // Add container-specific environment variables from configuration if provided
        if (containerConfig?.Environment?.Count > 0)
        {
            foreach (var envVar in containerConfig.Environment)
            {
                if (envVar.Name != null && envVar.Value != null)
                {
                    envVars[envVar.Name] = envVar.Value;
                }
            }
        }
        
        // Override with specified image values
        envVars["IMAGE_SOURCE"] = "specified";
        
        return envVars;
    }

    /// <summary>
    /// Merge environment variables with defaults, respecting priority
    /// </summary>
    public Dictionary<string, string> MergeEnvironmentVariables(
        Dictionary<string, string> customVars,
        Dictionary<string, string> defaultVars)
    {
        var merged = new Dictionary<string, string>(defaultVars);
        
        foreach (var kvp in customVars)
        {
            merged[kvp.Key] = kvp.Value;
        }
        
        return merged;
    }

    /// <summary>
    /// Add environment variable if not already present
    /// </summary>
    public void AddEnvironmentVariableIfNotPresent(
        Dictionary<string, string> envVars,
        string key,
        string value)
    {
        if (!envVars.ContainsKey(key))
        {
            envVars[key] = value;
        }
    }

    /// <summary>
    /// Add multiple environment variables if not already present
    /// </summary>
    public void AddEnvironmentVariablesIfNotPresent(
        Dictionary<string, string> envVars,
        Dictionary<string, string> additionalVars)
    {
        foreach (var kvp in additionalVars)
        {
            AddEnvironmentVariableIfNotPresent(envVars, kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Get ASP.NET Core environment name
    /// </summary>
    public string GetAspNetCoreEnvironment(string environmentName)
    {
        return environmentName.ToLower() switch
        {
            "development" => "Development",
            "integration" => "Integration",
            "staging" => "Staging",
            "production" => "Production",
            _ => "Development"
        };
    }

    /// <summary>
    /// Validate environment variable name
    /// </summary>
    public bool IsValidEnvironmentVariableName(string name)
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
    /// Validate environment variable value
    /// </summary>
    public bool IsValidEnvironmentVariableValue(string value)
    {
        // Environment variable values can contain any characters except null
        return value != null;
    }

    /// <summary>
    /// Validate environment variables dictionary
    /// </summary>
    public void ValidateEnvironmentVariables(Dictionary<string, string> envVars)
    {
        if (envVars == null)
            return;

        var invalidNames = envVars.Keys
            .Where(key => !IsValidEnvironmentVariableName(key))
            .ToList();

        if (invalidNames.Count > 0)
        {
            throw new ArgumentException($"Invalid environment variable names: {string.Join(", ", invalidNames)}");
        }

        var invalidValues = envVars
            .Where(kvp => !IsValidEnvironmentVariableValue(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        if (invalidValues.Count > 0)
        {
            throw new ArgumentException($"Invalid environment variable values for keys: {string.Join(", ", invalidValues)}");
        }
    }

    /// <summary>
    /// Get environment variables for health check path
    /// </summary>
    public string GetHealthCheckPath(ContainerDefinitionConfig? containerConfig)
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
    /// Create environment variables for specific container types
    /// </summary>
    public Dictionary<string, string> CreateContainerTypeEnvironmentVariables(string containerName)
    {
        var envVars = CreateDefaultEnvironmentVariables(containerName);
        
        // Add container type specific variables
        if (containerName.ToLower().Contains("frontend"))
        {
            envVars["CONTAINER_TYPE"] = "frontend";
            envVars["SERVICE_TYPE"] = "web";
        }
        else if (containerName.ToLower().Contains("api"))
        {
            envVars["CONTAINER_TYPE"] = "api";
            envVars["SERVICE_TYPE"] = "backend";
        }
        else if (containerName.ToLower().Contains("worker"))
        {
            envVars["CONTAINER_TYPE"] = "worker";
            envVars["SERVICE_TYPE"] = "background";
        }
        else
        {
            envVars["CONTAINER_TYPE"] = "general";
            envVars["SERVICE_TYPE"] = "application";
        }
        
        return envVars;
    }

    /// <summary>
    /// Create environment variables for cron job containers
    /// </summary>
    public Dictionary<string, string> CreateCronJobEnvironmentVariables(string containerName)
    {
        var envVars = CreateDefaultEnvironmentVariables(containerName);
        
        // Add cron job specific variables
        envVars["CONTAINER_TYPE"] = "cron";
        envVars["SERVICE_TYPE"] = "scheduled";
        envVars["ESSENTIAL"] = "false";
        
        return envVars;
    }
}
