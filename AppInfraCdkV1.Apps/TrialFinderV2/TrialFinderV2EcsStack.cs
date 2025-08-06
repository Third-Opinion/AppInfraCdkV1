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
using System.Text.RegularExpressions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.ECR;
using Amazon.ECR.Model;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

/// <summary>
/// ECS Stack for TrialFinder V2 application
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
public class TrialFinderV2EcsStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly Dictionary<string, Amazon.CDK.AWS.SecretsManager.ISecret> _createdSecrets = new();
    private readonly Dictionary<string, string> _envVarToSecretNameMapping = new();
    private readonly Dictionary<string, IRepository> _ecrRepositories = new();
    private IRole? _githubActionsRole;

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

        // Import Cognito stack outputs
        var cognitoOutputs = ImportCognitoStackOutputs();

        // Create ECS cluster
        var cluster = CreateEcsCluster(vpc, context);

        // Create GitHub Actions deployment role
        _githubActionsRole = CreateGithubActionsDeploymentRole(context);

        // Create ECS service with containers from configuration
        CreateEcsService(cluster, albOutputs, cognitoOutputs, context);

        // Export secret ARNs for all created secrets
        ExportSecretArns();

        // Create ECR repositories from configuration
        CreateEcrRepositoriesFromConfig(context);

        // Export ECR repository information
        ExportEcrRepositoryOutputs();
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
    /// Import outputs from the Cognito stack
    /// </summary>
    private CognitoStackOutputs ImportCognitoStackOutputs()
    {
        var userPoolId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-user-pool-id");
        var appClientId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-app-client-id");
        var domainUrl = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-domain-url");
        var domainName = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-domain-name");

        return new CognitoStackOutputs
        {
            UserPoolId = userPoolId,
            AppClientId = appClientId,
            DomainUrl = domainUrl,
            DomainName = domainName
        };
    }

    /// <summary>
    /// Import shared database security group from EnvironmentBaseStack
    /// </summary>
    private ISecurityGroup ImportSharedDatabaseSecurityGroup()
    {
        var rdsSecurityGroupId = Fn.ImportValue($"{_context.Environment.Name}-sg-rds-id");
        return SecurityGroup.FromSecurityGroupId(this, "ImportedRdsSecurityGroup", rdsSecurityGroupId);
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
        CognitoStackOutputs cognitoOutputs,
        DeploymentContext context)
    {
        Console.WriteLine("\n🚀 Creating ECS Service...");
        
        // Load configuration from JSON
        var ecsConfig = _configLoader.LoadEcsConfig(context.Environment.Name);
        ecsConfig = _configLoader.SubstituteVariables(ecsConfig, context);
        
        Console.WriteLine($"📋 Loading ECS configuration for environment: {context.Environment.Name}");

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
                    CpuArchitecture = CpuArchitecture.X86_64
                }
            });

        // Collect all secret names from all containers first and build mapping once
        Console.WriteLine("🔐 Collecting all secret names and building mapping...");
        CollectAllSecretsAndBuildMapping(firstTaskDef, context);
        
        // Add containers from configuration and get primary container info
        Console.WriteLine("📦 Configuring containers from configuration...");
        var primaryContainer = AddContainersFromConfiguration(taskDefinition, firstTaskDef, logGroup, cognitoOutputs, context);

        // Import security groups
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
            Console.WriteLine($"🔗 Attaching ECS service to ALB target group");
            Console.WriteLine($"   Container: {primaryContainer.ContainerName}");
            Console.WriteLine($"   Port: {primaryContainer.ContainerPort}");
            
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
            Console.WriteLine("✅ ECS service attached to target group successfully");
        }
        else
        {
            Console.WriteLine("⚠️  Skipping ALB target group attachment - no container with valid port found");
        }
        
        // Export task definition ARN and family name for GitHub Actions
        ExportTaskDefinitionOutputs(taskDefinition, service, context);
        
        // Display summary of created secrets
        if (_createdSecrets.Count > 0)
        {
            Console.WriteLine($"\n  🔑 Secrets Manager Summary:");
            Console.WriteLine($"     Total secrets created/referenced: {_createdSecrets.Count}");
            foreach (var kvp in _createdSecrets)
            {
                Console.WriteLine($"     - {kvp.Key}: {kvp.Value.SecretName}");
            }
        }
    }

    /// <summary>
    /// Add containers from configuration with conditional logic
    /// </summary>
    private ContainerInfo AddContainersFromConfiguration(FargateTaskDefinition taskDefinition,
        TaskDefinitionConfig? taskDefConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs,
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
            
            Console.WriteLine($"  📋 Adding container: {containerName}");
            Console.WriteLine($"     Image: {containerConfig.Image ?? "placeholder"}");
            Console.WriteLine($"     Essential: {containerConfig.Essential ?? true}");
            Console.WriteLine($"     Port mappings: {containerConfig.PortMappings?.Count ?? 0}");
            Console.WriteLine($"     Secrets: {containerConfig.Secrets?.Count ?? 0}");
            
            if (containerPort.HasValue)
            {
                Console.WriteLine($"     Primary port: {containerPort.Value}");
            }
            else
            {
                Console.WriteLine($"     Primary port: None (no port mappings)");
            }
            
            AddConfiguredContainer(taskDefinition, containerConfig, logGroup, cognitoOutputs, context);
            containersProcessed++;

            // Use the first container with ports as the primary container for load balancing
            if (primaryContainer == null && containerPort.HasValue)
            {
                primaryContainer = new ContainerInfo(containerName, containerPort.Value);
                Console.WriteLine($"  ✅ Selected '{containerName}' as primary container for load balancing (port: {containerPort.Value})");
            }
        }

        // If no containers were processed at all, fall back to placeholder
        if (containersProcessed == 0)
        {
            Console.WriteLine("  ⚠️  No containers defined in configuration, adding placeholder container");
            AddPlaceholderContainer(taskDefinition, logGroup, context);
            return new ContainerInfo("app", 8080);
        }

        // If containers were processed but none have ports, we can't attach to load balancer
        // Return null to indicate no primary container available
        if (primaryContainer == null)
        {
            // Use the first container name for reference, even without ports
            var firstContainerName = containerDefinitions.First().Name ?? "default-container";
            Console.WriteLine($"  ⚠️  No containers with port mappings found. Load balancer attachment will be skipped.");
            Console.WriteLine($"     Using '{firstContainerName}' as reference container (no ports)");
            return new ContainerInfo(firstContainerName, 0); // Port 0 indicates no port mapping
        }

        Console.WriteLine($"  📊 Container configuration summary:");
        Console.WriteLine($"     Total containers: {containersProcessed}");
        Console.WriteLine($"     Primary container: {primaryContainer.ContainerName} (port: {primaryContainer.ContainerPort})");

        return primaryContainer;
    }

    /// <summary>
    /// Add a container based on configuration with comprehensive defaults
    /// </summary>
    private void AddConfiguredContainer(FargateTaskDefinition taskDefinition,
        ContainerDefinitionConfig containerConfig,
        ILogGroup logGroup,
        CognitoStackOutputs cognitoOutputs,
        DeploymentContext context)
    {
        var containerName = containerConfig.Name ?? "default-container";
        
        // Determine if we should use ECR latest image, placeholder, or specified image
        ContainerImage containerImage;
        Dictionary<string, string> environmentVars;
        
        if (string.IsNullOrWhiteSpace(containerConfig.Image) || containerConfig.Image == "placeholder")
        {
            // Check if ECR repository has latest image first
            var ecrImageUri = GetLatestEcrImageUri(containerName, context);
            if (!string.IsNullOrEmpty(ecrImageUri))
            {
                // Use latest image from ECR repository
                containerImage = ContainerImage.FromRegistry(ecrImageUri);
                environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
                environmentVars["DEPLOYMENT_TYPE"] = "ecr-latest";
                environmentVars["IMAGE_SOURCE"] = "ecr";
                environmentVars["ECR_REPOSITORY"] = _ecrRepositories["webapp"].RepositoryName;
                Console.WriteLine($"     🚀 Using latest ECR image for container '{containerName}': {ecrImageUri}");
            }
            else
            {
                // Fall back to placeholder image
                var placeholderRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"PlaceholderRepository-{containerName}",
                    "thirdopinion/infra/deploy-placeholder");
                containerImage = ContainerImage.FromEcrRepository(placeholderRepository, "latest");
                
                // Add placeholder-specific environment variables
                environmentVars = CreateDefaultEnvironmentVariables(context);
                environmentVars["DEPLOYMENT_TYPE"] = "placeholder";
                environmentVars["MANAGED_BY"] = "CDK";
                environmentVars["APP_NAME"] = context.Application.Name;
                environmentVars["APP_VERSION"] = "1.0.0"; // Static version to prevent unnecessary redeployments
                environmentVars["IMAGE_SOURCE"] = "placeholder";
                Console.WriteLine($"     📦 Using placeholder image for container '{containerName}' (no latest ECR image found)");
            }
        }
        else
        {
            // Use specified image
            containerImage = ContainerImage.FromRegistry(containerConfig.Image);
            environmentVars = GetEnvironmentVariables(containerConfig, context, containerName);
            environmentVars["IMAGE_SOURCE"] = "specified";
            Console.WriteLine($"     🎯 Using specified image for container '{containerName}': {containerConfig.Image}");
        }

        var containerOptions = new ContainerDefinitionOptions
        {
            Image = containerImage,
            Essential = containerConfig.Essential ?? GetDefaultEssential(containerName),
            Environment = environmentVars,
            Secrets = GetContainerSecrets(containerConfig.Secrets, cognitoOutputs, context),
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
            Console.WriteLine($"     Added {portMappings.Length} port mapping(s) to container '{containerName}'");
        }
        else
        {
            Console.WriteLine($"     No port mappings configured for container '{containerName}'");
        }

        // Apply health check based on container type
        var healthCheck = GetContainerHealthCheck(containerConfig, containerName);
        if (healthCheck != null)
        {
            containerOptions.HealthCheck = healthCheck;
            Console.WriteLine($"     Added health check for container '{containerName}'");
        }
        else
        {
            Console.WriteLine($"     Skipping health check for cron job container '{containerName}'");
        }

        // Add CPU allocation if specified
        if (containerConfig.Cpu.HasValue && containerConfig.Cpu.Value > 0)
        {
            containerOptions.Cpu = containerConfig.Cpu.Value;
        }

        taskDefinition.AddContainer(containerName, containerOptions);
    }

    /// <summary>
    /// Determine appropriate health check for container based on its type
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck? GetContainerHealthCheck(
        ContainerDefinitionConfig containerConfig,
        string containerName)
    {
        // Check if health check is explicitly disabled in configuration
        if (containerConfig.DisableHealthCheck == true)
        {
            Console.WriteLine($"     Health check explicitly disabled for container '{containerName}'");
            return null;
        }

        // Check if this is a cron job container (automatic detection)
        if (IsCronJobContainer(containerConfig, containerName))
        {
            Console.WriteLine($"     Skipping health check for cron job container '{containerName}'");
            return null;
        }

        // Use custom health check from configuration if provided
        if (containerConfig.HealthCheck != null)
        {
            var customHealthCheck = CreateCustomHealthCheck(containerConfig.HealthCheck, containerName);
            if (customHealthCheck != null)
            {
                Console.WriteLine($"     Using custom health check for container '{containerName}'");
                return customHealthCheck;
            }
        }

        // For web applications, use standard health check
        Console.WriteLine($"     Using standard health check for container '{containerName}'");
        return GetStandardHealthCheck(containerConfig);
    }

    /// <summary>
    /// Create custom health check from configuration
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck? CreateCustomHealthCheck(
        HealthCheckConfig healthCheckConfig,
        string containerName)
    {
        // If health check is explicitly disabled in config
        if (healthCheckConfig.Disabled == true)
        {
            return null;
        }

        // Validate required command
        if (healthCheckConfig.Command == null || healthCheckConfig.Command.Count == 0)
        {
            Console.WriteLine($"     Warning: Custom health check for '{containerName}' has no command, using standard health check");
            return GetStandardHealthCheck(null); // Pass null for containerConfig
        }

        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = healthCheckConfig.Command.ToArray(),
            Interval = Duration.Seconds(healthCheckConfig.Interval ?? 30),
            Timeout = Duration.Seconds(healthCheckConfig.Timeout ?? 5),
            Retries = healthCheckConfig.Retries ?? 3,
            StartPeriod = Duration.Seconds(healthCheckConfig.StartPeriod ?? 60)
        };
    }

    /// <summary>
    /// Determine if a container is a cron job based on configuration and naming
    /// </summary>
    private bool IsCronJobContainer(ContainerDefinitionConfig containerConfig, string containerName)
    {
        // Check container name patterns that indicate cron jobs
        var nameLower = containerName.ToLowerInvariant();
        var cronJobPatterns = new[]
        {
            "cron", "job", "batch", "scheduler", "task", "worker", "processor", "cleanup", "backup", "sync"
        };

        if (cronJobPatterns.Any(pattern => nameLower.Contains(pattern)))
        {
            return true;
        }

        // Check if container has no port mappings (typical for cron jobs)
        if (containerConfig.PortMappings?.Count == 0)
        {
            return true;
        }

        // Check environment variables for cron job indicators
        if (containerConfig.Environment?.Any(e => 
            e.Name?.ToLowerInvariant().Contains("cron") == true ||
            e.Name?.ToLowerInvariant().Contains("schedule") == true ||
            e.Value?.ToLowerInvariant().Contains("cron") == true) == true)
        {
            return true;
        }

        // Check if container is marked as non-essential (typical for cron jobs)
        if (containerConfig.Essential == false)
        {
            return true;
        }

        return false;
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
    /// Get standard health check for web application containers
    /// </summary>
    private Amazon.CDK.AWS.ECS.HealthCheck GetStandardHealthCheck(ContainerDefinitionConfig? containerConfig = null)
    {
        // Determine health check path from configuration or environment variable
        var healthCheckPath = GetHealthCheckPath(containerConfig);
        
        return new Amazon.CDK.AWS.ECS.HealthCheck
        {
            Command = new[]
            {
                "CMD-SHELL",
                $"wget --no-verbose --tries=1 --spider http://localhost:8080{healthCheckPath} || exit 1"
            },
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
        // Check if health check path is specified in container configuration
        if (containerConfig?.HealthCheck?.Command?.Count > 0)
        {
            // If custom health check command is provided, extract path from it
            var command = string.Join(" ", containerConfig.HealthCheck.Command);
            if (command.Contains("http://localhost:8080"))
            {
                // Extract path from custom command
                var match = Regex.Match(command, @"http://localhost:8080([^\s]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        // Check environment variables in container config for health check path
        if (containerConfig?.Environment?.Count > 0)
        {
            var healthCheckPathEnv = containerConfig.Environment.FirstOrDefault(e => 
                e.Name?.Equals("HEALTH_CHECK_PATH", StringComparison.OrdinalIgnoreCase) == true);
            
            if (!string.IsNullOrWhiteSpace(healthCheckPathEnv?.Value))
            {
                // Ensure path starts with /
                return healthCheckPathEnv.Value.StartsWith("/") ? healthCheckPathEnv.Value : $"/{healthCheckPathEnv.Value}";
            }
        }

        // Default health check paths based on common patterns
        return "/health";
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
            Secrets = GetContainerSecrets(new List<string> { "test-secret" }, null, context),
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
        var placeholderRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, "PlaceholderRepository",
            "thirdopinion/infra/deploy-placeholder");

        // Add environment variables that GitHub Actions will override
        var placeholderEnv = CreateDefaultEnvironmentVariables(context);
        placeholderEnv["DEPLOYMENT_TYPE"] = "placeholder";
        placeholderEnv["MANAGED_BY"] = "CDK";
        placeholderEnv["APP_NAME"] = context.Application.Name;
        placeholderEnv["APP_VERSION"] = "1.0.0"; // Static version to prevent unnecessary redeployments

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
            Secrets = GetContainerSecrets(new List<string> { "test-secret" }, null, context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "app"
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
    /// Create default environment variables
    /// </summary>
    private Dictionary<string, string> CreateDefaultEnvironmentVariables(DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = "1.0.0", // Static version to prevent unnecessary redeployments
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/health",
            ["AWS_REGION"] = context.Environment.Region,
            ["AWS_ACCOUNT_ID"] = context.Environment.AccountId
            // Removed QuickSight-related environment variables
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

        // Add QuickSight permissions for embedding functionality
        AddQuickSightPermissions(taskRole);

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
    /// Create GitHub Actions deployment role
    /// </summary>
    private IRole CreateGithubActionsDeploymentRole(DeploymentContext context)
    {
        // Follow naming convention: {environment}-{service}-github-actions-role
        var roleName = context.Namer.IamRole(IamPurpose.GithubActionsDeploy);
        
        var deploymentRole = new Role(this, "GithubActionsDeploymentRole", new RoleProps
        {
            RoleName = roleName,
            Description = $"Role for GitHub Actions to deploy to ECS in {context.Environment.Name}",
            AssumedBy = new FederatedPrincipal($"arn:aws:iam::{context.Environment.AccountId}:oidc-provider/token.actions.githubusercontent.com", new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com",
                    ["token.actions.githubusercontent.com:iss"] = "https://token.actions.githubusercontent.com"
                },
                ["StringLike"] = new Dictionary<string, string>
                {
                    ["token.actions.githubusercontent.com:sub"] = $"repo:Third-Opinion/TrialFinderV2:*"
                }
            }, "sts:AssumeRoleWithWebIdentity"),
            ManagedPolicies = Array.Empty<IManagedPolicy>()
        });

        // Add ECS permissions for task definition management
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECSDeployment",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeTaskDefinition",
                "ecs:RegisterTaskDefinition",
                "ecs:DescribeServices",
                "ecs:UpdateService",
                "ecs:DescribeClusters",
                "ecs:ListTasks",
                "ecs:DescribeTasks",
                "ecs:StopTask",
                "ecs:RunTask"
            },
            Resources = new[]
            {
                $"arn:aws:ecs:{context.Environment.Region}:{context.Environment.AccountId}:task-definition/*",
                $"arn:aws:ecs:{context.Environment.Region}:{context.Environment.AccountId}:service/*",
                $"arn:aws:ecs:{context.Environment.Region}:{context.Environment.AccountId}:cluster/*",
                $"arn:aws:ecs:{context.Environment.Region}:{context.Environment.AccountId}:task/*"
            }
        }));

        // Add specific permission for DescribeTaskDefinition with * resource
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowDescribeTaskDefinition",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeTaskDefinition"
            },
            Resources = new[]
            {
                "*"
            }
        }));
        
        // Add ECR permissions for image access
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECRAccess",
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
            Resources = new[]
            {
                $"arn:aws:ecr:{context.Environment.Region}:{context.Environment.AccountId}:repository/thirdopinion/*",
                $"arn:aws:ecr:{context.Environment.Region}:{context.Environment.AccountId}:repository/{context.Application.Name.ToLowerInvariant()}/*"
            }
        }));

        // Add ECR authorization token permission (account-wide)
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECRAuthorizationToken",
            Effect = Effect.ALLOW,
            Actions = new[] { "ecr:GetAuthorizationToken" },
            Resources = new[] { "*" }
        }));

        // Add CloudFormation permissions to read stack outputs
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowCloudFormationRead",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "cloudformation:DescribeStacks",
                "cloudformation:ListStacks",
                "cloudformation:GetTemplate"
            },
            Resources = new[]
            {
                $"arn:aws:cloudformation:{context.Environment.Region}:{context.Environment.AccountId}:stack/*"
            }
        }));

        // Add IAM permissions for role assumption (needed for OIDC)
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowIAMRoleAssumption",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole"
            },
            Resources = new[]
            {
                $"arn:aws:iam::{context.Environment.AccountId}:role/{context.Environment.Name}-{context.Application.Name.ToLowerInvariant()}-*"
            }
        }));

        // Add Secrets Manager permissions for secret existence checking
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowSecretsManagerRead",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:DescribeSecret",
                "secretsmanager:GetSecretValue",
                "secretsmanager:ListSecrets"
            },
            Resources = new[]
            {
                $"arn:aws:secretsmanager:{context.Environment.Region}:{context.Environment.AccountId}:secret:/{context.Environment.Name.ToLowerInvariant()}/{context.Application.Name.ToLowerInvariant()}/*"
            }
        }));

        // Add tag for identification
        Amazon.CDK.Tags.Of(deploymentRole).Add("ManagedBy", "CDK");
        Amazon.CDK.Tags.Of(deploymentRole).Add("Purpose", "GitHub-Actions-Deployment");
        Amazon.CDK.Tags.Of(deploymentRole).Add("Service", context.Application.Name);

        return deploymentRole;
    }

    /// <summary>
    /// Get container port for load balancer registration from JSON configuration
    /// Prefers port 8080 for web applications, falls back to first available port
    /// </summary>
    private int? GetContainerPort(ContainerDefinitionConfig containerConfig, string containerName)
    {
        if (containerConfig.PortMappings?.Count > 0)
        {
            // First, try to find port 8080 (preferred for web applications)
            var preferredPortMapping = containerConfig.PortMappings.FirstOrDefault(pm => 
                pm.ContainerPort == 8080);
            
            if (preferredPortMapping?.ContainerPort.HasValue == true)
            {
                Console.WriteLine($"     Selected preferred port 8080 for container '{containerName}'");
                return preferredPortMapping.ContainerPort.Value;
            }
            
            // Fall back to first available port mapping
            var firstPortMapping = containerConfig.PortMappings[0];
            if (firstPortMapping.ContainerPort.HasValue)
            {
                Console.WriteLine($"     Selected fallback port {firstPortMapping.ContainerPort.Value} for container '{containerName}'");
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
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/shared/*",
            // Allow access to RDS database credentials secret (including version suffixes)
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
    /// Add QuickSight permissions to IAM role for embedding functionality and database access
    /// </summary>
    private void AddQuickSightPermissions(IRole role)
    {
        // Cast to Role to access AddToPolicy method
        var concreteRole = role as Role;
        if (concreteRole == null) return;

        // QuickSight user management permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightUserManagement",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:RegisterUser",
                "quicksight:UnregisterUser",
                "quicksight:DescribeUser",
                "quicksight:ListUsers",
                "quicksight:UpdateUser"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:user/*"
            }
        }));

        // QuickSight embedding permissions (includes Generative Q&A)
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightEmbedding",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:GenerateEmbedUrlForRegisteredUser", // For Generative Q&A and dashboard embedding
                "quicksight:GetDashboardEmbedUrl",
                "quicksight:GetSessionEmbedUrl",
                "quicksight:DescribeDashboard",
                "quicksight:ListDashboards",
                "quicksight:DescribeDataSet",
                "quicksight:ListDataSets"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:user/*",
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:dashboard/*",
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:dataset/*"
            }
        }));

        // QuickSight namespace permissions (for multi-tenant scenarios)
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightNamespaceAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeNamespace",
                "quicksight:ListNamespaces"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:namespace/*"
            }
        }));

        // QuickSight data source permissions for database access
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightDataSourceAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeDataSource",
                "quicksight:ListDataSources",
                "quicksight:CreateDataSource",
                "quicksight:UpdateDataSource",
                "quicksight:DeleteDataSource",
                "quicksight:DescribeDataSourcePermissions",
                "quicksight:UpdateDataSourcePermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:datasource/*"
            }
        }));

        // QuickSight analysis permissions for creating and managing analyses
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightAnalysisAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeAnalysis",
                "quicksight:ListAnalyses",
                "quicksight:CreateAnalysis",
                "quicksight:UpdateAnalysis",
                "quicksight:DeleteAnalysis",
                "quicksight:DescribeAnalysisPermissions",
                "quicksight:UpdateAnalysisPermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:analysis/*"
            }
        }));

        // QuickSight theme permissions for consistent styling
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightThemeAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeTheme",
                "quicksight:ListThemes",
                "quicksight:CreateTheme",
                "quicksight:UpdateTheme",
                "quicksight:DeleteTheme",
                "quicksight:DescribeThemePermissions",
                "quicksight:UpdateThemePermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:theme/*"
            }
        }));
    }

    /// <summary>
    /// Collect all secret names from all containers and build mapping once
    /// </summary>
    private void CollectAllSecretsAndBuildMapping(TaskDefinitionConfig? taskDefConfig, DeploymentContext context)
    {
        var allSecretNames = new HashSet<string>();
        
        // Collect secrets from container definitions
        var containerDefinitions = taskDefConfig?.ContainerDefinitions;
        if (containerDefinitions != null)
        {
            foreach (var containerConfig in containerDefinitions)
            {
                if (containerConfig.Secrets != null)
                {
                    foreach (var secretName in containerConfig.Secrets)
                    {
                        allSecretNames.Add(secretName);
                    }
                }
            }
        }
        
        // Add any hardcoded secrets (like test-secret)
        allSecretNames.Add("test-secret");
        
        // Build mapping for all collected secrets
        Console.WriteLine($"   Found {allSecretNames.Count} unique secret(s) across all containers");
        BuildSecretNameMapping(allSecretNames.ToList());
    }

    /// <summary>
    /// Get container secrets from Secrets Manager
    /// </summary>
    private Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();
        
        if (secretNames?.Count > 0)
        {
            Console.WriteLine($"     🔐 Processing {secretNames.Count} secret(s):");
            foreach (var envVarName in secretNames)
            {
                // Use the mapping to get the secret name, or fall back to the original name
                var secretName = GetSecretNameFromEnvVar(envVarName);
                
                var fullSecretName = BuildSecretName(secretName, context);
                var secret = GetOrCreateSecret(secretName, fullSecretName, cognitoOutputs, context);
                
                // Use the original environment variable name from the configuration
                secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
                
                Console.WriteLine($"        - Environment variable '{envVarName}' -> Secret '{secretName}'");
                Console.WriteLine($"          Full secret path: {secret.SecretArn}");
            }
        }
        
        return secrets;
    }

    /// <summary>
    /// Build the mapping dictionary from secret names in configuration
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
    /// Get secret name from environment variable name using mapping
    /// </summary>
    private string GetSecretNameFromEnvVar(string envVarName)
    {
        // Use the dynamically built mapping
        if (_envVarToSecretNameMapping.TryGetValue(envVarName, out var mappedSecretName))
        {
            return mappedSecretName;
        }
        
        // Fallback: convert the environment variable name to a valid secret name
        // Replace __ with - and convert to lowercase
        return envVarName.ToLowerInvariant().Replace("__", "-");
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
    /// Check if a secret exists in AWS Secrets Manager using AWS SDK
    /// </summary>
    /// <summary>
    /// Get secret information from AWS Secrets Manager using AWS SDK
    /// Returns a tuple with (exists, arn) where arn is null if secret doesn't exist
    /// </summary>
    private async Task<(bool exists, string? arn)> GetSecretAsync(string secretName)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(secretName))
        {
            Console.WriteLine($"          ⚠️  Secret name is null or empty");
            return (false, null);
        }

        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = await secretsManagerClient.DescribeSecretAsync(describeSecretRequest);
            return (true, response.ARN);
        }
        catch (ResourceNotFoundException)
        {
            return (false, null);
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if secret '{secretName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it");
            return (false, null);
        }
    }

    /// <summary>
    /// Check if a secret exists in Secrets Manager using AWS SDK (synchronous wrapper)
    /// </summary>
    private (bool exists, string? arn) GetSecret(string secretName)
    {
        // Use Task.Run to avoid blocking the main thread
        return Task.Run(() => GetSecretAsync(secretName)).Result;
    }

    /// <summary>
    /// Get or create a secret in Secrets Manager
    /// This method uses AWS SDK to check if secrets exist before creating them.
    /// If a secret exists, it imports the existing secret reference to preserve manual values.
    /// If a secret doesn't exist, it creates a new secret with generated values.
    /// </summary>
    private Amazon.CDK.AWS.SecretsManager.ISecret GetOrCreateSecret(string secretName, string fullSecretName, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        // Use AWS SDK to check if secret exists and get its ARN
        Console.WriteLine($"          🔍 Checking if secret '{fullSecretName}' exists using AWS SDK...");
        
        var (secretExists, secretArn) = GetSecret(fullSecretName);
        
        if (secretExists)
        {
            // Secret exists - import it to preserve manual values
            Console.WriteLine($"          ✅ Found existing secret '{fullSecretName}' - importing reference (preserving manual values)");
            
            var existingSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretCompleteArn(this, $"ImportedSecret-{secretName}", secretArn!);
            
            // Add the CDKManaged tag to existing secrets to ensure IAM policy compliance
            Amazon.CDK.Tags.Of(existingSecret).Add("CDKManaged", "true");
            
            _createdSecrets[secretName] = existingSecret;
            return existingSecret;
        }
        else
        {
            // Secret doesn't exist - create it with generated values or Cognito values
            Console.WriteLine($"          ✨ Creating new secret '{fullSecretName}' with generated values");
            
            // For Cognito secrets, we'll create them with generated values for now
            // The actual values will be populated by the application at runtime
            if (cognitoOutputs != null && IsCognitoSecret(secretName))
            {
                Console.WriteLine($"          🔐 Creating Cognito secret '{secretName}' with generated value (will be updated manually)");
            }
            {
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

                // Store the full ARN with version suffix for later use
                // _secretFullArns[secretName] = secret.SecretArn; // This line is removed

                _createdSecrets[secretName] = secret;
                return secret;
            }
        }
    }

    /// <summary>
    /// Check if a secret name corresponds to a Cognito secret
    /// </summary>
    private bool IsCognitoSecret(string secretName)
    {
        var cognitoSecretNames = new[]
        {
            "cognito-client-id",
            "cognito-client-secret", 
            "cognito-user-pool-id",
            "cognito-domain"
        };
        
        return cognitoSecretNames.Contains(secretName.ToLowerInvariant());
    }

    /// <summary>
    /// Get the actual value for a Cognito secret
    /// </summary>
    private string GetCognitoSecretValue(string secretName, CognitoStackOutputs cognitoOutputs)
    {
        return secretName.ToLowerInvariant() switch
        {
            "cognito-client-id" => cognitoOutputs.AppClientId,
            "cognito-client-secret" => "***SECRET***", // Client secret is not exposed for security
            "cognito-user-pool-id" => cognitoOutputs.UserPoolId,
            "cognito-domain" => cognitoOutputs.DomainUrl,
            _ => throw new ArgumentException($"Unknown Cognito secret name: {secretName}")
        };
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

        // Export GitHub Actions deployment role ARN
        new CfnOutput(this, "GithubActionsRoleArn", new CfnOutputProps
        {
            Value = _githubActionsRole?.RoleArn ?? "N/A",
            Description = "GitHub Actions deployment IAM Role ARN",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-github-actions-role-arn"
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

    /// <summary>
    /// Helper class to hold Cognito stack outputs
    /// </summary>
    private class CognitoStackOutputs
    {
        public string UserPoolId { get; set; } = "";
        public string AppClientId { get; set; } = "";
        public string DomainUrl { get; set; } = "";
        public string DomainName { get; set; } = "";
    }

    /// <summary>
    /// Create ECR repository for TrialFinder container images
    /// </summary>
    /// <summary>
    /// Create ECR repositories from configuration
    /// </summary>
    private void CreateEcrRepositoriesFromConfig(DeploymentContext context)
    {
        var config = _configLoader.LoadFullConfig(context.Environment.Name);
        
        if (config.EcsConfiguration?.TaskDefinition != null)
        {
            foreach (var taskDef in config.EcsConfiguration.TaskDefinition)
            {
                if (taskDef.ContainerDefinitions != null)
                {
                    foreach (var container in taskDef.ContainerDefinitions)
                    {
                        if (container.Repository != null && !string.IsNullOrWhiteSpace(container.Repository.Type))
                        {
                            var repositoryName = context.Namer.EcrRepository(container.Repository.Type);
                            var repositoryKey = container.Name ?? "unknown"; // Use container name as key
                            
                            if (!_ecrRepositories.ContainsKey(repositoryKey))
                            {
                                _ecrRepositories[repositoryKey] = GetOrCreateEcrRepository(
                                    container.Repository.Type, 
                                    repositoryName, 
                                    $"TrialFinder{container.Name}EcrRepository"
                                );
                            }
                        }
                    }
                }
            }
        }
    }

    private IRepository CreateEcrRepository(DeploymentContext context)
    {
        var repositoryName = context.Namer.EcrRepository("webapp");
        
        return GetOrCreateEcrRepository("webapp", repositoryName, "TrialFinderEcrRepository");
    }

    /// <summary>
    /// Get existing ECR repository or create new one
    /// </summary>
    private IRepository GetOrCreateEcrRepository(string serviceType, string repositoryName, string constructId)
    {
        // Use AWS SDK to check if repository actually exists
        var region = _context.Environment.Region;
        using var ecrClient = new AmazonECRClient(new AmazonECRConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
        });

        try
        {
            // Check if repository exists using AWS SDK
            var describeRequest = new DescribeRepositoriesRequest
            {
                RepositoryNames = new List<string> { repositoryName }
            };
            var describeResponse = Task.Run(() => ecrClient.DescribeRepositoriesAsync(describeRequest)).Result;
            
            if (describeResponse.Repositories != null && describeResponse.Repositories.Count > 0)
            {
                // Repository exists, import it
                var existingRepository = Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, $"{constructId}Import", repositoryName);
                Console.WriteLine($"✅ Imported existing ECR repository: {repositoryName}");
                return existingRepository;
            }
            else
            {
                // Repository doesn't exist, create it
                var repository = new Amazon.CDK.AWS.ECR.Repository(this, constructId, new RepositoryProps
                {
                    RepositoryName = repositoryName,
                    ImageScanOnPush = true,
                    RemovalPolicy = RemovalPolicy.RETAIN
                });
                Console.WriteLine($"✅ Created new ECR repository: {repositoryName}");
                return repository;
            }
        }
        catch (RepositoryNotFoundException)
        {
            // Repository doesn't exist, create it
            var repository = new Amazon.CDK.AWS.ECR.Repository(this, constructId, new RepositoryProps
            {
                RepositoryName = repositoryName,
                ImageScanOnPush = true,
                RemovalPolicy = RemovalPolicy.RETAIN
            });
            Console.WriteLine($"✅ Created new ECR repository: {repositoryName}");
            return repository;
        }
        catch (Exception ex)
        {
            // For any other error, assume repository doesn't exist and create it
            Console.WriteLine($"⚠️  Error checking ECR repository '{repositoryName}': {ex.Message}");
            Console.WriteLine($"   Creating new ECR repository: {repositoryName}");
            
            var repository = new Amazon.CDK.AWS.ECR.Repository(this, constructId, new RepositoryProps
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
    /// Export ECR repository information for external consumption
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
                Description = $"TrialFinder ECR Repository ARN for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-finder-{repositoryName}-ecr-repository-arn"
            });

            new CfnOutput(this, $"EcrRepositoryName-{repositoryName}", new CfnOutputProps
            {
                Value = repository.RepositoryName,
                Description = $"TrialFinder ECR Repository Name for {repositoryName}",
                ExportName = $"{_context.Environment.Name}-trial-finder-{repositoryName}-ecr-repository-name"
            });
        }
    }

    /// <summary>
    /// Check if ECR repository has a latest image and return its URI (async version)
    /// </summary>
    private async Task<string?> GetLatestEcrImageUriAsync(string containerName, DeploymentContext context)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(containerName))
        {
            Console.WriteLine($"     ⚠️  Container name is null or empty");
            return null;
        }

        try
        {
            // Get the ECR repository for the container
            if (!_ecrRepositories.TryGetValue(containerName, out var repository))
            {
                Console.WriteLine($"     ⚠️  ECR repository not found for container '{containerName}'");
                return null;
            }

            var repositoryName = repository.RepositoryName;
            var region = context.Environment.Region;
            var accountId = context.Environment.AccountId;

            Console.WriteLine($"     🔍 Checking for latest image in ECR repository: {repositoryName}");

            // Use AWS SDK to check if repository has latest tag
            using var ecrClient = new AmazonECRClient(new AmazonECRConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            });

            // List images in the repository
            var listImagesRequest = new ListImagesRequest
            {
                RepositoryName = repositoryName
            };

            var listImagesResponse = await ecrClient.ListImagesAsync(listImagesRequest);
            
            if (listImagesResponse.ImageIds == null || listImagesResponse.ImageIds.Count == 0)
            {
                Console.WriteLine($"     ℹ️  No images found in ECR repository: {repositoryName}");
                return null;
            }

            // Check if latest tag exists first, then fall back to the most recently pushed image
            var latestImage = listImagesResponse.ImageIds.FirstOrDefault(img => 
                img.ImageTag != null && img.ImageTag.Equals("latest", StringComparison.OrdinalIgnoreCase));

            if (latestImage == null)
            {
                // Get detailed information about all images to find the most recently pushed one
                var describeImagesRequest = new DescribeImagesRequest
                {
                    RepositoryName = repositoryName,
                    ImageIds = listImagesResponse.ImageIds
                };

                var describeImagesResponse = await ecrClient.DescribeImagesAsync(describeImagesRequest);
                
                if (describeImagesResponse.ImageDetails == null || describeImagesResponse.ImageDetails.Count == 0)
                {
                    Console.WriteLine($"     ℹ️  No image details found in ECR repository: {repositoryName}");
                    return null;
                }

                // Find the most recently pushed image
                var mostRecentImage = describeImagesResponse.ImageDetails
                    .OrderByDescending(img => img.ImagePushedAt)
                    .FirstOrDefault();

                if (mostRecentImage == null)
                {
                    Console.WriteLine($"     ℹ️  No images with push timestamps found in ECR repository: {repositoryName}");
                    return null;
                }

                // Get the tag for the most recent image
                var imageTag = mostRecentImage.ImageTags?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(imageTag))
                {
                    Console.WriteLine($"     ℹ️  Most recent image has no tags in ECR repository: {repositoryName}");
                    return null;
                }

                // Use the most recently pushed image
                var mostRecentImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:{imageTag}";
                Console.WriteLine($"     ✅ Found most recently pushed image: {mostRecentImageUri} (pushed at: {mostRecentImage.ImagePushedAt})");
                return mostRecentImageUri;
            }

            // Construct the full image URI for latest tag
            var latestImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:latest";
            Console.WriteLine($"     ✅ Found latest image: {latestImageUri}");
            
            return latestImageUri;
        }
        catch (RepositoryNotFoundException)
        {
            Console.WriteLine($"     ⚠️  ECR repository not found for container '{containerName}'");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"     ⚠️  Error checking ECR repository for container '{containerName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if ECR repository has a latest image and return its URI (synchronous wrapper)
    /// </summary>
    private string? GetLatestEcrImageUri(string containerName, DeploymentContext context)
    {
        // Use Task.Run to avoid blocking the main thread
        return Task.Run(() => GetLatestEcrImageUriAsync(containerName, context)).Result;
    }
}