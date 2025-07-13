using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ECS.Patterns;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.ExternalResources;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Stacks.WebApp;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public class TrialFinderV2Stack : WebApplicationStack
{
    private readonly DeploymentContext _context;
    
    public TrialFinderV2Stack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props, context)
    {
        _context = context;
        
        // Validate external dependencies before creating resources
        ValidateExternalDependencies(context);
        
        // Add TrialFinderV2-specific resources here
        CreateTrialFinderSpecificResources(context);
    }

    private void CreateTrialFinderSpecificResources(DeploymentContext context)
    {
        // Use VPC lookup to find existing VPC instead of imports
        var vpc = Vpc.FromLookup(this, "ExistingVpc", new VpcLookupOptions
        {
            VpcId = "vpc-085a37ab90d4186ac"  // Use the existing VPC we found
        });
        
        var cluster = GetEcsCluster(vpc);
        
        // Create security groups for ALB and ECS
        var securityGroups = CreateSecurityGroups(vpc, context);
        
        // Create ALB with S3 logging
        var alb = CreateApplicationLoadBalancer(vpc, securityGroups.AlbSecurityGroup, context);
        
        // Create ECR repository for container images
        var ecrRepository = CreateEcrRepository(context);
        
        // Create ECS service and task definition
        var ecsService = CreateEcsService(cluster, alb, securityGroups.EcsSecurityGroup, context, ecrRepository);
        
        // Create TrialFinder-specific storage and services
        CreateTrialDocumentStorage(context);
        // CreateAsyncProcessingQueue(context);
        // CreateNotificationServices(context);
    }



    private void CreateTrialDocumentStorage(DeploymentContext context)
    {
        // Document storage for trial PDFs, protocols, etc.
        Bucket documentsBucket = new Bucket(this, "TrialDocumentsBucket", new BucketProps
        {
            // Use auto-generated bucket name to avoid conflicts
            // BucketName = context.Namer.S3Bucket(StoragePurpose.Documents),
            Versioned = false,
            RemovalPolicy = RemovalPolicy.RETAIN_ON_UPDATE_OR_DELETE,
            AutoDeleteObjects = false,
            EventBridgeEnabled = false,
            // LifecycleRules = new ILifecycleRule[]
            // {
            //     new LifecycleRule
            //     {
            //         Id = "ArchiveOldVersions",
            //         Enabled = true,
            //         NoncurrentVersionExpiration
            //             = Duration.Days(context.Environment.IsProductionClass ? 365 : 90),
            //         Transitions = new[]
            //         {
            //             new Transition
            //             {
            //                 StorageClass = StorageClass.INFREQUENT_ACCESS,
            //                 TransitionAfter = Duration.Days(30)
            //             },
            //             new Transition
            //             {
            //                 StorageClass = StorageClass.GLACIER,
            //                 TransitionAfter = Duration.Days(90)
            //             }
            //         }
            //     }
            // }
        });
    }

    private void CreateAsyncProcessingQueue(DeploymentContext context)
    {
        // Dead letter queue for failed processing
        var deadLetterQueue = new Queue(this, "ProcessingDeadLetterQueue", new QueueProps
        {
            QueueName = context.Namer.SqsQueue(QueuePurpose.DeadLetter),
            RetentionPeriod = Duration.Days(14)
        });

        // Main processing queue for trial data imports
        var processingQueue = new Queue(this, "TrialProcessingQueue", new QueueProps
        {
            QueueName = context.Namer.SqsQueue(QueuePurpose.Processing),
            VisibilityTimeout = Duration.Minutes(15),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 3
            }
        });

        // High priority queue for urgent updates
        var urgentQueue = new Queue(this, "UrgentProcessingQueue", new QueueProps
        {
            QueueName = context.Namer.SqsQueue(QueuePurpose.Urgent),
            VisibilityTimeout = Duration.Minutes(5),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 5
            }
        });
    }

    private void CreateNotificationServices(DeploymentContext context)
    {
        // SNS topic for trial status updates
        var trialUpdatesTopic = new Topic(this, "TrialUpdatesTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.TrialUpdates),
            DisplayName = "Clinical Trial Updates"
        });

        // SNS topic for system alerts
        var alertsTopic = new Topic(this, "SystemAlertsTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.SystemAlerts),
            DisplayName = "System Alerts and Monitoring"
        });

        // SNS topic for user notifications
        var userNotificationsTopic = new Topic(this, "UserNotificationsTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.UserNotifications),
            DisplayName = "User Notifications"
        });
    }

    /// <summary>
    /// Validates that all required external resources exist and are properly configured
    /// </summary>
    private void ValidateExternalDependencies(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating TrialFinderV2 external dependencies...");
        
        var requirements = new TrialFinderV2ExternalDependencies();
        var requirementsList = requirements.GetRequirements(context);
        
        // Check if external resources are expected to exist
        if (!context.AllExternalResourcesValid && requirementsList.Any())
        {
            Console.WriteLine("⚠️  External resource validation not completed - assuming resources exist");
            Console.WriteLine("   Run external resource validation before deployment in production");
            
            // In development/testing, we'll proceed with warnings
            foreach (var requirement in requirementsList)
            {
                Console.WriteLine($"   Expected: {requirement.ResourceType} - {requirement.ExpectedName}");
                Console.WriteLine($"   ARN: {requirement.ExpectedArn}");
            }
        }
        else if (context.AllExternalResourcesValid)
        {
            Console.WriteLine("✅ All TrialFinderV2 external dependencies validated");
        }
        else if (context.ExternalResourceErrors.Any())
        {
            Console.WriteLine("❌ External resource validation failed:");
            foreach (var error in context.ExternalResourceErrors)
            {
                Console.WriteLine($"   {error}");
            }
            
            // Generate creation commands for missing resources
            Console.WriteLine("\n📋 To create missing resources, run:");
            var validator = new ExternalResourceValidator();
            foreach (var requirement in requirementsList)
            {
                var commands = validator.GenerateCreationCommands(requirement, context);
                Console.WriteLine(commands);
                Console.WriteLine();
            }
            
            throw new InvalidOperationException("External resource dependencies not met. See console output for details.");
        }
    }

    /// <summary>
    /// Get the ECS cluster from the parent WebApplicationStack
    /// </summary>
    private ICluster GetEcsCluster(IVpc vpc)
    {
        // The cluster is created in the parent WebApplicationStack
        // We need to reference it by name since it's in the same stack
        return Cluster.FromClusterAttributes(this, "ImportedCluster", new ClusterAttributes
        {
            ClusterName = _context.Namer.EcsCluster(),
            Vpc = vpc
        });
    }

    /// <summary>
    /// Create security groups for ALB and ECS based on existing patterns
    /// </summary>
    private (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) CreateSecurityGroups(IVpc vpc, DeploymentContext context)
    {
        // ALB Security Group - allows HTTPS traffic from internet
        var albSecurityGroup = new SecurityGroup(this, "TrialFinderAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = context.Namer.SecurityGroupForAlb(ResourcePurpose.Web),
            Description = "Security group for TrialFinder ALB - allows HTTPS from internet",
            AllowAllOutbound = true
        });

        // Allow HTTPS inbound from anywhere
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "HTTPS from internet");
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "HTTP from internet (redirect to HTTPS)");

        // Import existing ECS Security Group instead of creating new one
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "TrialFinderEcsSecurityGroup", "sg-0e7ddc9391c2220f4");

        return (albSecurityGroup, ecsSecurityGroup);
    }

    /// <summary>
    /// Create Application Load Balancer with S3 access logging
    /// </summary>
    private IApplicationLoadBalancer CreateApplicationLoadBalancer(IVpc vpc, ISecurityGroup securityGroup, DeploymentContext context)
    {
        // Create S3 bucket for ALB access logs
        var albLogsBucket = new Bucket(this, "TrialFinderAlbLogsBucket", new BucketProps
        {
            BucketName = context.Namer.Custom("alb-logs", ResourcePurpose.Web),
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
            LifecycleRules = new[]
            {
                new Amazon.CDK.AWS.S3.LifecycleRule
                {
                    Id = "DeleteOldLogs",
                    Enabled = true,
                    Expiration = Duration.Days(30)
                }
            }
        });

        // Create Application Load Balancer
        var alb = new ApplicationLoadBalancer(this, "TrialFinderAlb", new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancerProps
        {
            Vpc = vpc,
            LoadBalancerName = context.Namer.ApplicationLoadBalancer(ResourcePurpose.Web),
            InternetFacing = true,
            SecurityGroup = securityGroup,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC }
        });

        // Enable access logging to S3
        alb.LogAccessLogs(albLogsBucket, "trial-finder-alb-logs");

        return alb;
    }

    /// <summary>
    /// Create ECS service and task definition
    /// </summary>
    private IBaseService CreateEcsService(ICluster cluster, IApplicationLoadBalancer alb, ISecurityGroup securityGroup, DeploymentContext context, IRepository ecrRepository)
    {
        // Create log group for ECS tasks
        var logGroup = new LogGroup(this, "TrialFinderLogGroup", new LogGroupProps
        {
            LogGroupName = context.Namer.LogGroup("trial-finder", ResourcePurpose.Web),
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY
        });

        // Create Fargate task definition
        var taskDefinition = new FargateTaskDefinition(this, "TrialFinderTaskDefinition", new FargateTaskDefinitionProps
        {
            Family = context.Namer.EcsTaskDefinition(ResourcePurpose.Web),
            MemoryLimitMiB = 512,
            Cpu = 256,
            TaskRole = CreateTaskRole(),
            ExecutionRole = CreateExecutionRole()
        });

        // Add container to task definition
        var container = taskDefinition.AddContainer("trial-finder-v2", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(ecrRepository, "latest"),
            PortMappings = new[]
            {
                new PortMapping
                {
                    ContainerPort = 8080,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            Environment = CreateEnvironmentVariables(context),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = "trial-finder"
            }),
            HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
            {
                Command = new[] { "CMD-SHELL", "curl -f http://localhost:8080/health || exit 1" },
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                Retries = 3,
                StartPeriod = Duration.Seconds(60)
            }
        });

        // Create target group
        var targetGroup = new ApplicationTargetGroup(this, "TrialFinderTargetGroup", new ApplicationTargetGroupProps
        {
            Port = 8080,
            Protocol = ApplicationProtocol.HTTP,
            Vpc = cluster.Vpc,
            TargetGroupName = context.Namer.Custom("tg", ResourcePurpose.Web),
            TargetType = TargetType.IP
        });
        
        // Configure health check
        targetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
        {
            Path = "/health",
            Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.Protocol.HTTP,
            Interval = Duration.Seconds(30),
            HealthyThresholdCount = 2,
            UnhealthyThresholdCount = 3
        });

        // Create ECS service
        var service = new FargateService(this, "TrialFinderService", new FargateServiceProps
        {
            Cluster = cluster,
            ServiceName = context.Namer.EcsService(ResourcePurpose.Web),
            TaskDefinition = taskDefinition,
            DesiredCount = 1,
            MinHealthyPercent = 50, // Explicitly set to avoid CDK warning
            MaxHealthyPercent = 200,
            AssignPublicIp = false,
            SecurityGroups = new[] { securityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            EnableExecuteCommand = true
        });

        // Create ALB listener
        var listener = alb.AddListener("TrialFinderListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP,
            DefaultTargetGroups = new[] { targetGroup }
        });

        // Register ECS service with target group
        service.AttachToApplicationTargetGroup(targetGroup);

        return service;
    }

    /// <summary>
    /// Create environment variables for the container
    /// </summary>
    private Dictionary<string, string> CreateEnvironmentVariables(DeploymentContext context)
    {
        return new Dictionary<string, string>
        {
            ["ENVIRONMENT"] = context.Environment.Name,
            ["ACCOUNT_TYPE"] = context.Environment.AccountType.ToString(),
            ["APP_VERSION"] = context.Application.Version,
            ["PORT"] = "8080",
            ["HEALTH_CHECK_PATH"] = "/health"
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
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add permissions for Session Manager (ECS Exec)
        taskRole.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore"));
        
        return taskRole;
    }

    /// <summary>
    /// Create IAM role for ECS execution
    /// </summary>
    private IRole CreateExecutionRole()
    {
        var executionRole = new Role(this, "TrialFinderExecutionRole", new RoleProps
        {
            RoleName = _context.Namer.IamRole(IamPurpose.EcsExecution),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add CloudWatch Logs permissions
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogStream",
                "logs:PutLogEvents",
                "logs:CreateLogGroup"
            },
            Resources = new[] { "*" }
        }));

        // Add ECR permissions for pulling images
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

        return executionRole;
    }

    /// <summary>
    /// Create ECR repository for TrialFinder container images
    /// </summary>
    private IRepository CreateEcrRepository(DeploymentContext context)
    {
        var repositoryName = context.Namer.EcrRepository("web");
        
        // Import existing ECR repository instead of creating new one
        var repository = Repository.FromRepositoryName(this, "TrialFinderRepository", repositoryName);

        // Note: ECR pull permissions will be granted to the ECS execution role 
        // when the task definition is created

        return repository;
    }
}