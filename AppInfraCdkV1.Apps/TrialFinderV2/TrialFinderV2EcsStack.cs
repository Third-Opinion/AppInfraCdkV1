using System.Linq;
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
    private readonly Dictionary<string, Amazon.CDK.AWS.SecretsManager.Secret> _createdSecrets = new();

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

        // Create log group for ECS tasks with appropriate retention
        var logGroup = new LogGroup(this, "TrialFinderLogGroup", new LogGroupProps
        {
            LogGroupName = context.Namer.LogGroup("trial-finder", ResourcePurpose.Web),
            Retention = GetLogRetention(context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Use the first task definition from config or fallback to default naming
        TaskDefinitionConfig? firstTaskDef = ecsConfig.TaskDefinition?.FirstOrDefault();
        var taskDefinitionName = firstTaskDef?.TaskDefinitionName != null
            ? context.Namer.EcsTaskDefinition(firstTaskDef.TaskDefinitionName)
            : context.Namer.EcsTaskDefinition("web");

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
        var primaryContainer = AddContainersFromConfiguration(taskDefinition, firstTaskDef, logGroup, context);

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

        // Only attach to target group if primary container has a valid port
        if (primaryContainer.ContainerPort > 0)
        {
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
        
        // Export task definition ARN and family name for GitHub Actions
        ExportTaskDefinitionOutputs(taskDefinition, service, context);
    }

    /// <summary>
    /// Add containers from configuration with conditional logic
    /// </summary>
    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig? taskDefConfig,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        var containerDefinitions = taskDefConfig?.ContainerDefinitions;
        if (containerDefinitions == null || containerDefinitions.Count == 0)
        {
            // Fallback to placeholder container if no configuration provided
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("app", 8080);
        }

        ContainerInfo? primaryContainer = null;
        var containersProcessed = 0;

        foreach (var containerConfig in containerDefinitions)
        {
            if (string.IsNullOrWhiteSpace(containerConfig.Name))
            {
                throw new InvalidOperationException("Container name is required in configuration");
            }
            var containerName = containerConfig.Name;
            var containerPort = GetContainerPort(containerConfig, containerName);
            
            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, context);
            containersProcessed++;

            // Use the first container with ports as the primary container for load balancing
            if (primaryContainer == null && containerPort.HasValue)
            {
                primaryContainer = new ContainerInfo(containerName, containerPort.Value);
            }
        }

        // If no containers were processed at all, fall back to placeholder
        if (containersProcessed == 0)
        {
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("app", 8080);
        }

        // If containers were processed but none have ports, we can't attach to load balancer
        // Return null to indicate no primary container available
        if (primaryContainer == null)
        {
            // Use the first container name for reference, even without ports
            var firstContainerName = containerDefinitions.First().Name ?? "default-container";
            return new ContainerInfo(firstContainerName, 0); // Port 0 indicates no port mapping
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
        
        // Determine if we should use placeholder or specified image
        ContainerImage containerImage;
        Dictionary<string, string> environmentVars;
        
        if (string.IsNullOrWhiteSpace(containerConfig.Image) || containerConfig.Image == "placeholder")
        {
            // Use placeholder repository
            var placeholderRepository = Repository.FromRepositoryName(this, $"PlaceholderRepository-{containerName}",
                "thirdopinion/infra/deploy-placeholder");
            containerImage = ContainerImage.FromEcrRepository(placeholderRepository, "latest");
            
            // Add placeholder-specific environment variables
            environmentVars = CreateDefaultEnvironmentVariables(context);
            environmentVars["DEPLOYMENT_TYPE"] = "placeholder";
            environmentVars["MANAGED_BY"] = "CDK";
            environmentVars["APP_NAME"] = context.Application.Name;
            environmentVars["APP_VERSION"] = "placeholder";
        }
        else
        {
            // Use specified image
            containerImage = ContainerImage.FromRegistry(containerConfig.Image);
            environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
        }

        var containerOptions = new ContainerDefinitionOptions
        {
            Image = containerImage,
            Essential = containerConfig.Essential ?? GetDefaultEssential(containerName),
            Environment = environmentVars,
            Secrets = GetContainerSecrets(containerConfig.Secrets, context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = containerName
            })
        };

        // Add port mappings only if they exist in configuration
        var portMappings = GetPortMappings(containerConfig, containerName);
        if (portMappings.Length > 0)
        {
            containerOptions.PortMappings = portMappings;
        }

        // Add standard health check for all containers
        containerOptions.HealthCheck = GetStandardHealthCheck();

        // Add CPU allocation if specified
        if (containerConfig.Cpu.HasValue && containerConfig.Cpu.Value > 0)
        {
            containerOptions.Cpu = containerConfig.Cpu.Value;
        }

        taskDefinition.AddContainer(containerName, containerOptions);
    }




    /// <summary>
    /// Get port mappings from JSON configuration with generated names
    /// </summary>
    private Amazon.CDK.AWS.ECS.PortMapping[] GetPortMappings(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            return containerConfig.PortMappings
                .Where(pm => pm.ContainerPort.HasValue && !string.IsNullOrWhiteSpace(pm.Protocol)) // Require both port and protocol
                .Select(pm => new Amazon.CDK.AWS.ECS.PortMapping
                {
                    Name = GeneratePortMappingName(containerName, pm.ContainerPort!.Value, pm.Protocol!),
                    ContainerPort = pm.ContainerPort!.Value,
                    HostPort = pm.HostPort,
                    Protocol = GetProtocol(pm.Protocol),
                    AppProtocol = GetAppProtocol(pm.AppProtocol)
                })
                .ToArray();
        }

        // Return empty array if no port mappings defined in JSON
        return Array.Empty<Amazon.CDK.AWS.ECS.PortMapping>();
    }


    /// <summary>
    /// Get default essential setting for containers
    /// </summary>
    private bool GetDefaultEssential(string containerName)
    {
        return true; // All containers are essential by default
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
    /// Get standard health check for all containers
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck GetStandardHealthCheck()
    {
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[]
            {
                "CMD-SHELL",
                "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1"
            },
            Interval = Duration.Seconds(30),
            Timeout = Duration.Seconds(5),
            Retries = 3,
            StartPeriod = Duration.Seconds(60)
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
            Secrets = GetContainerSecrets(new List<string> { "test-secret" }, context),
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
    /// Add placeholder container for CDK-managed infrastructure
    /// This container serves as a stable placeholder while GitHub Actions manages app deployments
    /// </summary>
    private void AddPlaceholderContainer(FargateTaskDefinition taskDefinition,
        ILogGroup logGroup,
        DeploymentContext context)
    {
        // Import the ECR repository for the placeholder image
        var placeholderRepository = Repository.FromRepositoryName(this, "PlaceholderRepository",
            "thirdopinion/infra/deploy-placeholder");

        // Add environment variables that GitHub Actions will override
        var placeholderEnv = CreateDefaultEnvironmentVariables(context);
        placeholderEnv["DEPLOYMENT_TYPE"] = "placeholder";
        placeholderEnv["MANAGED_BY"] = "CDK";
        placeholderEnv["APP_NAME"] = context.Application.Name;
        placeholderEnv["APP_VERSION"] = "placeholder";

        taskDefinition.AddContainer("app", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(placeholderRepository, "latest"),
            Essential = true,
            PortMappings = new[]
            {
                new Amazon.CDK.AWS.ECS.PortMapping
                {
                    ContainerPort = 8080,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            Environment = placeholderEnv,
            Secrets = GetContainerSecrets(new List<string> { "test-secret" }, context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "app"
            }),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[]
                {
                    "CMD-SHELL",
                    "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1"
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
    /// Create dedicated IAM role for ECS tasks following naming convention
    /// </summary>
    private IRole CreateTaskRole()
    {
        // Follow naming convention: {environment}-{service}-task-role
        var roleName = $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-task-role";
        
        var taskRole = new Role(this, "TrialFinderTaskRole", new RoleProps
        {
            RoleName = roleName,
            Description = $"Task role for {_context.Application.Name} ECS tasks in {_context.Environment.Name}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            // Task role should not have the execution policy
            ManagedPolicies = Array.Empty<IManagedPolicy>()
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

        // Add CloudWatch Logs permissions for application logging
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogGroup",
                "logs:CreateLogStream",
                "logs:PutLogEvents",
                "logs:DescribeLogStreams"
            },
            Resources = new[] 
            { 
                $"arn:aws:logs:{_context.Environment.Region}:{_context.Environment.AccountId}:log-group:{_context.Namer.LogGroup("*", ResourcePurpose.Web)}:*"
            }
        }));

        // Add Secrets Manager permissions for environment-specific secrets
        AddSecretsManagerPermissions(taskRole);

        // Add tag for identification
        Amazon.CDK.Tags.Of(taskRole).Add("ManagedBy", "CDK");
        Amazon.CDK.Tags.Of(taskRole).Add("Purpose", "ECS-Task");
        Amazon.CDK.Tags.Of(taskRole).Add("Service", _context.Application.Name);

        return taskRole;
    }

    /// <summary>
    /// Create dedicated IAM role for ECS execution following naming convention
    /// </summary>
    private IRole CreateExecutionRole(ILogGroup logGroup)
    {
        // Follow naming convention: {environment}-{service}-execution-role
        var roleName = $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-execution-role";
        
        var executionRole = new Role(this, "TrialFinderExecutionRole", new RoleProps
        {
            RoleName = roleName,
            Description = $"Execution role for {_context.Application.Name} ECS tasks in {_context.Environment.Name}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add CloudWatch Logs permissions with specific log group
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogStream",
                "logs:PutLogEvents"
            },
            Resources = new[] 
            { 
                logGroup.LogGroupArn,
                $"{logGroup.LogGroupArn}:*"
            }
        }));

        // Add ECR permissions for pulling images from specific repositories
        var ecrRepoArns = new[]
        {
            $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/thirdopinion/*",
            $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/{_context.Application.Name.ToLowerInvariant()}/*"
        };
        
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
            Resources = ecrRepoArns
        }));

        // Add ECR authorization token permission (account-wide)
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[] { "ecr:GetAuthorizationToken" },
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for container startup
        AddSecretsManagerPermissions(executionRole);

        // Add tag for identification
        Amazon.CDK.Tags.Of(executionRole).Add("ManagedBy", "CDK");
        Amazon.CDK.Tags.Of(executionRole).Add("Purpose", "ECS-Execution");
        Amazon.CDK.Tags.Of(executionRole).Add("Service", _context.Application.Name);

        return executionRole;
    }

    /// <summary>
    /// Get container port for load balancer registration from JSON configuration
    /// </summary>
    private int? GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        // Use the first port mapping if available
        if (containerConfig.PortMappings?.Count > 0)
        {
            var firstPortMapping = containerConfig.PortMappings[0];
            if (firstPortMapping.ContainerPort.HasValue)
            {
                return firstPortMapping.ContainerPort.Value;
            }
        }

        // Return null if no port mappings defined - container cannot be used as primary container
        return null;
    }

    /// <summary>
    /// Generate port mapping name based on container name, port, and protocol
    /// </summary>
    private string GeneratePortMappingName(string containerName, int containerPort, string protocol)
    {
        return $"{containerName}-{containerPort}-{protocol.ToLowerInvariant()}";
    }

    /// <summary>
    /// Get protocol from string (required)
    /// </summary>
    private Amazon.CDK.AWS.ECS.Protocol GetProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            throw new InvalidOperationException("Protocol is required in port mapping configuration");
        }

        return protocol.ToUpperInvariant() switch
        {
            "TCP" => Amazon.CDK.AWS.ECS.Protocol.TCP,
            "UDP" => Amazon.CDK.AWS.ECS.Protocol.UDP,
            _ => throw new InvalidOperationException($"Unsupported protocol '{protocol}'. Supported protocols: TCP, UDP")
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

        // Environment-specific secret path patterns
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var applicationName = _context.Application.Name.ToLowerInvariant();
        
        // Define specific secret ARNs that the task can access
        var allowedSecretArns = new[]
        {
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/{applicationName}/*",
            // Allow access to shared secrets if needed
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/shared/*"
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
        // Use wildcard for KMS keys as they might be customer-managed
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

        // Add explicit deny for cross-environment access
        var deniedPrefixes = new[]
        {
            "production", "staging", "integration", "development"
        }.Where(env => env != environmentPrefix).ToArray();
        
        if (deniedPrefixes.Length > 0)
        {
            var deniedPatterns = deniedPrefixes.Select(prefix => $"/{prefix}/*").ToArray();
            
            concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "DenyCrossEnvironmentSecrets",
                Effect = Effect.DENY,
                Actions = new[]
                {
                    "secretsmanager:GetSecretValue",
                    "secretsmanager:DescribeSecret"
                },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["ForAnyValue:StringLike"] = new Dictionary<string, object>
                    {
                        ["secretsmanager:SecretId"] = deniedPatterns
                    }
                }
            }));
        }
    }

    /// <summary>
    /// Get container secrets from Secrets Manager
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
                
                // Use the secret name as the environment variable name (converted to uppercase)
                var envVarName = secretName.ToUpperInvariant().Replace("-", "_");
                secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
            }
        }
        
        return secrets;
    }

    /// <summary>
    /// Build full secret name following the naming convention: /{environmentPrefix}/{applicationName}/{secretName}
    /// </summary>
    private string BuildSecretName(string secretName, DeploymentContext context)
    {
        var environmentPrefix = context.Environment.Name.ToLowerInvariant();
        var applicationName = context.Application.Name.ToLowerInvariant();
        return $"/{environmentPrefix}/{applicationName}/{secretName}";
    }

    /// <summary>
    /// Get or create a secret in Secrets Manager
    /// </summary>
    private Amazon.CDK.AWS.SecretsManager.Secret GetOrCreateSecret(string secretName, string fullSecretName, DeploymentContext context)
    {
        // Check if we already created this secret
        if (_createdSecrets.ContainsKey(secretName))
        {
            return _createdSecrets[secretName];
        }

        // Create the secret with a placeholder value
        var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
        {
            SecretName = fullSecretName,
            Description = $"Secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = $"{{\"secretName\":\"{secretName}\"}}",
                GenerateStringKey = "value",
                PasswordLength = 32,
                ExcludeCharacters = "\"@/\\"
            }
        });

        // Store the secret reference for exporting ARN later
        _createdSecrets[secretName] = secret;

        return secret;
    }

    /// <summary>
    /// Export secret ARNs for all created secrets
    /// </summary>
    private void ExportSecretArns()
    {
        foreach (var (secretName, secret) in _createdSecrets)
        {
            var exportName = $"{_context.Environment.Name}-{_context.Application.Name}-{secretName}-secret-arn";
            new CfnOutput(this, $"SecretArn-{secretName}", new CfnOutputProps
            {
                Value = secret.SecretArn,
                Description = $"ARN for secret '{secretName}'",
                ExportName = exportName
            });
        }
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
    /// Get appropriate log retention based on environment
    /// </summary>
    private RetentionDays GetLogRetention(AccountType accountType)
    {
        return accountType switch
        {
            AccountType.Production => RetentionDays.ONE_MONTH,
            AccountType.NonProduction => RetentionDays.ONE_WEEK,
            _ => RetentionDays.THREE_DAYS
        };
    }

    /// <summary>
    /// Export task definition outputs for GitHub Actions deployments
    /// </summary>
    private void ExportTaskDefinitionOutputs(FargateTaskDefinition taskDefinition,
        FargateService service,
        DeploymentContext context)
    {
        // Export task definition ARN
        new CfnOutput(this, "TaskDefinitionArn", new CfnOutputProps
        {
            Value = taskDefinition.TaskDefinitionArn,
            Description = "ECS Task Definition ARN for GitHub Actions deployments",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-task-definition-arn"
        });

        // Export task definition family
        new CfnOutput(this, "TaskDefinitionFamily", new CfnOutputProps
        {
            Value = taskDefinition.Family,
            Description = "ECS Task Definition family name",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-task-family"
        });

        // Export service name
        new CfnOutput(this, "ServiceName", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = "ECS Service name",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-service-name"
        });

        // Export cluster name
        new CfnOutput(this, "ClusterName", new CfnOutputProps
        {
            Value = service.Cluster.ClusterName,
            Description = "ECS Cluster name",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-cluster-name"
        });

        // Export task role ARN
        new CfnOutput(this, "TaskRoleArn", new CfnOutputProps
        {
            Value = taskDefinition.TaskRole.RoleArn,
            Description = "ECS Task IAM Role ARN",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-task-role-arn"
        });

        // Export execution role ARN
        new CfnOutput(this, "ExecutionRoleArn", new CfnOutputProps
        {
            Value = taskDefinition.ExecutionRole?.RoleArn ?? "N/A",
            Description = "ECS Execution IAM Role ARN",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-execution-role-arn"
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