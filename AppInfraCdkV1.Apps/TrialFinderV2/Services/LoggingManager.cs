using System;
using Amazon.CDK;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.ECS;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Enums;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Manages CloudWatch logging configuration for the TrialFinderV2 application
/// 
/// This service handles:
/// - Log group creation with appropriate retention policies
/// - Environment-specific log retention configuration
/// - Logging driver setup for ECS tasks
/// </summary>
public class LoggingManager : Construct
{
    private readonly DeploymentContext _context;

    public LoggingManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Create log group for ECS tasks with appropriate retention
    /// </summary>
    public ILogGroup CreateLogGroup(string logGroupName, ResourcePurpose purpose)
    {
        return new LogGroup(this, logGroupName, new LogGroupProps
        {
            LogGroupName = _context.Namer.LogGroup(logGroupName, purpose),
            Retention = GetLogRetention(_context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }

    /// <summary>
    /// Get appropriate log retention based on environment
    /// </summary>
    public RetentionDays GetLogRetention(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => RetentionDays.ONE_MONTH,
            AccountType.NonProduction => RetentionDays.ONE_WEEK,
            _ => RetentionDays.THREE_DAYS
        };
    }

    /// <summary>
    /// Setup logging driver for ECS containers
    /// </summary>
    public LogDriver SetupLoggingDriver(ILogGroup logGroup, string streamPrefix)
    {
        return LogDriver.AwsLogs(new AwsLogDriverProps
        {
            LogGroup = logGroup,
            StreamPrefix = streamPrefix
        });
    }

    /// <summary>
    /// Create default log group for TrialFinderV2 application
    /// </summary>
    public ILogGroup CreateDefaultLogGroup()
    {
        return CreateLogGroup("trial-finder", ResourcePurpose.Web);
    }

    /// <summary>
    /// Create log group for specific container
    /// </summary>
    public ILogGroup CreateContainerLogGroup(string containerName)
    {
        return CreateLogGroup(containerName, ResourcePurpose.Web);
    }

    /// <summary>
    /// Get log retention duration for current environment
    /// </summary>
    public RetentionDays GetCurrentLogRetention()
    {
        return GetLogRetention(_context.Environment.AccountType);
    }
}
