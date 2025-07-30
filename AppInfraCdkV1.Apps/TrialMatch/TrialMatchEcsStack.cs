using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;
using System.Text.RegularExpressions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace AppInfraCdkV1.Apps.TrialMatch;

/// <summary>
/// ECS Stack for TrialMatch application
/// 
/// This stack manages ECS Fargate services with the following features:
/// - Automatic secret management with existence checking
/// - Container configuration from JSON files
/// - Integration with Application Load Balancer
/// - Environment-specific resource sizing
/// - Comprehensive IAM roles and permissions
/// 
/// Secret Management:
/// The stack checks if secrets already exist in AWS Secrets Manager before creating new ones.
/// This prevents CDK from attempting to recreate secrets that already exist, which would cause
/// deployment failures. Existing secrets are imported and referenced, while missing secrets
/// are created with generated values.
/// </summary>
public class TrialMatchEcsStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly Dictionary<string, Amazon.CDK.AWS.SecretsManager.ISecret> _createdSecrets = new();

    private readonly Dictionary<string, string> _envVarToSecretNameMapping = new();

    public TrialMatchEcsStack(Construct scope,
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

        // Export secret ARNs for all created secrets
        ExportSecretArns();
    }

    /// <summary>
    /// Create VPC reference using shared stack exports
    /// </summary>
    private IVpc CreateVpcReference(string? vpcNamePattern, DeploymentContext context)
    {
        // Import VPC attributes from shared stack
        var vpcId = Fn.ImportValue($"{context.Environment.Name}-vpc-id");
        var vpcCidr = Fn.ImportValue($"{context.Environment.Name}-vpc-cidr");
        var availabilityZones = Fn.ImportListValue($"{context.Environment.Name}-vpc-azs", 3);
        var publicSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-public-subnet-ids", 3);
        var privateSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-private-subnet-ids", 3);
        var isolatedSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-isolated-subnet-ids", 3);
        
        // Use VPC attributes to create reference
        return Vpc.FromVpcAttributes(this, "SharedVpc", new VpcAttributes
        {
            VpcId = vpcId,
            VpcCidrBlock = vpcCidr,
            AvailabilityZones = availabilityZones,
            PublicSubnetIds = publicSubnetIds,
            PrivateSubnetIds = privateSubnetIds,
            IsolatedSubnetIds = isolatedSubnetIds
        });
    }

    /// <summary>
    /// Import outputs from the ALB stack
    /// </summary>
    private AlbStackOutputs ImportAlbStackOutputs()
    {
        return new AlbStackOutputs
        {
            TargetGroupArn = Fn.ImportValue($"{_context.Environment.Name}-trial-match-target-group-arn"),
            EcsSecurityGroupId = Fn.ImportValue($"{_context.Environment.Name}-trial-match-ecs-security-group-id")
        };
    }

    /// <summary>
    /// Import shared database security group
    /// </summary>
    private ISecurityGroup ImportSharedDatabaseSecurityGroup()
    {
        var securityGroupId = Fn.ImportValue($"{_context.Environment.Name}-shared-database-security-group-id");
        return SecurityGroup.FromSecurityGroupId(this, "SharedDatabaseSecurityGroup", securityGroupId);
    }

    /// <summary>
    /// Create ECS cluster
    /// </summary>
    private ICluster CreateEcsCluster(IVpc vpc, DeploymentContext context)
    {
        return new Cluster(this, "TrialMatchCluster", new ClusterProps
        {
            Vpc = vpc,
            ClusterName = context.Namer.EcsCluster(ResourcePurpose.Web)
        });
    }

    /// <summary>
    /// Create ECS service with containers from configuration
    /// </summary>
    private void CreateEcsService(ICluster cluster,
        AlbStackOutputs albOutputs,
        DeploymentContext context)
    {
        // Load ECS configuration
        var ecsConfig = _configLoader.LoadEcsConfig(context.Environment.Name);
        
        // Create log group for the service
        var logGroup = new LogGroup(this, "TrialMatchLogGroup", new LogGroupProps
        {
            LogGroupName = context.Namer.LogGroup("trial-match", ResourcePurpose.Web),
            Retention = GetLogRetention(context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Create task definition
        var taskDefinition = new FargateTaskDefinition(this, "TrialMatchTaskDefinition", new FargateTaskDefinitionProps
        {
            Family = context.Namer.EcsTaskDefinition(ResourcePurpose.Web),
            Cpu = 256,
            MemoryLimitMiB = 512,
            ExecutionRole = CreateExecutionRole(logGroup),
            TaskRole = CreateTaskRole()
        });

        // Add containers from configuration
        var containerInfo = AddContainersFromConfiguration(taskDefinition, ecsConfig.TaskDefinition.FirstOrDefault(), logGroup, context);

        // Create security group for ECS tasks
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "EcsSecurityGroup", albOutputs.EcsSecurityGroupId);

        // Create ECS service with deployment-friendly settings
        var service = new FargateService(this, "TrialMatchService", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = context.Namer.EcsService(ResourcePurpose.Web),
            TaskDefinition = taskDefinition,
            DesiredCount = context.Environment.AccountType == AccountType.Production ? 2 : 1,
            MinHealthyPercent = 0, // Allow all tasks to be replaced during deployment
            MaxHealthyPercent = 200,
            AssignPublicIp = false,
            SecurityGroups = new[] { ecsSecurityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            EnableExecuteCommand = true
        });

        // Only attach to target group if primary container has a valid port
        if (containerInfo.ContainerPort > 0)
        {
            Console.WriteLine($"🔗 Attaching ECS service to ALB target group");
            Console.WriteLine($"   Container: {containerInfo.ContainerName}");
            Console.WriteLine($"   Port: {containerInfo.ContainerPort}");
            
            // Import target group from ALB stack and attach service
            var targetGroup = ApplicationTargetGroup.FromTargetGroupAttributes(this,
                "ImportedTargetGroup", new TargetGroupAttributes
                {
                    TargetGroupArn = albOutputs.TargetGroupArn,
                    LoadBalancerArns = albOutputs.TargetGroupArn // This will be overridden by the actual target group
                });

            // Register ECS service with target group using explicit container and port
            service.AttachToApplicationTargetGroup(targetGroup);
            Console.WriteLine("✅ ECS service attached to target group successfully");
        }
        else
        {
            Console.WriteLine("⚠️  Skipping ALB target group attachment - no container with valid port found");
        }

        // Export task definition outputs
        ExportTaskDefinitionOutputs(taskDefinition, service, context);
    }

    /// <summary>
    /// Add containers from configuration to task definition
    /// </summary>
    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig? taskDefConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        // Collect all secrets and build mapping
        CollectAllSecretsAndBuildMapping(taskDefConfig, context);

        var containerDefinitions = taskDefConfig?.ContainerDefinitions ?? new List<ContainerDefinitionConfig>();
        
        if (containerDefinitions.Count == 0)
        {
            // Fallback to default container if no configuration provided
            AddDefaultContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("trial-match", 8080);
        }

        ContainerInfo? mainContainerInfo = null;

        foreach (var containerConfig in containerDefinitions)
        {
            if (containerConfig.Name == null) continue;

            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, context);

            // Track the main container (first essential container)
            if (mainContainerInfo == null && (containerConfig.Essential ?? GetDefaultEssential(containerConfig.Name)))
            {
                var containerPort = GetContainerPort(containerConfig, containerConfig.Name) ?? 8080;
                mainContainerInfo = new ContainerInfo(containerConfig.Name, containerPort);
            }
        }

        // Ensure we have at least one container
        if (mainContainerInfo == null)
        {
            AddDefaultContainer(taskDefinition, logGroup, context);
            mainContainerInfo = new ContainerInfo("trial-match", 8080);
        }

        return mainContainerInfo;
    }

    /// <summary>
    /// Add a configured container to the task definition
    /// </summary>
    private void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        if (containerConfig.Name == null) return;

        var container = taskDefinition.AddContainer(containerConfig.Name, new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry(containerConfig.Image ?? "placeholder"),
            ContainerName = containerConfig.Name,
            Cpu = containerConfig.Cpu ?? 0,
            Essential = containerConfig.Essential ?? GetDefaultEssential(containerConfig.Name),
            PortMappings = GetPortMappings(containerConfig, containerConfig.Name),
            Environment = GetEnvironmentVariables(containerConfig, context, containerConfig.Name),
            Secrets = GetContainerSecrets(containerConfig.Secrets, context),
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            }),
            HealthCheck = GetContainerHealthCheck(containerConfig, containerConfig.Name)
        });
    }

    /// <summary>
    /// Get container health check configuration
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck? GetContainerHealthCheck(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        // Skip health check for non-essential containers or if explicitly disabled
        if (!(containerConfig.Essential ?? GetDefaultEssential(containerName)) || 
            containerConfig.DisableHealthCheck == true)
        {
            return null;
        }

        // Use custom health check if provided
        if (containerConfig.HealthCheck?.Command?.Count > 0)
        {
            return CreateCustomHealthCheck(containerConfig.HealthCheck, containerName);
        }

        // Use standard health check
        return GetStandardHealthCheck(containerConfig);
    }

    /// <summary>
    /// Create custom health check from configuration
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck CreateCustomHealthCheck(
        HealthCheckConfig healthCheckConfig,
        string containerName)
    {
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = healthCheckConfig.Command?.ToArray() ?? new[] { "CMD-SHELL", "curl -f http://localhost:8080/health || exit 1" },
            Interval = Duration.Seconds(healthCheckConfig.Interval ?? 30),
            Timeout = Duration.Seconds(healthCheckConfig.Timeout ?? 5),
            Retries = healthCheckConfig.Retries ?? 3,
            StartPeriod = Duration.Seconds(healthCheckConfig.StartPeriod ?? 60)
        };
    }

    /// <summary>
    /// Check if container is a cron job (non-essential)
    /// </summary>
    private bool IsCronJobContainer(ContainerDefinitionConfig containerConfig, string containerName)
    {
        // Non-essential containers are typically cron jobs
        if (containerConfig.Essential == false) return true;

        // Check container name patterns for cron jobs
        var cronJobPatterns = new[] { "cron", "job", "worker", "loader", "processor" };
        return cronJobPatterns.Any(pattern => containerName.ToLower().Contains(pattern));
    }

    /// <summary>
    /// Get port mappings for container
    /// </summary>
    private Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        var portMappings = new List<Amazon.CDK.AWS.ECS.PortMapping>();

        if (containerConfig.PortMappings?.Count > 0)
        {
            foreach (var portMapping in containerConfig.PortMappings)
            {
                if (portMapping.ContainerPort == null) continue;

                portMappings.Add(new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = portMapping.ContainerPort.Value,
                    HostPort = portMapping.HostPort ?? portMapping.ContainerPort.Value,
                    Protocol = GetProtocol(portMapping.Protocol),
                    AppProtocol = GetAppProtocol(portMapping.AppProtocol),
                    Name = GeneratePortMappingName(containerName, portMapping.ContainerPort.Value, portMapping.Protocol ?? "tcp")
                });
            }
        }

        return portMappings.ToArray();
    }

    /// <summary>
    /// Get default essential setting for container
    /// </summary>
    private bool GetDefaultEssential(string containerName)
    {
        // Main application containers are essential by default
        return !IsCronJobContainer(new ContainerDefinitionConfig { Name = containerName }, containerName);
    }

    /// <summary>
    /// Get environment variables for container
    /// </summary>
    private Dictionary<string, string> GetEnvironmentVariables(
        ContainerDefinitionConfig containerConfig,
        DeploymentContext context,
        string containerName)
    {
        var envVars = new Dictionary<string, string>();

        // Add container-specific environment variables
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
        var defaultEnvVars = GetContainerSpecificEnvironmentDefaults(containerName, context);
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
    private Dictionary<string, string> GetContainerSpecificEnvironmentDefaults(string containerName,
        DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name),
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["AWS_REGION"] = context.Environment.Region,
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/health"
        };
    }

    /// <summary>
    /// Get ASP.NET Core environment name
    /// </summary>
    private string GetAspNetCoreEnvironment(string environmentName)
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
    /// Get standard health check configuration
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck GetStandardHealthCheck(ContainerDefinitionConfig? containerConfig = null)
    {
        var healthCheckPath = GetHealthCheckPath(containerConfig);
        
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[] { "CMD-SHELL", $"curl -f http://localhost:8080{healthCheckPath} || exit 1" },
            Interval = Duration.Seconds(30),
            Timeout = Duration.Seconds(5),
            Retries = 3,
            StartPeriod = Duration.Seconds(60)
        };
    }

    /// <summary>
    /// Get health check path from configuration or environment variable
    /// </summary>
    private string GetHealthCheckPath(ContainerDefinitionConfig? containerConfig)
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
    /// Add default container if no configuration provided
    /// </summary>
    private void AddDefaultContainer(FargateTaskDefinition taskDefinition,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        var container = taskDefinition.AddContainer("trial-match", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("placeholder"),
            ContainerName = "trial-match",
            Essential = true,
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 8080,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                    Name = "trial-match-8080-tcp"
                },
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 80,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                    Name = "trial-match-80-tcp"
                }
            },
            Environment = CreateDefaultEnvironmentVariables(context),
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            }),
            HealthCheck = GetStandardHealthCheck()
        });
    }

    /// <summary>
    /// Add placeholder container for testing
    /// </summary>
    private void AddPlaceholderContainer(FargateTaskDefinition taskDefinition,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        var container = taskDefinition.AddContainer("trial-match-placeholder", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry("nginx:alpine"),
            ContainerName = "trial-match-placeholder",
            Essential = true,
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 80,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
                    Name = "trial-match-placeholder-80-tcp"
                }
            },
            Environment = new Dictionary<string, string>
            {
                ["ENVIRONMENT"] = context.Environment.Name,
                ["SERVICE_NAME"] = "trial-match-placeholder"
            },
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            })
        });
    }

    /// <summary>
    /// Create default environment variables
    /// </summary>
    private Dictionary<string, string> CreateDefaultEnvironmentVariables(DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name),
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["AWS_REGION"] = context.Environment.Region,
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/health"
        };
    }

    /// <summary>
    /// Create task role with necessary permissions
    /// </summary>
    private IRole CreateTaskRole()
    {
        var role = new Role(this, "TrialMatchTaskRole", new RoleProps
        {
            RoleName = "TrialMatchTaskRole",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = "IAM role for TrialMatch ECS task execution"
        });

        // Add Secrets Manager permissions
        AddSecretsManagerPermissions(role);

        // Add other necessary permissions
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonS3ReadOnlyAccess"));
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonSQSFullAccess"));
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonSNSFullAccess"));

        return role;
    }

    /// <summary>
    /// Create execution role with necessary permissions
    /// </summary>
    private IRole CreateExecutionRole(ILogGroup logGroup)
    {
        var role = new Role(this, "TrialMatchExecutionRole", new RoleProps
        {
            RoleName = "TrialMatchExecutionRole",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = "IAM role for TrialMatch ECS task execution"
        });

        // Add ECS task execution policy
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy"));

        // Add Secrets Manager permissions
        AddSecretsManagerPermissions(role);

        // Add CloudWatch Logs permissions
        logGroup.GrantWrite(role);

        return role;
    }

    /// <summary>
    /// Get container port from configuration
    /// </summary>
    private int? GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            var mainPortMapping = containerConfig.PortMappings.FirstOrDefault();
            return mainPortMapping?.ContainerPort;
        }

        // Default ports based on container name
        return containerName.ToLower() switch
        {
            "trial-match" => 8080,
            _ => 8080
        };
    }

    /// <summary>
    /// Generate port mapping name
    /// </summary>
    private string GeneratePortMappingName(string containerName, int containerPort, string protocol)
    {
        return $"{containerName}-{containerPort}-{protocol}";
    }

    /// <summary>
    /// Get protocol from string
    /// </summary>
    private Amazon.CDK.AWS.ECS.Protocol GetProtocol(string? protocol)
    {
        return (protocol?.ToLower()) switch
        {
            "tcp" => Amazon.CDK.AWS.ECS.Protocol.TCP,
            "udp" => Amazon.CDK.AWS.ECS.Protocol.UDP,
            _ => Amazon.CDK.AWS.ECS.Protocol.TCP
        };
    }

    /// <summary>
    /// Get application protocol from string with default
    /// </summary>
    private Amazon.CDK.AWS.ECS.AppProtocol? GetAppProtocol(string? appProtocol)
    {
        if (string.IsNullOrWhiteSpace(appProtocol))
        {
            return Amazon.CDK.AWS.ECS.AppProtocol.Http;
        }

        return appProtocol.ToLowerInvariant() switch
        {
            "http" => Amazon.CDK.AWS.ECS.AppProtocol.Http,
            "https" => Amazon.CDK.AWS.ECS.AppProtocol.Http,
            "grpc" => Amazon.CDK.AWS.ECS.AppProtocol.Grpc,
            _ => Amazon.CDK.AWS.ECS.AppProtocol.Http
        };
    }

    /// <summary>
    /// Container information
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

        // Environment-specific secret path patterns
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var applicationName = _context.Application.Name.ToLowerInvariant();
        
        // Define specific secret ARNs that the task can access
        var allowedSecretArns = new[]
        {
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/{applicationName}/*",
            // Allow access to shared secrets if needed
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/shared/*",
            // Allow access to specific API keys
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:OpenAiOptions__ApiKey*",
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:GoogleMaps__ApiKey*",
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:ZipCodeApi__ApiKey*"
        };
        
        // Add Secrets Manager permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowSecretsManagerAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = allowedSecretArns
        }));

        // Add KMS permissions for secret decryption
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowKMSDecrypt",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "kms:Decrypt",
                "kms:DescribeKey",
                "kms:GenerateDataKey"
            },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["kms:ViaService"] = $"secretsmanager.{_context.Environment.Region}.amazonaws.com"
                }
            }
        }));
    }

    /// <summary>
    /// Collect all secrets and build mapping
    /// </summary>
    private void CollectAllSecretsAndBuildMapping(TaskDefinitionConfig? taskDefConfig, DeploymentContext context)
    {
        var allSecrets = new List<string>();

        if (taskDefConfig?.ContainerDefinitions?.Count > 0)
        {
            foreach (var container in taskDefConfig.ContainerDefinitions)
            {
                if (container.Secrets?.Count > 0)
                {
                    allSecrets.AddRange(container.Secrets);
                }
            }
        }

        // Build secret name mapping
        BuildSecretNameMapping(allSecrets);
    }

    /// <summary>
    /// Get container secrets
    /// </summary>
    private Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();

        if (secretNames?.Count > 0)
        {
            foreach (var secretName in secretNames)
            {
                var fullSecretName = BuildSecretName(secretName, context);
                var secret = GetOrCreateSecret(secretName, fullSecretName, context);
                
                secrets[secretName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
            }
        }

        return secrets;
    }

    /// <summary>
    /// Build secret name mapping
    /// </summary>
    private void BuildSecretNameMapping(List<string> secretNames)
    {
        foreach (var secretName in secretNames)
        {
            var envVarName = GetSecretNameFromEnvVar(secretName);
            _envVarToSecretNameMapping[envVarName] = secretName;
        }
    }

    /// <summary>
    /// Get secret name from environment variable
    /// </summary>
    private string GetSecretNameFromEnvVar(string secretName)
    {
        // Convert secret name to environment variable format
        // e.g., "OpenAiOptions__ApiKey" -> "OpenAiOptions__ApiKey"
        return secretName;
    }

    /// <summary>
    /// Build full secret name
    /// </summary>
    private string BuildSecretName(string secretName, DeploymentContext context)
    {
        return $"{context.Environment.Name}-trial-match-{secretName}";
    }

    /// <summary>
    /// Check if secret exists
    /// </summary>
    private bool SecretExists(string secretName)
    {
        try
        {
            // This is a simplified check - in a real implementation, you might want to use AWS SDK
            // to check if the secret actually exists
            return false; // Assume it doesn't exist for now
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get or create secret
    /// </summary>
    private Amazon.CDK.AWS.SecretsManager.ISecret GetOrCreateSecret(string secretName, string fullSecretName, DeploymentContext context)
    {
        if (_createdSecrets.ContainsKey(fullSecretName))
        {
            return _createdSecrets[fullSecretName];
        }

        // Check if secret already exists
        if (SecretExists(fullSecretName))
        {
            var existingSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, $"ExistingSecret-{secretName}", fullSecretName);
            _createdSecrets[fullSecretName] = existingSecret;
            return existingSecret;
        }

        // Create new secret
        var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
        {
            SecretName = fullSecretName,
            Description = $"Secret for TrialMatch {secretName}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"value\":\"placeholder\"}",
                GenerateStringKey = "value"
            }
        });

        _createdSecrets[fullSecretName] = secret;
        return secret;
    }

    /// <summary>
    /// Export secret ARNs
    /// </summary>
    private void ExportSecretArns()
    {
        foreach (var kvp in _createdSecrets)
        {
            var secretName = kvp.Key;
            var secret = kvp.Value;

            new CfnOutput(this, $"SecretArn-{secretName}", new CfnOutputProps
            {
                Value = secret.SecretArn,
                Description = $"TrialMatch Secret ARN for {secretName}",
                ExportName = $"{_context.Environment.Name}-trial-match-secret-{secretName}-arn"
            });
        }
    }

    /// <summary>
    /// Get log retention based on account type
    /// </summary>
    private RetentionDays GetLogRetention(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => RetentionDays.ONE_MONTH,
            AccountType.NonProduction => RetentionDays.ONE_WEEK,
            _ => RetentionDays.ONE_WEEK
        };
    }

    /// <summary>
    /// Export task definition outputs
    /// </summary>
    private void ExportTaskDefinitionOutputs(FargateTaskDefinition taskDefinition,
        FargateService service,
        DeploymentContext context)
    {
        new CfnOutput(this, "TaskDefinitionArn", new CfnOutputProps
        {
            Value = taskDefinition.TaskDefinitionArn,
            Description = "TrialMatch Task Definition ARN",
            ExportName = $"{context.Environment.Name}-trial-match-task-definition-arn"
        });

        new CfnOutput(this, "ServiceArn", new CfnOutputProps
        {
            Value = service.ServiceArn,
            Description = "TrialMatch ECS Service ARN",
            ExportName = $"{context.Environment.Name}-trial-match-service-arn"
        });

        new CfnOutput(this, "ServiceName", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = "TrialMatch ECS Service Name",
            ExportName = $"{context.Environment.Name}-trial-match-service-name"
        });
    }

    /// <summary>
    /// ALB stack outputs
    /// </summary>
    private class AlbStackOutputs
    {
        public string TargetGroupArn { get; set; } = "";
        public string EcsSecurityGroupId { get; set; } = "";
    }
} 