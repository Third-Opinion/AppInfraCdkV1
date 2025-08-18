using System;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Exports stack outputs and CloudFormation exports for the TrialFinderV2 application
/// 
/// This service handles:
/// - ECS service output exports
/// - Scheduled task output exports
/// - IAM role ARN exports
/// - Cluster information exports
/// </summary>
public class OutputExporter : Construct
{
    private readonly DeploymentContext _context;

    public OutputExporter(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Export ECS service outputs for external consumption
    /// </summary>
    public void ExportEcsOutputs(FargateService service, FargateTaskDefinition taskDefinition)
    {
        // Export service name
        new CfnOutput(this, "ServiceName", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = "ECS Service name",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-service-name"
        });

        // Export task definition ARN
        new CfnOutput(this, "TaskDefinitionArn", new CfnOutputProps
        {
            Value = taskDefinition.TaskDefinitionArn,
            Description = "ECS Task Definition ARN for GitHub Actions deployments",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-task-definition-arn"
        });

        // Export task definition family
        new CfnOutput(this, "TaskDefinitionFamily", new CfnOutputProps
        {
            Value = taskDefinition.Family,
            Description = "ECS Task Definition family name",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-task-family"
        });

        // Export cluster name from service
        new CfnOutput(this, "ServiceClusterName", new CfnOutputProps
        {
            Value = service.Cluster.ClusterName,
            Description = "ECS Cluster name from service",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-service-cluster-name"
        });
    }

    /// <summary>
    /// Export scheduled task outputs for external consumption
    /// </summary>
    public void ExportScheduledTaskOutputs(string taskDefinitionArn, string taskDefinitionFamily)
    {
        // Export scheduled task definition ARN
        new CfnOutput(this, "ScheduledTaskDefinitionArn", new CfnOutputProps
        {
            Value = taskDefinitionArn,
            Description = "ECS Scheduled Task Definition ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-scheduled-task-definition-arn"
        });

        // Export scheduled task definition family
        new CfnOutput(this, "ScheduledTaskDefinitionFamily", new CfnOutputProps
        {
            Value = taskDefinitionFamily,
            Description = "ECS Scheduled Task Definition family name",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-scheduled-task-family"
        });
    }

    /// <summary>
    /// Export IAM role ARNs for external consumption
    /// </summary>
    public void ExportIamRoleOutputs(IRole taskRole, IRole executionRole, IRole? githubActionsRole = null)
    {
        // Export task role ARN
        new CfnOutput(this, "TaskRoleArn", new CfnOutputProps
        {
            Value = taskRole.RoleArn,
            Description = "ECS Task IAM Role ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-task-role-arn"
        });

        // Export execution role ARN
        new CfnOutput(this, "ExecutionRoleArn", new CfnOutputProps
        {
            Value = executionRole.RoleArn,
            Description = "ECS Execution IAM Role ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-execution-role-arn"
        });

        // Export GitHub Actions deployment role ARN if provided
        if (githubActionsRole != null)
        {
            new CfnOutput(this, "GithubActionsRoleArn", new CfnOutputProps
            {
                Value = githubActionsRole.RoleArn,
                Description = "GitHub Actions deployment IAM Role ARN",
                ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-github-actions-role-arn"
            });
        }
    }

    /// <summary>
    /// Export cluster information for external consumption
    /// </summary>
    public void ExportClusterOutputs(ICluster cluster)
    {
        // Export cluster ARN
        new CfnOutput(this, "ClusterArn", new CfnOutputProps
        {
            Value = cluster.ClusterArn,
            Description = "ECS Cluster ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-cluster-arn"
        });

        // Export cluster name
        new CfnOutput(this, "ClusterName", new CfnOutputProps
        {
            Value = cluster.ClusterName,
            Description = "ECS Cluster name",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-cluster-name"
        });
    }

    /// <summary>
    /// Export all outputs for a complete ECS setup
    /// </summary>
    public void ExportAllOutputs(FargateService service, FargateTaskDefinition taskDefinition, IRole taskRole, IRole executionRole, IRole? githubActionsRole = null)
    {
        ExportEcsOutputs(service, taskDefinition);
        ExportIamRoleOutputs(taskRole, executionRole, githubActionsRole);
        ExportClusterOutputs(service.Cluster);
    }

    /// <summary>
    /// Export GitHub Actions ECS deployment role
    /// </summary>
    public void ExportGitHubActionsEcsDeployRole(IRole role)
    {
        new CfnOutput(this, "GithubActionsEcsDeployRoleArn", new CfnOutputProps
        {
            Value = role.RoleArn,
            Description = "GitHub Actions ECS deployment IAM Role ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-github-actions-ecs-deploy-role-arn"
        });
    }
}
