using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Stacks.Components;

/// <summary>
/// Component for creating ECS task definitions with Secrets Manager integration
/// </summary>
public class EcsTaskWithSecrets : Construct
{
    public TaskDefinition TaskDefinition { get; private set; }
    public ContainerDefinition ContainerDefinition { get; private set; }

    public EcsTaskWithSecrets(Construct scope, string id, EcsTaskWithSecretsProps props)
        : base(scope, id)
    {
        // Reference the existing IAM roles by ARN
        var executionRole = Role.FromRoleArn(this, "ExecutionRole", props.ExecutionRoleArn);
        var taskRole = Role.FromRoleArn(this, "TaskRole", props.TaskRoleArn);

        // Create task definition
        TaskDefinition = new TaskDefinition(this, "TaskDefinition", new TaskDefinitionProps
        {
            Family = props.TaskDefinitionFamily,
            Cpu = props.Cpu.ToString(),
            MemoryMiB = props.MemoryMiB.ToString(),
            NetworkMode = NetworkMode.AWS_VPC,
            Compatibility = Compatibility.FARGATE,
            ExecutionRole = executionRole,
            TaskRole = taskRole
        });

        // Reference the secrets
        var dbSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "DatabaseSecret", props.DatabaseSecretName);
        var apiKeysSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "ApiKeysSecret", props.ApiKeysSecretName);
        var jwtSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "JwtSecret", props.JwtSecretName);

        // Add container with secret injection
        ContainerDefinition = TaskDefinition.AddContainer("app-container", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry(props.ImageUri),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                StreamPrefix = "ecs",
                LogGroup = props.LogGroup,
                LogRetention = RetentionDays.ONE_WEEK
            }),
            Essential = true,
            PortMappings = new[]
            {
                new PortMapping
                {
                    ContainerPort = props.ContainerPort,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            Environment = props.Environment,
            Secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>
            {
                // Database connection secrets
                ["DB_USERNAME"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(dbSecret, "username"),
                ["DB_PASSWORD"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(dbSecret, "password"),
                ["DB_HOST"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(dbSecret, "host"),
                ["DB_PORT"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(dbSecret, "port"),
                ["DB_DATABASE"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(dbSecret, "database"),
                
                // API keys
                ["STRIPE_API_KEY"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(apiKeysSecret, "stripe_api_key"),
                ["SENDGRID_API_KEY"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(apiKeysSecret, "sendgrid_api_key"),
                ["GOOGLE_MAPS_API_KEY"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(apiKeysSecret, "google_maps_api_key"),
                
                // JWT configuration
                ["JWT_SECRET"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(jwtSecret, "jwt_secret"),
                ["JWT_EXPIRATION"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(jwtSecret, "jwt_expiration"),
                ["REFRESH_TOKEN_SECRET"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(jwtSecret, "refresh_token_secret")
            }
        });

        // Add common tags
        foreach (var tag in props.Tags)
        {
            Amazon.CDK.Tags.Of(this).Add(tag.Key, tag.Value);
        }
    }
}

/// <summary>
/// Properties for EcsTaskWithSecrets construct
/// </summary>
public class EcsTaskWithSecretsProps
{
    public required string ExecutionRoleArn { get; set; }
    public required string TaskRoleArn { get; set; }
    public required string TaskDefinitionFamily { get; set; }
    public required int Cpu { get; set; }
    public required int MemoryMiB { get; set; }
    public required string ImageUri { get; set; }
    public required int ContainerPort { get; set; }
    public required string DatabaseSecretName { get; set; }
    public required string ApiKeysSecretName { get; set; }
    public required string JwtSecretName { get; set; }
    public required Amazon.CDK.AWS.Logs.ILogGroup LogGroup { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
    public Dictionary<string, string> Tags { get; set; } = new();
}