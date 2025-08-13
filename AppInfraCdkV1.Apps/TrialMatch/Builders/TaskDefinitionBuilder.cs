using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Enums;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Builder for creating ECS task definitions
/// </summary>
public class TaskDefinitionBuilder : Construct
{
    private readonly DeploymentContext _context;
    private readonly IamRoleBuilder _iamRoleBuilder;

    public TaskDefinitionBuilder(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
        _iamRoleBuilder = new IamRoleBuilder(this, "IamRoleBuilder", context);
    }

    /// <summary>
    /// Create Fargate task definition
    /// </summary>
    public FargateTaskDefinition CreateFargateTaskDefinition(
        Construct scope,
        string serviceName,
        ICluster cluster,
        IRole taskRole,
        IRole executionRole,
        ILogGroup logGroup)
    {
        var cpu = GetTaskCpu(_context.Environment.AccountType);
        var memory = GetTaskMemory(_context.Environment.AccountType);

        return new FargateTaskDefinition(scope, $"TrialMatchTaskDefinition-{serviceName}", new FargateTaskDefinitionProps
        {
            Cpu = cpu,
            MemoryLimitMiB = memory,
            TaskRole = taskRole,
            ExecutionRole = executionRole,
            Family = $"{_context.Application.Name}-{serviceName}-{_context.Environment.Name}",
            RuntimePlatform = new RuntimePlatform
            {
                CpuArchitecture = CpuArchitecture.X86_64,
                OperatingSystemFamily = OperatingSystemFamily.LINUX
            }
        });
    }

    /// <summary>
    /// Create task role with necessary permissions using IamRoleBuilder
    /// </summary>
    public IRole CreateTaskRole(string serviceName)
    {
        return _iamRoleBuilder.CreateTaskRole(serviceName);
    }

    /// <summary>
    /// Create execution role with necessary permissions using IamRoleBuilder
    /// </summary>
    public IRole CreateExecutionRole(ILogGroup logGroup, string serviceName)
    {
        return _iamRoleBuilder.CreateExecutionRole(logGroup, serviceName);
    }

    /// <summary>
    /// Get task CPU based on account type
    /// </summary>
    private double GetTaskCpu(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => 1024, // 1 vCPU
            AccountType.NonProduction => 512, // 0.5 vCPU
            _ => 512
        };
    }

    /// <summary>
    /// Get task memory based on account type
    /// </summary>
    private double GetTaskMemory(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => 2048, // 2 GB
            AccountType.NonProduction => 1024, // 1 GB
            _ => 1024
        };
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
    /// Create log group for the service
    /// </summary>
    public ILogGroup CreateLogGroup(string serviceName)
    {
        var logRetention = GetLogRetention(_context.Environment.AccountType);

        return new LogGroup(this, $"LogGroup-{serviceName}", new LogGroupProps
        {
            LogGroupName = $"/ecs/{_context.Application.Name}/{serviceName}/{_context.Environment.Name}",
            Retention = logRetention,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
    }
} 