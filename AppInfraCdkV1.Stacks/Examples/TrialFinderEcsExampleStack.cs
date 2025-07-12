using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Stacks.Components;
using Constructs;

namespace AppInfraCdkV1.Stacks.Examples;

/// <summary>
/// Example stack showing how to use ECS task definition with Secrets Manager integration
/// </summary>
public class TrialFinderEcsExampleStack : Stack
{
    private readonly DeploymentContext _context;

    public TrialFinderEcsExampleStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;

        // Import the base stack resources
        var vpcId = Fn.ImportValue($"{context.Environment.Name}-vpc-id");
        var vpc = Vpc.FromLookup(this, "ImportedVpc", new VpcLookupOptions
        {
            VpcId = vpcId
        });

        // Import the ECS cluster from shared stack
        var clusterName = $"{context.Environment.Name.ToLower()}-shared-cluster-ue2";
        var cluster = Cluster.FromClusterAttributes(this, "ImportedCluster", new ClusterAttributes
        {
            ClusterName = clusterName,
            Vpc = vpc,
            SecurityGroups = new ISecurityGroup[] { }
        });

        // Create log group for the service
        var logGroup = new LogGroup(this, "ServiceLogGroup", new LogGroupProps
        {
            LogGroupName = $"/aws/ecs/{context.Namer.EcsService("trialfinder")}",
            Retention = context.Environment.IsProductionClass 
                ? RetentionDays.ONE_MONTH 
                : RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Create task definition with secrets
        var taskWithSecrets = new EcsTaskWithSecrets(this, "TrialFinderTaskWithSecrets", new EcsTaskWithSecretsProps
        {
            ExecutionRoleArn = "arn:aws:iam::615299752206:role/dev-ecs-task-execution-role-ue2",
            TaskRoleArn = "arn:aws:iam::615299752206:role/dev-trialfinder-task-role-ue2",
            TaskDefinitionFamily = context.Namer.EcsTaskDefinition("trialfinder"),
            Cpu = 256,
            MemoryMiB = 512,
            ImageUri = "nginx:latest", // Example image - would be replaced with actual application image
            ContainerPort = 80,
            DatabaseSecretName = "/dev/trialfinder/database-connection",
            ApiKeysSecretName = "/dev/trialfinder/api-keys",
            JwtSecretName = "/dev/trialfinder/jwt-config",
            LogGroup = logGroup,
            Environment = new Dictionary<string, string>
            {
                ["NODE_ENV"] = context.Environment.Name.ToLower(),
                ["AWS_REGION"] = "us-east-2",
                ["LOG_LEVEL"] = context.Environment.IsProductionClass ? "info" : "debug"
            },
            Tags = context.GetCommonTags()
        });

        // Create ECS service (optional - for demonstration)
        var service = new FargateService(this, "TrialFinderService", new FargateServiceProps
        {
            Cluster = cluster,
            TaskDefinition = taskWithSecrets.TaskDefinition,
            ServiceName = context.Namer.EcsService("trialfinder"),
            DesiredCount = 1,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            SecurityGroups = new[]
            {
                SecurityGroup.FromSecurityGroupId(this, "EcsSecurityGroup", 
                    Fn.ImportValue($"{context.Environment.Name}-sg-ecs-id"))
            },
            EnableLogging = true,
            CloudMapOptions = new CloudMapOptions
            {
                Name = "trialfinder",
                DnsRecordType = DnsRecordType.A
            }
        });

        // Outputs
        new CfnOutput(this, "TaskDefinitionArn", new CfnOutputProps
        {
            Value = taskWithSecrets.TaskDefinition.TaskDefinitionArn,
            Description = "ARN of the task definition with secrets integration"
        });

        new CfnOutput(this, "ServiceArn", new CfnOutputProps
        {
            Value = service.ServiceArn,
            Description = "ARN of the ECS service"
        });

        // Apply common tags
        var tags = context.GetCommonTags();
        tags["StackType"] = "EcsExample";
        foreach (var tag in tags)
        {
            Tags.SetTag(tag.Key, tag.Value);
        }
    }
}