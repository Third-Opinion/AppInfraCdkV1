using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Services;

/// <summary>
/// Service responsible for exporting CloudFormation outputs for the TrialMatch ECS stack
/// 
/// This service centralizes all output creation logic, making the main stack cleaner
/// and providing a single point of control for all exported values.
/// </summary>
public class OutputExporter
{
    private readonly Stack _stack;
    private readonly DeploymentContext _context;

    public OutputExporter(Stack stack, DeploymentContext context)
    {
        _stack = stack;
        _context = context;
    }

    /// <summary>
    /// Export cluster information outputs
    /// </summary>
    public void ExportClusterOutputs(ICluster cluster)
    {
        if (cluster == null) return;

        new CfnOutput(_stack, $"ClusterArn", new CfnOutputProps
        {
            Value = cluster.ClusterArn,
            Description = "TrialMatch ECS Cluster ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-cluster-arn"
        });

        new CfnOutput(_stack, $"ClusterName", new CfnOutputProps
        {
            Value = cluster.ClusterName,
            Description = "TrialMatch ECS Cluster Name",
            ExportName = $"{_context.Environment.Name}-trial-match-cluster-name"
        });
    }

    /// <summary>
    /// Export task definition outputs for a specific service
    /// </summary>
    public void ExportTaskDefinitionOutputs(FargateTaskDefinition taskDefinition,
        FargateService service,
        string serviceName,
        ContainerInfo containerInfo)
    {
        new CfnOutput(_stack, $"TaskDefinitionArn-{serviceName}", new CfnOutputProps
        {
            Value = taskDefinition.TaskDefinitionArn,
            Description = $"TrialMatch {serviceName} Task Definition ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-task-definition-arn"
        });

        new CfnOutput(_stack, $"ServiceArn-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceArn,
            Description = $"TrialMatch {serviceName} ECS Service ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-service-arn"
        });

        new CfnOutput(_stack, $"ServiceName-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = $"TrialMatch {serviceName} ECS Service Name",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-service-name"
        });

        new CfnOutput(_stack, $"ContainerName-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerName,
            Description = $"TrialMatch {serviceName} Container Name",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-container-name"
        });

        new CfnOutput(_stack, $"ContainerPort-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerPort.ToString(),
            Description = $"TrialMatch {serviceName} Container Port",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-container-port"
        });
    }

    /// <summary>
    /// Export GitHub Actions ECS deployment role ARN
    /// </summary>
    public void ExportGitHubActionsEcsDeployRole(IRole role)
    {
        new CfnOutput(_stack, "GitHubActionsEcsDeployRoleArn", new CfnOutputProps
        {
            Value = role.RoleArn,
            Description = $"ARN of the GitHub Actions ECS deployment role for TrialMatch in {_context.Environment.Name}",
            ExportName = $"{_context.Environment.Name}-trial-match-github-actions-ecs-deploy-role-arn"
        });
    }

    /// <summary>
    /// Export ECS service outputs for a specific service
    /// </summary>
    public void ExportEcsServiceOutputs(FargateService service, string serviceName)
    {
        new CfnOutput(_stack, $"ServiceArn-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceArn,
            Description = $"TrialMatch {serviceName} ECS Service ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-service-arn"
        });

        new CfnOutput(_stack, $"ServiceName-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = $"TrialMatch {serviceName} ECS Service Name",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-service-name"
        });
    }

    /// <summary>
    /// Export container-specific outputs
    /// </summary>
    public void ExportContainerOutputs(string serviceName, ContainerInfo containerInfo)
    {
        new CfnOutput(_stack, $"ContainerName-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerName,
            Description = $"TrialMatch {serviceName} Container Name",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-container-name"
        });

        new CfnOutput(_stack, $"ContainerPort-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerPort.ToString(),
            Description = $"TrialMatch {serviceName} Container Port",
            ExportName = $"{_context.Environment.Name}-trial-match-{serviceName}-container-port"
        });
    }
}
