using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;
using Amazon.CDK;

namespace AppInfraCdkV1.Apps.TrialMatch.Services;

/// <summary>
/// Service responsible for managing logging configuration for the TrialMatch ECS stack
/// 
/// This service centralizes all logging-related logic, including log group creation,
/// retention policies, and log configuration based on account type.
/// </summary>
public class LoggingManager
{
    private readonly Construct _scope;
    private readonly DeploymentContext _context;

    public LoggingManager(Construct scope, DeploymentContext context)
    {
        _scope = scope;
        _context = context;
    }

    /// <summary>
    /// Create a log group for a specific service with appropriate retention policy
    /// </summary>
    public ILogGroup CreateLogGroup(string serviceName)
    {
        var logGroupName = $"/ecs/trial-match/{serviceName}";
        
        return new LogGroup(_scope, $"LogGroup-{serviceName}", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Create a log group for a specific container within a service
    /// </summary>
    public ILogGroup CreateContainerLogGroup(string serviceName, string containerName)
    {
        var logGroupName = $"/ecs/trial-match/{serviceName}/{containerName}";
        
        return new LogGroup(_scope, $"LogGroup-{serviceName}-{containerName}", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Create a log group for the main application
    /// </summary>
    public ILogGroup CreateMainLogGroup()
    {
        var logGroupName = "/ecs/trial-match/main";
        
        return new LogGroup(_scope, "LogGroup-Main", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Create a log group for API services
    /// </summary>
    public ILogGroup CreateApiLogGroup()
    {
        var logGroupName = "/ecs/trial-match/api";
        
        return new LogGroup(_scope, "LogGroup-Api", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Create a log group for frontend services
    /// </summary>
    public ILogGroup CreateFrontendLogGroup()
    {
        var logGroupName = "/ecs/trial-match/frontend";
        
        return new LogGroup(_scope, "LogGroup-Frontend", new LogGroupProps
        {
            LogGroupName = logGroupName,
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Get log retention based on account type
    /// </summary>
    public RetentionDays GetLogRetention(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => RetentionDays.ONE_MONTH,
            AccountType.NonProduction => RetentionDays.ONE_WEEK,
            _ => RetentionDays.ONE_WEEK
        };
    }

    /// <summary>
    /// Create log groups for all configured containers in a service
    /// </summary>
    public Dictionary<string, ILogGroup> CreateLogGroupsForService(string serviceName, List<string> containerNames)
    {
        var logGroups = new Dictionary<string, ILogGroup>();
        
        foreach (var containerName in containerNames)
        {
            var logGroup = CreateContainerLogGroup(serviceName, containerName);
            logGroups[containerName] = logGroup;
        }
        
        return logGroups;
    }

    /// <summary>
    /// Get the appropriate log group for a container
    /// </summary>
    public ILogGroup GetLogGroupForContainer(string serviceName, string containerName)
    {
        // Try to get existing log group first
        var existingLogGroup = _scope.Node.TryGetContext($"LogGroup-{serviceName}-{containerName}") as ILogGroup;
        
        if (existingLogGroup != null)
        {
            return existingLogGroup;
        }
        
        // Create new log group if it doesn't exist
        return CreateContainerLogGroup(serviceName, containerName);
    }

    /// <summary>
    /// Configure log group with additional settings
    /// </summary>
    public void ConfigureLogGroup(ILogGroup logGroup, LogGroupProps? additionalProps = null)
    {
        if (additionalProps != null)
        {
            // Apply additional configuration if provided
            // Note: CDK doesn't allow modification of existing resources, so this is for future extensibility
        }
    }
}
