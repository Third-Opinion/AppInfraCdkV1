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
    private ICluster? _cluster;
    private readonly Dictionary<string, IRepository> _ecrRepositories = new();

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

        // Create ECR repositories for API and frontend
        CreateEcrRepositories(context);

        // Create ECS cluster
        _cluster = CreateEcsCluster(vpc, context);

        // Create ECS services with containers from configuration
        CreateEcsServices(_cluster, albOutputs, context);

        // Export secret ARNs for all created secrets
        ExportSecretArns();

        // Export cluster information
        ExportClusterOutputs();

        // Export ECR repository information
        ExportEcrRepositoryOutputs();

        // Create GitHub Actions ECS deployment role
        CreateGitHubActionsEcsDeployRole(context);
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
            ApiTargetGroupArn = Fn.ImportValue($"{_context.Environment.Name}-trial-match-api-target-group-arn"),
            FrontendTargetGroupArn = Fn.ImportValue($"{_context.Environment.Name}-trial-match-frontend-target-group-arn"),
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
    /// Create ECS services with containers from configuration
    /// </summary>
    private void CreateEcsServices(ICluster cluster,
        AlbStackOutputs albOutputs,
        DeploymentContext context)
    {
        // Load ECS configuration
        var ecsConfig = _configLoader.LoadEcsConfig(context.Environment.Name);
        
        if (ecsConfig.Services == null || ecsConfig.Services.Count == 0)
        {
            throw new InvalidOperationException("No services configured in ECS configuration");
        }

        foreach (var serviceConfig in ecsConfig.Services)
        {
            CreateEcsService(cluster, albOutputs, context, serviceConfig);
        }
    }

    /// <summary>
    /// Create a single ECS service with containers from configuration
    /// </summary>
    private void CreateEcsService(ICluster cluster,
        AlbStackOutputs albOutputs,
        DeploymentContext context,
        ServiceConfig serviceConfig)
    {
        // Create log group for the service
        var logGroup = new LogGroup(this, $"TrialMatchLogGroup-{serviceConfig.ServiceName}", new LogGroupProps
        {
            LogGroupName = context.Namer.LogGroup(serviceConfig.ServiceName, ResourcePurpose.Web),
            Retention = GetLogRetention(context.Environment.AccountType),
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Create task definition
        var taskDefinition = new FargateTaskDefinition(this, $"TrialMatchTaskDefinition-{serviceConfig.ServiceName}", new FargateTaskDefinitionProps
        {
            Family = $"{context.Namer.EcsTaskDefinition(ResourcePurpose.Web)}-{serviceConfig.ServiceName.Replace("trial-match-", "")}",
            Cpu = 256,
            MemoryLimitMiB = 512,
            ExecutionRole = CreateExecutionRole(logGroup, serviceConfig.ServiceName),
            TaskRole = CreateTaskRole(serviceConfig.ServiceName)
        });

        // Add containers from configuration
        var containerInfo = AddContainersFromConfiguration(taskDefinition, serviceConfig.TaskDefinition.FirstOrDefault(), logGroup, context);

        // Create security group for ECS tasks
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, $"EcsSecurityGroup-{serviceConfig.ServiceName}", albOutputs.EcsSecurityGroupId);

        // Create ECS service with deployment-friendly settings
        var service = new FargateService(this, $"TrialMatchService-{serviceConfig.ServiceName}", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = $"{context.Namer.EcsService(ResourcePurpose.Web)}-{serviceConfig.ServiceName.Replace("trial-match-", "")}",
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
            Console.WriteLine($"🔗 Attaching ECS service '{serviceConfig.ServiceName}' to ALB target group");
            Console.WriteLine($"   Container: {containerInfo.ContainerName}");
            Console.WriteLine($"   Port: {containerInfo.ContainerPort}");
            
            // Import target group from ALB stack and attach service
            var targetGroupArn = serviceConfig.ServiceName.Contains("api") ? albOutputs.ApiTargetGroupArn : albOutputs.FrontendTargetGroupArn;
            var targetGroup = ApplicationTargetGroup.FromTargetGroupAttributes(this,
                $"ImportedTargetGroup-{serviceConfig.ServiceName}", new TargetGroupAttributes
                {
                    TargetGroupArn = targetGroupArn,
                    LoadBalancerArns = targetGroupArn // This will be overridden by the actual target group
                });

            // Register ECS service with target group using explicit container and port
            service.AttachToApplicationTargetGroup(targetGroup);
            Console.WriteLine($"✅ ECS service '{serviceConfig.ServiceName}' attached to target group successfully");
        }
        else
        {
            Console.WriteLine($"⚠️  Skipping ALB target group attachment for service '{serviceConfig.ServiceName}' - no container with valid port found");
        }

        // Export task definition outputs
        ExportTaskDefinitionOutputs(taskDefinition, service, context, serviceConfig.ServiceName, containerInfo);
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

        // Log summary of secrets created/referenced
        Console.WriteLine($"\n  🔑 Secrets Manager Summary:");
        Console.WriteLine($"     Total secrets created/referenced: {_createdSecrets.Count}");
        foreach (var kvp in _createdSecrets)
        {
            Console.WriteLine($"     - {kvp.Key}: {kvp.Value.SecretName}");
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

        Console.WriteLine($"\n  📦 Adding container '{containerConfig.Name}':");
        Console.WriteLine($"     Image: {containerConfig.Image ?? "placeholder"}");
        Console.WriteLine($"     CPU: {containerConfig.Cpu ?? 0}");
        Console.WriteLine($"     Essential: {containerConfig.Essential ?? GetDefaultEssential(containerConfig.Name)}");
        Console.WriteLine($"     Port Mappings: {containerConfig.PortMappings?.Count ?? 0}");
        Console.WriteLine($"     Environment Variables: {containerConfig.Environment?.Count ?? 0}");
        Console.WriteLine($"     Secrets: {containerConfig.Secrets?.Count ?? 0}");

        // Handle placeholder image by using ECR repository
        ContainerImage containerImage;
        if (containerConfig.Image == "placeholder")
        {
            var placeholderRepository = Repository.FromRepositoryName(this, $"PlaceholderRepository-{containerConfig.Name}",
                "thirdopinion/infra/deploy-placeholder");
            containerImage = ContainerImage.FromEcrRepository(placeholderRepository, "latest");
        }
        else
        {
            containerImage = ContainerImage.FromRegistry(containerConfig.Image ?? "placeholder");
        }

        var container = taskDefinition.AddContainer(containerConfig.Name, new ContainerDefinitionOptions
        {
            Image = containerImage,
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
        // Determine port based on container name
        var port = containerName.ToLower().Contains("frontend") ? "80" : "8080";
        
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name),
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["AWS_REGION"] = context.Environment.Region,
            ["PORT"] = port,
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
        // Import the ECR repository for the placeholder image
        var placeholderRepository = Repository.FromRepositoryName(this, "PlaceholderRepository",
            "thirdopinion/infra/deploy-placeholder");

        // Add environment variables that GitHub Actions will override
        var placeholderEnv = CreateDefaultEnvironmentVariables(context, "trial-match");
        placeholderEnv["DEPLOYMENT_TYPE"] = "placeholder";
        placeholderEnv["MANAGED_BY"] = "CDK";
        placeholderEnv["APP_NAME"] = context.Application.Name;
        placeholderEnv["APP_VERSION"] = "1.0.0"; // Static version to prevent unnecessary redeployments

        var container = taskDefinition.AddContainer("trial-match", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(placeholderRepository, "latest"),
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
            Environment = placeholderEnv,
            Logging = LogDrivers.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "ecs"
            }),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            }
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
    private Dictionary<string, string> CreateDefaultEnvironmentVariables(DeploymentContext context, string? containerName = null)
    {
        // Determine port based on container name
        var port = containerName?.ToLower().Contains("frontend") == true ? "80" : "8080";
        
        return new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetAspNetCoreEnvironment(context.Environment.Name),
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["AWS_REGION"] = context.Environment.Region,
            ["PORT"] = port,
            ["HEALTH_CHECK_PATH"] = "/health"
        };
    }

    /// <summary>
    /// Create task role with necessary permissions
    /// </summary>
    private IRole CreateTaskRole(string serviceName)
    {
        var role = new Role(this, $"TrialMatchTaskRole-{serviceName}", new RoleProps
        {
            RoleName = $"TrialMatchTaskRole-{serviceName}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = $"IAM role for TrialMatch {serviceName} ECS task execution"
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
    private IRole CreateExecutionRole(ILogGroup logGroup, string serviceName)
    {
        var role = new Role(this, $"TrialMatchExecutionRole-{serviceName}", new RoleProps
        {
            RoleName = $"TrialMatchExecutionRole-{serviceName}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = $"IAM role for TrialMatch {serviceName} ECS task execution"
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
        Console.WriteLine("🔐 Collecting all secret names and building mapping...");
        
        var allSecretNames = new List<string>();

        if (taskDefConfig?.ContainerDefinitions?.Count > 0)
        {
            foreach (var container in taskDefConfig.ContainerDefinitions)
            {
                if (container.Secrets?.Count > 0)
                {
                    allSecretNames.AddRange(container.Secrets);
                }
            }
        }

        Console.WriteLine($"   Found {allSecretNames.Count} unique secret(s) across all containers");

        // Build secret name mapping
        BuildSecretNameMapping(allSecretNames);
    }

    /// <summary>
    /// Build secret name mapping
    /// </summary>
    private void BuildSecretNameMapping(List<string> secretNames)
    {
        foreach (var secretName in secretNames)
        {
            // Only add if not already present to avoid overwriting
            if (!_envVarToSecretNameMapping.ContainsKey(secretName))
            {
                // Convert the secret name to a valid AWS Secrets Manager name
                // Replace __ with - and convert to lowercase
                var mappedSecretName = secretName.Replace("__", "-").ToLowerInvariant();
                _envVarToSecretNameMapping[secretName] = mappedSecretName;
            }
        }
    }

    /// <summary>
    /// Get secret name from environment variable
    /// </summary>
    private string GetSecretNameFromEnvVar(string secretName)
    {
        // Use the dynamically built mapping
        if (_envVarToSecretNameMapping.TryGetValue(secretName, out var mappedSecretName))
        {
            return mappedSecretName;
        }
        
        // Fallback: convert the environment variable name to a valid secret name
        // Replace __ with - and convert to lowercase
        return secretName.ToLowerInvariant().Replace("__", "-");
    }

    /// <summary>
    /// Build full secret name
    /// </summary>
    private string BuildSecretName(string secretName, DeploymentContext context)
    {
        var environmentPrefix = context.Environment.Name.ToLowerInvariant();
        var applicationName = context.Application.Name.ToLowerInvariant();
        return $"/{environmentPrefix}/{applicationName}/{secretName}";
    }

    /// <summary>
    /// Check if secret exists using AWS SDK
    /// </summary>
    private bool SecretExists(string secretName)
    {
        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = secretsManagerClient.DescribeSecretAsync(describeSecretRequest).Result;
            return response != null;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if secret '{secretName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it");
            return false;
        }
    }

    /// <summary>
    /// Get container secrets
    /// </summary>
    private Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();

        if (secretNames?.Count > 0)
        {
            Console.WriteLine($"     🔐 Processing {secretNames.Count} secret(s):");
            
            foreach (var envVarName in secretNames)
            {
                // Special handling for tm-frontend-env-vars secret
                if (envVarName == "tm-frontend-env-vars")
                {
                    var secretName = GetSecretNameFromEnvVar(envVarName);
                    var fullSecretName = BuildSecretName(secretName, context);
                    
                    Console.WriteLine($"        - Multi-value secret '{envVarName}' -> Secret '{secretName}'");
                    Console.WriteLine($"          Full secret path: {fullSecretName}");
                    
                    var secret = GetOrCreateMultiValueSecret(secretName, fullSecretName, context);
                    
                    // Create individual secret references for each environment variable
                    var frontendEnvVars = new[]
                    {
                        "NEXT_PUBLIC_COGNITO_USER_POOL_ID",
                        "NEXT_PUBLIC_COGNITO_CLIENT_ID", 
                        "NEXT_PUBLIC_COGNITO_CLIENT_SECRET",
                        "NEXT_PUBLIC_COGNITO_DOMAIN",
                        "NEXT_PUBLIC_API_URL",
                        "NEXT_PUBLIC_API_MODE"
                    };
                    
                    foreach (var envVar in frontendEnvVars)
                    {
                        secrets[envVar] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret, envVar);
                    }
                }
                else
                {
                    // Handle individual secrets as before
                    var secretName = GetSecretNameFromEnvVar(envVarName);
                    var fullSecretName = BuildSecretName(secretName, context);
                    
                    Console.WriteLine($"        - Environment variable '{envVarName}' -> Secret '{secretName}'");
                    Console.WriteLine($"          Full secret path: {fullSecretName}");
                    
                    var secret = GetOrCreateSecret(secretName, fullSecretName, context);
                    
                    secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
                }
            }
        }

        return secrets;
    }

    /// <summary>
    /// Get or create a secret in Secrets Manager
    /// This method uses AWS SDK to check if secrets exist before creating them.
    /// If a secret exists, it imports the existing secret reference to preserve manual values.
    /// If a secret doesn't exist, it creates a new secret with generated values.
    /// </summary>
    private Amazon.CDK.AWS.SecretsManager.ISecret GetOrCreateSecret(string secretName, string fullSecretName, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        // Use AWS SDK to check if secret exists
        Console.WriteLine($"          🔍 Checking if secret '{fullSecretName}' exists using AWS SDK...");
        
        if (SecretExists(fullSecretName))
        {
            // Secret exists - import it to preserve manual values
            Console.WriteLine($"          ✅ Found existing secret '{fullSecretName}' - importing reference (preserving manual values)");
            var existingSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, $"ImportedSecret-{secretName}", fullSecretName);
            
            // Add the CDKManaged tag to existing secrets to ensure IAM policy compliance
            Amazon.CDK.Tags.Of(existingSecret).Add("CDKManaged", "true");
            
            _createdSecrets[secretName] = existingSecret;
            return existingSecret;
        }
        else
        {
            // Secret doesn't exist - create it with generated values
            Console.WriteLine($"          ✨ Creating new secret '{fullSecretName}' with generated values");
            
            // Regular secret with generated values
            var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                GenerateSecretString = new SecretStringGenerator
                {
                    SecretStringTemplate = $"{{\"secretName\":\"{secretName}\",\"managedBy\":\"CDK\",\"environment\":\"{context.Environment.Name}\"}}",
                    GenerateStringKey = "value",
                    PasswordLength = 32,
                    ExcludeCharacters = "\"@/\\"
                }
            });

            // Add the CDKManaged tag required by IAM policy
            Amazon.CDK.Tags.Of(secret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = secret;
            return secret;
        }
    }

    /// <summary>
    /// Get or create a multi-value secret in Secrets Manager
    /// This method is specifically for handling secrets that contain multiple key-value pairs,
    /// like the tm-frontend-env-vars secret.
    /// </summary>
    private Amazon.CDK.AWS.SecretsManager.ISecret GetOrCreateMultiValueSecret(string secretName, string fullSecretName, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        // Use AWS SDK to check if secret exists
        Console.WriteLine($"          🔍 Checking if secret '{fullSecretName}' exists using AWS SDK...");
        
        if (SecretExists(fullSecretName))
        {
            // Secret exists - import it to preserve manual values
            Console.WriteLine($"          ✅ Found existing secret '{fullSecretName}' - importing reference (preserving manual values)");
            var existingSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, $"ImportedSecret-{secretName}", fullSecretName);
            
            // Add the CDKManaged tag to existing secrets to ensure IAM policy compliance
            Amazon.CDK.Tags.Of(existingSecret).Add("CDKManaged", "true");
            
            _createdSecrets[secretName] = existingSecret;
            return existingSecret;
        }
        else
        {
            // Secret doesn't exist - create it with the proper structure for frontend environment variables
            Console.WriteLine($"          ✨ Creating new multi-value secret '{fullSecretName}' with frontend environment variables");
            
            // Create the JSON structure for frontend environment variables
            var frontendEnvVarsJson = $@"{{
  ""NEXT_PUBLIC_COGNITO_USER_POOL_ID"": ""your_value"",
  ""NEXT_PUBLIC_COGNITO_CLIENT_ID"": ""your_value"", 
  ""NEXT_PUBLIC_COGNITO_CLIENT_SECRET"": ""your_value"",
  ""NEXT_PUBLIC_COGNITO_DOMAIN"": ""your_value"",
  ""NEXT_PUBLIC_API_URL"": ""your_value"",
  ""NEXT_PUBLIC_API_MODE"": ""your_value""
}}";
            
            // Multi-value secret with the proper JSON structure
            var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Multi-value secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                GenerateSecretString = new SecretStringGenerator
                {
                    SecretStringTemplate = frontendEnvVarsJson
                }
            });

            // Add the CDKManaged tag required by IAM policy
            Amazon.CDK.Tags.Of(secret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = secret;
            return secret;
        }
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

            // Convert secret name to valid export name (replace all underscores with hyphens and convert to lowercase)
            var validExportName = secretName.Replace("_", "-").Replace("__", "-").ToLowerInvariant();

            new CfnOutput(this, $"SecretArn-{validExportName}", new CfnOutputProps
            {
                Value = secret.SecretArn,
                Description = $"TrialMatch Secret ARN for {secretName}",
                ExportName = $"{_context.Environment.Name}-trial-match-secret-{validExportName}-arn"
            });
        }
    }

    /// <summary>
    /// Export cluster information
    /// </summary>
    private void ExportClusterOutputs()
    {
        if (_cluster == null)
        {
            Console.WriteLine("Cluster not created, skipping export.");
            return;
        }

        new CfnOutput(this, $"ClusterArn", new CfnOutputProps
        {
            Value = _cluster.ClusterArn,
            Description = "TrialMatch ECS Cluster ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-cluster-arn"
        });

        new CfnOutput(this, $"ClusterName", new CfnOutputProps
        {
            Value = _cluster.ClusterName,
            Description = "TrialMatch ECS Cluster Name",
            ExportName = $"{_context.Environment.Name}-trial-match-cluster-name"
        });
    }

    /// <summary>
    /// Export ECR repository information
    /// </summary>
    private void ExportEcrRepositoryOutputs()
    {
        foreach (var kvp in _ecrRepositories)
        {
            var repositoryName = kvp.Key;
            var repository = kvp.Value;

            new CfnOutput(this, $"EcrRepositoryArn-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryArn,
                Description = $"TrialMatch ECR Repository ARN for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-match-{repositoryName}-ecr-repository-arn"
            });

            new CfnOutput(this, $"EcrRepositoryName-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryName,
                Description = $"TrialMatch ECR Repository Name for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-match-{repositoryName}-ecr-repository-name"
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
        DeploymentContext context,
        string serviceName,
        ContainerInfo containerInfo)
    {
        new CfnOutput(this, $"TaskDefinitionArn-{serviceName}", new CfnOutputProps
        {
            Value = taskDefinition.TaskDefinitionArn,
            Description = $"TrialMatch {serviceName} Task Definition ARN",
            ExportName = $"{context.Environment.Name}-trial-match-{serviceName}-task-definition-arn"
        });

        new CfnOutput(this, $"ServiceArn-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceArn,
            Description = $"TrialMatch {serviceName} ECS Service ARN",
            ExportName = $"{context.Environment.Name}-trial-match-{serviceName}-service-arn"
        });

        new CfnOutput(this, $"ServiceName-{serviceName}", new CfnOutputProps
        {
            Value = service.ServiceName,
            Description = $"TrialMatch {serviceName} ECS Service Name",
            ExportName = $"{context.Environment.Name}-trial-match-{serviceName}-service-name"
        });

        new CfnOutput(this, $"ContainerName-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerName,
            Description = $"TrialMatch {serviceName} Container Name",
            ExportName = $"{context.Environment.Name}-trial-match-{serviceName}-container-name"
        });

        new CfnOutput(this, $"ContainerPort-{serviceName}", new CfnOutputProps
        {
            Value = containerInfo.ContainerPort.ToString(),
            Description = $"TrialMatch {serviceName} Container Port",
            ExportName = $"{context.Environment.Name}-trial-match-{serviceName}-container-port"
        });
    }

    /// <summary>
    /// Create ECR repositories for API and frontend
    /// </summary>
    private void CreateEcrRepositories(DeploymentContext context)
    {
        var apiRepositoryName = context.Namer.EcrRepository("api");
        var frontendRepositoryName = context.Namer.EcrRepository("frontend");

        // Create or import API repository
        if (!_ecrRepositories.ContainsKey("api"))
        {
            _ecrRepositories["api"] = GetOrCreateEcrRepository("api", apiRepositoryName, "TrialMatchApiEcrRepository");
        }

        // Create or import frontend repository
        if (!_ecrRepositories.ContainsKey("frontend"))
        {
            _ecrRepositories["frontend"] = GetOrCreateEcrRepository("frontend", frontendRepositoryName, "TrialMatchFrontendEcrRepository");
        }
    }

    /// <summary>
    /// Get existing ECR repository or create new one
    /// </summary>
    private IRepository GetOrCreateEcrRepository(string serviceType, string repositoryName, string constructId)
    {
        try
        {
            // Try to import existing repository first
            var existingRepository = Repository.FromRepositoryName(this, $"{constructId}Import", repositoryName);
            Console.WriteLine($"✅ Imported existing ECR repository: {repositoryName}");
            return existingRepository;
        }
        catch (Exception)
        {
            // If import fails, create new repository
            var repository = new Repository(this, constructId, new RepositoryProps
            {
                RepositoryName = repositoryName,
                ImageScanOnPush = true,
                RemovalPolicy = RemovalPolicy.RETAIN
            });
            Console.WriteLine($"✅ Created new ECR repository: {repositoryName}");
            return repository;
        }
    }

    /// <summary>
    /// Create GitHub Actions ECS deployment role
    /// </summary>
    private void CreateGitHubActionsEcsDeployRole(DeploymentContext context)
    {
        var role = new Role(this, "GitHubActionsEcsDeployRole", new RoleProps
        {
            RoleName = $"dev-tm-role-g-ecsdeploy-github-actions",
            AssumedBy = new CompositePrincipal(
                new ServicePrincipal("ecs.amazonaws.com"),
                new WebIdentityPrincipal(
                    $"arn:aws:iam::{context.Environment.AccountId}:oidc-provider/token.actions.githubusercontent.com",
                    new Dictionary<string, object>
                    {
                        ["StringEquals"] = new Dictionary<string, object>
                        {
                            ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                        },
                        ["StringLike"] = new Dictionary<string, object>
                        {
                            ["token.actions.githubusercontent.com:sub"] = new object[]
                            {
                                // AppInfraCdkV1 repository conditions
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/development",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/master",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main",
                                $"repo:Third-Opinion/AppInfraCdkV1:pull_request",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*",
                                $"repo:Third-Opinion/AppInfraCdkV1:environment:development",
                                // TrialMatch Frontend repository conditions
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/develop",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/development",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/master",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/main",
                                $"repo:Third-Opinion/TrialMatch-FE:pull_request",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/*",
                                $"repo:Third-Opinion/TrialMatch-FE:environment:development",
                                // TrialMatch Backend repository conditions
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/develop",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/development",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/master",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/main",
                                $"repo:Third-Opinion/TrialMatch-BE:pull_request",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/*",
                                $"repo:Third-Opinion/TrialMatch-BE:environment:development"
                            }
                        }
                    }
                )
            ),
            Description = $"Allows ECS to create and manage AWS resources on your behalf for TrialMatch in {context.Environment.Name} environment",
            MaxSessionDuration = Duration.Hours(1)
        });

        // Add ECS permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeServices",
                "ecs:DescribeTaskDefinition",
                "ecs:DescribeTasks",
                "ecs:ListTasks",
                "ecs:UpdateService",
                "ecs:RegisterTaskDefinition",
                "ecs:CreateService",
                "ecs:DeleteService",
                "ecs:StopTask",
                "ecs:RunTask",
                "ecs:StartTask",
                "ecs:DescribeClusters",
                "ecs:ListServices",
                "ecs:ListTaskDefinitions"
            },
            Resources = new[] { "*" }
        }));

        // Add IAM PassRole permissions for ECS task roles
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole"
            },
            Resources = new[]
            {
                $"arn:aws:iam::{context.Environment.AccountId}:role/TrialMatchTaskRole-*",
                $"arn:aws:iam::{context.Environment.AccountId}:role/TrialMatchExecutionRole-*"
            }
        }));

        // Add ECR permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecr:GetAuthorizationToken",
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage",
                "ecr:PutImage",
                "ecr:InitiateLayerUpload",
                "ecr:UploadLayerPart",
                "ecr:CompleteLayerUpload"
            },
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for tagged secrets
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    ["secretsmanager:ResourceTag/CDKManaged"] = "true"
                }
            }
        }));

        // Add CloudWatch Logs permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogGroup",
                "logs:CreateLogStream",
                "logs:PutLogEvents",
                "logs:DescribeLogGroups",
                "logs:DescribeLogStreams"
            },
            Resources = new[] { "*" }
        }));

        // Add IAM permissions for ECS task roles
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole",
                "iam:GetRole"
            },
            Resources = new[]
            {
                $"arn:aws:iam::{context.Environment.AccountId}:role/dev-tm-role-ue2-ecs-task-*",
                $"arn:aws:iam::{context.Environment.AccountId}:role/dev-tm-role-ue2-ecs-exec-*"
            }
        }));

        // Attach the same managed policies as TrialFinderV2 role
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonEC2ContainerServiceRole"));
        
        // Try to attach the custom ECS deploy policy if it exists
        try
        {
            role.AddManagedPolicy(ManagedPolicy.FromManagedPolicyArn(this, "DevGPolicyGhEcsDeploy", 
                $"arn:aws:iam::{context.Environment.AccountId}:policy/dev-g-policy-g-gh-ecs-deploy"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Warning: Could not attach dev-g-policy-g-gh-ecs-deploy policy: {ex.Message}");
            Console.WriteLine("The role will still work with the inline policies defined above.");
        }

        // Add tags
        Amazon.CDK.Tags.Of(role).Add("Purpose", "GitHubActionsECSDeploy");
        Amazon.CDK.Tags.Of(role).Add("Environment", context.Environment.Name);
        Amazon.CDK.Tags.Of(role).Add("Application", context.Application.Name);
        Amazon.CDK.Tags.Of(role).Add("ManagedBy", "CDK");

        // Export the role ARN
        new CfnOutput(this, "GitHubActionsEcsDeployRoleArn", new CfnOutputProps
        {
            Value = role.RoleArn,
            Description = $"ARN of the GitHub Actions ECS deployment role for TrialMatch in {context.Environment.Name}",
            ExportName = $"{context.Environment.Name}-trial-match-github-actions-ecs-deploy-role-arn"
        });
    }

    /// <summary>
    /// ALB stack outputs
    /// </summary>
    private class AlbStackOutputs
    {
        public string ApiTargetGroupArn { get; set; } = "";
        public string FrontendTargetGroupArn { get; set; } = "";
        public string EcsSecurityGroupId { get; set; } = "";
    }
} 