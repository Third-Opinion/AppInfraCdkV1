using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public class TrialFinderV2EcsStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;

    public TrialFinderV2EcsStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();

        // Load configuration including VPC name pattern
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dynamic lookup by name
        var vpc = CreateVpcReference(fullConfig.VpcNamePattern, context);

        // Import ALB stack outputs
        var albOutputs = ImportAlbStackOutputs();

        // Create ECS cluster
        var cluster = CreateEcsCluster(vpc, context);

        // Create ECS service with containers from configuration
        CreateEcsService(cluster, albOutputs, context);

        // Create test secrets for the current environment
        CreateTestSecrets();
    }

    /// <summary>
    /// Create VPC reference using dynamic lookup by name or fallback to hardcoded ID
    /// </summary>
    private IVpc CreateVpcReference(string? vpcNamePattern, DeploymentContext context)
    {
        // Validate VPC name pattern is provided
        if (string.IsNullOrEmpty(vpcNamePattern))
        {
            throw new InvalidOperationException(
                $"VPC name pattern is required but not found in configuration for environment '{context.Environment.Name}'. " +
                "Please add 'vpcNamePattern' to the configuration file.");
        }
        
        // Use dynamic lookup by VPC name tag
        return Vpc.FromLookup(this, "SharedVpc", new VpcLookupOptions
        {
            Tags = new Dictionary<string, string>
            {
                { "Name", vpcNamePattern }
            }
        });
    }

    /// <summary>
    /// Import outputs from the ALB stack
    /// </summary>
    private AlbStackOutputs ImportAlbStackOutputs()
    {
        var targetGroupArn
            = Fn.ImportValue(
                $"{_context.Environment.Name}-{_context.Application.Name}-target-group-arn");
        var ecsSecurityGroupId
            = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-ecs-sg-id");

        return new AlbStackOutputs
        {
            TargetGroupArn = targetGroupArn,
            EcsSecurityGroupId = ecsSecurityGroupId
        };
    }

    /// <summary>
    /// Create ECS cluster
    /// </summary>
    private ICluster CreateEcsCluster(IVpc vpc, DeploymentContext context)
    {
        var cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            ClusterName = context.Namer.EcsCluster(),
            Vpc = vpc
        });

        // Fargate capacity is enabled by default for ECS clusters

        return cluster;
    }

    /// <summary>
    /// Create ECS service with containers from configuration
    /// </summary>
    private void CreateEcsService(ICluster cluster,
        AlbStackOutputs albOutputs,
        DeploymentContext context)
    {
        // Load configuration from JSON
        var ecsConfig = _configLoader.LoadEcsConfig(context.Environment.Name);
        ecsConfig = _configLoader.SubstituteVariables(ecsConfig, context);

        // Create log group for ECS tasks
        var logGroup = new LogGroup(this, "TrialFinderLogGroup", new LogGroupProps
        {
            LogGroupName = context.Namer.LogGroup("trial-finder", ResourcePurpose.Web),
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Use taskDefinitionName from config or fallback to default naming
        var taskDefinitionName = ecsConfig.TaskDefinition?.TaskDefinitionName ??
                                 context.Namer.EcsTaskDefinition(ResourcePurpose.Web);

        // Create Fargate task definition
        var taskDefinition = new FargateTaskDefinition(this, taskDefinitionName,
            new FargateTaskDefinitionProps
            {
                Family = taskDefinitionName,
                MemoryLimitMiB = 512,
                Cpu = 256,
                TaskRole = CreateTaskRole(),
                ExecutionRole = CreateExecutionRole(logGroup),
                RuntimePlatform = new RuntimePlatform
                {
                    OperatingSystemFamily = OperatingSystemFamily.LINUX,
                    CpuArchitecture = CpuArchitecture.ARM64
                }
            });

        // Add containers from configuration and get primary container info
        var primaryContainer = AddContainersFromConfiguration(taskDefinition, ecsConfig, logGroup, context);

        // Import security group from ALB stack
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "ImportedEcsSecurityGroup",
            albOutputs.EcsSecurityGroupId);

        // Create ECS service with deployment-friendly settings
        var service = new FargateService(this, "TrialFinderService", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = context.Namer.EcsService(ResourcePurpose.Web),
            TaskDefinition = taskDefinition,
            DesiredCount = 1,
            MinHealthyPercent = 0, // Allow all tasks to be replaced during deployment
            MaxHealthyPercent = 200,
            AssignPublicIp = false,
            SecurityGroups = new[] { ecsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            EnableExecuteCommand = true
        });

        // Import target group from ALB stack and attach service
        var targetGroup = ApplicationTargetGroup.FromTargetGroupAttributes(this,
            "ImportedTargetGroup", new TargetGroupAttributes
            {
                TargetGroupArn = albOutputs.TargetGroupArn,
                LoadBalancerArns
                    = albOutputs
                        .TargetGroupArn // This will be overridden by the actual target group
            });

        // Register ECS service with target group using explicit container and port
        service.AttachToApplicationTargetGroup(targetGroup);
    }

    /// <summary>
    /// Add containers from configuration with conditional logic
    /// </summary>
    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        EcsTaskConfiguration ecsConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        // Check if deployTestContainer flag is enabled
        if (ecsConfig.DeployTestContainer)
        {
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("placeHolder", 8080);
        }

        var containerDefinitions = ecsConfig.TaskDefinition?.ContainerDefinitions;
        if (containerDefinitions == null || containerDefinitions.Count == 0)
        {
            // Fallback to default nginx container if no configuration provided
            AddDefaultContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("trial-finder-v2", 8080);
        }

        ContainerInfo? primaryContainer = null;

        foreach (var containerConfig in containerDefinitions)
        {
            // Skip containers marked with skip: true
            if (containerConfig.Skip == true)
            {
                continue;
            }

            var containerName = containerConfig.Name ?? "default-container";
            var containerPort = GetContainerPort(containerConfig, containerName);
            
            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, context);

            // Use the first non-skipped container as the primary container for load balancing
            if (primaryContainer == null)
            {
                primaryContainer = new ContainerInfo(containerName, containerPort);
            }
        }

        // If no containers were processed, fall back to default
        if (primaryContainer == null)
        {
            AddDefaultContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("trial-finder-v2", 8080);
        }

        return primaryContainer;
    }

    /// <summary>
    /// Add a container based on configuration with comprehensive defaults
    /// </summary>
    private void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        var containerName = containerConfig.Name ?? "default-container";
        var image = containerConfig.Image ?? GetDefaultImage(containerName);

        var containerOptions = new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry(image),
            Essential = containerConfig.Essential ?? GetDefaultEssential(containerName),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = containerName
            })
        };

        // Add port mappings with defaults
        containerOptions.PortMappings = GetPortMappings(containerConfig, containerName);

        // Add environment variables with defaults
        containerOptions.Environment
            = GetEnvironmentVariables(containerConfig, context, containerName);

        // Add health check with defaults
        var healthCheck = GetHealthCheck(containerConfig, containerName);
        if (healthCheck != null)
        {
            containerOptions.HealthCheck = healthCheck;
        }

        // Add CPU allocation if specified
        if (containerConfig.Cpu.HasValue && containerConfig.Cpu.Value > 0)
        {
            containerOptions.Cpu = containerConfig.Cpu.Value;
        }

        // Add secrets from Secrets Manager
        containerOptions.Secrets = GetContainerSecrets(context);

        taskDefinition.AddContainer(containerName, containerOptions);
    }

    /// <summary>
    /// Get default container image based on container name
    /// </summary>
    private string GetDefaultImage(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => "nginx:latest",
            "doc-nlp-service-web" =>
                $"{_context.Environment.AccountId}.dkr.ecr.us-east-2.amazonaws.com/thirdopinion/doc-nlp-service:latest",
            _ => "nginx:latest"
        };
    }

    /// <summary>
    /// Get default essential setting based on container name
    /// </summary>
    private bool GetDefaultEssential(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => true,
            "doc-nlp-service-web" => false,
            _ => true
        };
    }

    /// <summary>
    /// Get port mappings with defaults
    /// </summary>
    private Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            return containerConfig.PortMappings
                .Select(pm => new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = pm.ContainerPort ?? GetDefaultPort(containerName)
                })
                .ToArray();
        }

        // Default port mapping for all containers
        return new[]
        {
            new Amazon.CDK.AWS.ECS.PortMapping
            {
                ContainerPort = GetDefaultPort(containerName)
            }
        };
    }

    /// <summary>
    /// Get default port based on container name
    /// </summary>
    private int GetDefaultPort(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => 8080,
            "doc-nlp-service-web" => 8080,
            _ => 8080
        };
    }

    /// <summary>
    /// Get environment variables with defaults
    /// </summary>
    private Dictionary<string, string> GetEnvironmentVariables(
        ContainerDefinitionConfig containerConfig,
        DeploymentContext context,
        string containerName)
    {
        var environmentVars = new Dictionary<string, string>();

        // Start with default environment variables
        var defaults = CreateDefaultEnvironmentVariables(context);
        foreach (var kvp in defaults)
        {
            environmentVars[kvp.Key] = kvp.Value;
        }

        // Add container-specific defaults
        var containerDefaults = GetContainerSpecificEnvironmentDefaults(containerName, context);
        foreach (var kvp in containerDefaults)
        {
            environmentVars[kvp.Key] = kvp.Value;
        }

        // Override with configuration-specified environment variables
        if (containerConfig.Environment?.Count > 0)
        {
            foreach (var env in containerConfig.Environment.Where(e =>
                         !string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.Value)))
            {
                environmentVars[env.Name!] = env.Value!;
            }
        }

        return environmentVars;
    }

    /// <summary>
    /// Get container-specific default environment variables
    /// </summary>
    private Dictionary<string, string> GetContainerSpecificEnvironmentDefaults(string containerName,
        DeploymentContext context)
    {
        return containerName.ToLowerInvariant() switch
        {
            "doc-nlp-service-web" => new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name)
            },
            _ => new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Map deployment environment to ASP.NET Core environment
    /// </summary>
    private string GetAspNetCoreEnvironment(string environmentName)
    {
        return environmentName.ToLowerInvariant() switch
        {
            "development" => "Development",
            "staging" => "Staging",
            "production" => "Production",
            "integration" => "Integration",
            _ => "Development"
        };
    }

    /// <summary>
    /// Get health check with defaults
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck? GetHealthCheck(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        if (containerConfig.HealthCheck?.Command?.Count > 0)
        {
            return new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = containerConfig.HealthCheck.Command.ToArray(),
                Interval = Duration.Seconds(containerConfig.HealthCheck.Interval ??
                                            GetDefaultHealthCheckInterval(containerName)),
                Timeout = Duration.Seconds(containerConfig.HealthCheck.Timeout ??
                                           GetDefaultHealthCheckTimeout(containerName)),
                Retries = containerConfig.HealthCheck.Retries ??
                          GetDefaultHealthCheckRetries(containerName),
                StartPeriod = Duration.Seconds(containerConfig.HealthCheck.StartPeriod ??
                                               GetDefaultHealthCheckStartPeriod(containerName))
            };
        }

        // Return default health check for specific containers
        var defaultCommand = GetDefaultHealthCheckCommand(containerName);
        if (defaultCommand.Length > 0)
        {
            return new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = defaultCommand,
                Interval = Duration.Seconds(GetDefaultHealthCheckInterval(containerName)),
                Timeout = Duration.Seconds(GetDefaultHealthCheckTimeout(containerName)),
                Retries = GetDefaultHealthCheckRetries(containerName),
                StartPeriod = Duration.Seconds(GetDefaultHealthCheckStartPeriod(containerName))
            };
        }

        return null;
    }

    /// <summary>
    /// Get default health check command for container
    /// </summary>
    private string[] GetDefaultHealthCheckCommand(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => new[] { "CMD-SHELL", "curl -f http://localhost:8080/ || exit 1" },
            "doc-nlp-service-web" => new[]
                { "CMD-SHELL", "curl -f http://localhost:8080/ || exit 1" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Get default health check interval for container
    /// </summary>
    private int GetDefaultHealthCheckInterval(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => 30,
            "doc-nlp-service-web" => 20,
            _ => 30
        };
    }

    /// <summary>
    /// Get default health check timeout for container
    /// </summary>
    private int GetDefaultHealthCheckTimeout(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => 10,
            "doc-nlp-service-web" => 30,
            _ => 10
        };
    }

    /// <summary>
    /// Get default health check retries for container
    /// </summary>
    private int GetDefaultHealthCheckRetries(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => 3,
            "doc-nlp-service-web" => 5,
            _ => 3
        };
    }

    /// <summary>
    /// Get default health check start period for container
    /// </summary>
    private int GetDefaultHealthCheckStartPeriod(string containerName)
    {
        return containerName.ToLowerInvariant() switch
        {
            "trial-finder-v2" => 120,
            "doc-nlp-service-web" => 120,
            _ => 120
        };
    }

    /// <summary>
    /// Add default nginx container when no configuration is provided
    /// </summary>
    private void AddDefaultContainer(FargateTaskDefinition taskDefinition,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        taskDefinition.AddContainer("trial-finder-v2", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("nginx:latest"),
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 8080
                }
            },
            Environment = CreateDefaultEnvironmentVariables(context),
            Secrets = GetContainerSecrets(context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "trial-finder"
            }),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "curl -f http://localhost:8080/ || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(10),
                Retries = 3,
                StartPeriod = Duration.Seconds(120)
            }
        });
    }

    /// <summary>
    /// Add placeholder container for testing deployments
    /// </summary>
    private void AddPlaceholderContainer(FargateTaskDefinition taskDefinition,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        // Import the ECR repository for the placeholder image
        var placeholderRepository = Repository.FromRepositoryName(this, "PlaceholderRepository",
            "thirdopinion/infra/deploy-placeholder");

        taskDefinition.AddContainer("placeHolder", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(placeholderRepository, "latest"),
            Essential = true,
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 8080
                }
            },
            Environment = CreateDefaultEnvironmentVariables(context),
            Secrets = GetContainerSecrets(context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "placeHolder"
            }),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[]
                {
                    "CMD-SHELL",
                    "wget --no-verbose --tries=1 --spider http://localhost:8080/ || exit 1"
                },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            }
        });
    }

    /// <summary>
    /// Create default environment variables
    /// </summary>
    private Dictionary<string, string> CreateDefaultEnvironmentVariables(DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/"
        };
    }

    /// <summary>
    /// Create IAM role for ECS tasks
    /// </summary>
    private IRole CreateTaskRole()
    {
        var taskRole = new Role(this, "TrialFinderTaskRole", new RoleProps
        {
            RoleName = _context.Namer.IamRole(IamPurpose.EcsTask),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add permissions for Session Manager (ECS Exec)
        taskRole.AddManagedPolicy(
            ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore"));

        // Add SSM permissions for ECS Exec
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ssmmessages:CreateControlChannel",
                "ssmmessages:CreateDataChannel",
                "ssmmessages:OpenControlChannel",
                "ssmmessages:OpenDataChannel"
            },
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for environment-specific secrets
        AddSecretsManagerPermissions(taskRole);

        return taskRole;
    }

    /// <summary>
    /// Create IAM role for ECS execution
    /// </summary>
    private IRole CreateExecutionRole(ILogGroup logGroup)
    {
        var executionRole = new Role(this, "TrialFinderExecutionRole", new RoleProps
        {
            RoleName = _context.Namer.IamRole(IamPurpose.EcsExecution),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add CloudWatch Logs permissions
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogStream",
                "logs:PutLogEvents"
            },
            Resources = new[] { logGroup.LogGroupArn }
        }));

        // Add ECR permissions for pulling images (including public ECR for nginx)
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecr:GetAuthorizationToken",
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage"
            },
            Resources = new[] { "*" }
        }));

        // Add permissions for ECR public (for nginx:latest)
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecr-public:GetAuthorizationToken",
                "sts:GetServiceBearerToken"
            },
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for container startup
        AddSecretsManagerPermissions(executionRole);

        return executionRole;
    }

    /// <summary>
    /// Get container port for load balancer registration
    /// </summary>
    private int GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        // Use the first port mapping if available
        if (containerConfig.PortMappings?.Count > 0)
        {
            return containerConfig.PortMappings[0].ContainerPort ?? GetDefaultPort(containerName);
        }

        // Fall back to default port
        return GetDefaultPort(containerName);
    }

    /// <summary>
    /// Helper class to hold container information for load balancer registration
    /// </summary>
    private class ContainerInfo
    {
        public string ContainerName { get; }
        public int ContainerPort { get; }

        public ContainerInfo(string containerName, int containerPort)
        {
            ContainerName = containerName;
            ContainerPort = containerPort;
        }
    }

    /// <summary>
    /// Add Secrets Manager permissions to IAM role with environment-specific scoping
    /// </summary>
    private void AddSecretsManagerPermissions(IRole role)
    {
        // Cast to Role to access AddToPolicy method
        var concreteRole = role as Role;
        if (concreteRole == null) return;

        // Environment-specific secret path pattern
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var secretResourceArn = $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/{_context.Application.Name.ToLowerInvariant()}/*";
        
        // Add Secrets Manager permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = new[] { secretResourceArn },
            Conditions = new Dictionary<string, object>
            {
                ["StringLike"] = new Dictionary<string, string>
                {
                    ["secretsmanager:SecretId"] = $"/{environmentPrefix}/{_context.Application.Name.ToLowerInvariant()}/*"
                }
            }
        }));

        // Add KMS permissions for secret decryption using default AWS managed key
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "kms:Decrypt",
                "kms:DescribeKey"
            },
            Resources = new[] { $"arn:aws:kms:{_context.Environment.Region}:{_context.Environment.AccountId}:key/alias/aws/secretsmanager" }
        }));

        // Add explicit deny for cross-environment access
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.DENY,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringNotLike"] = new Dictionary<string, string>
                {
                    ["secretsmanager:SecretId"] = $"/{environmentPrefix}/{_context.Application.Name.ToLowerInvariant()}/*"
                }
            }
        }));
    }

    /// <summary>
    /// Get container secrets from Secrets Manager
    /// </summary>
    private Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(DeploymentContext context)
    {
        var environmentPrefix = context.Environment.Name.ToLowerInvariant();
        var applicationName = context.Application.Name.ToLowerInvariant();
        
        return new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>
        {
            ["DB_CONNECTION_STRING"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(
                Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "DbSecretRef", 
                    $"/{environmentPrefix}/{applicationName}/database-connection")),
            ["API_KEY"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(
                Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "ApiKeySecretRef", 
                    $"/{environmentPrefix}/{applicationName}/api-key")),
            ["SERVICE_CLIENT_ID"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(
                Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "ServiceCredentialsSecretRef", 
                    $"/{environmentPrefix}/{applicationName}/service-credentials"), "clientId"),
            ["SERVICE_CLIENT_SECRET"] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(
                Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, "ServiceCredentialsSecretRef2", 
                    $"/{environmentPrefix}/{applicationName}/service-credentials"), "clientSecret")
        };
    }

    /// <summary>
    /// Create test secrets in Secrets Manager for the current environment
    /// </summary>
    private void CreateTestSecrets()
    {
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var applicationName = _context.Application.Name.ToLowerInvariant();
        
        // Create database connection secret
        var dbSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, "DatabaseConnectionSecret", new SecretProps
        {
            SecretName = $"/{environmentPrefix}/{applicationName}/database-connection",
            Description = $"Database connection string for {_context.Application.Name} in {_context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"username\":\"trialfinderuser\"}",
                GenerateStringKey = "password",
                PasswordLength = 32,
                ExcludeCharacters = "\"@/\\"
            }
        });

        // Create API key secret
        var apiKeySecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, "ApiKeySecret", new SecretProps
        {
            SecretName = $"/{environmentPrefix}/{applicationName}/api-key",
            Description = $"API key for {_context.Application.Name} in {_context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"service\":\"trial-finder\"}",
                GenerateStringKey = "apiKey",
                PasswordLength = 64,
                ExcludeCharacters = "\"@/\\"
            }
        });

        // Create service credentials secret
        var serviceCredentialsSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, "ServiceCredentialsSecret", new SecretProps
        {
            SecretName = $"/{environmentPrefix}/{applicationName}/service-credentials",
            Description = $"Service credentials for {_context.Application.Name} in {_context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"clientId\":\"trial-finder-client\"}",
                GenerateStringKey = "clientSecret",
                PasswordLength = 48,
                ExcludeCharacters = "\"@/\\"
            }
        });

        // Export secret ARNs for reference
        new CfnOutput(this, "DatabaseSecretArn", new CfnOutputProps
        {
            Value = dbSecret.SecretArn,
            Description = "Database connection secret ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-db-secret-arn"
        });

        new CfnOutput(this, "ApiKeySecretArn", new CfnOutputProps
        {
            Value = apiKeySecret.SecretArn,
            Description = "API key secret ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-api-key-secret-arn"
        });

        new CfnOutput(this, "ServiceCredentialsSecretArn", new CfnOutputProps
        {
            Value = serviceCredentialsSecret.SecretArn,
            Description = "Service credentials secret ARN",
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-service-credentials-secret-arn"
        });
    }

    /// <summary>
    /// Helper class to hold ALB stack outputs
    /// </summary>
    private class AlbStackOutputs
    {
        public string TargetGroupArn { get; set; } = "";
        public string EcsSecurityGroupId { get; set; } = "";
    }
}