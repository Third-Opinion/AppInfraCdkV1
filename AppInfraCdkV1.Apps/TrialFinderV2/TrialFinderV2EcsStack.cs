using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Apps.TrialFinderV2.Services;
using AppInfraCdkV1.Apps.TrialFinderV2.Builders;
using AppInfraCdkV1.Core.Configuration;
using AppInfraCdkV1.Core.Models;
using Constructs;

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
/// - Dedicated base stack infrastructure for complete isolation
/// 
/// Secret Management:
/// The stack checks if secrets already exist in AWS Secrets Manager before creating new ones.
/// This prevents CDK from attempting to recreate secrets that already exist, which would cause
/// deployment failures. Existing secrets are imported and referenced, while missing secrets
/// are created with generated values.
/// 
/// Base Stack Integration:
/// This stack now uses the dedicated TrialFinderV2 base stack for VPC, security groups,
/// database, and other shared infrastructure, ensuring complete isolation from other applications.
/// </summary>
public class TrialFinderV2EcsStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    
    // Service dependencies
    private readonly SecretManager _secretManager;
    private readonly EcrRepositoryManager _ecrRepositoryManager;
    private readonly LoggingManager _loggingManager;
    private readonly OutputExporter _outputExporter;
    private readonly IamRoleBuilder _iamRoleBuilder;
    private readonly ContainerConfigurationService _containerConfigurationService;
    private readonly EcsServiceFactory _ecsServiceFactory;
    
    private IRole? _githubActionsRole;

    public TrialFinderV2EcsStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();

        // Initialize services
        _secretManager = new SecretManager(this, "SecretManager", context);
        _ecrRepositoryManager = new EcrRepositoryManager(this, "EcrRepositoryManager", context);
        _loggingManager = new LoggingManager(this, "LoggingManager", context);
        _outputExporter = new OutputExporter(this, "OutputExporter", context);
        _iamRoleBuilder = new IamRoleBuilder(this, "IamRoleBuilder", context);
        
        // Initialize ContainerConfigurationService
        _containerConfigurationService = new ContainerConfigurationService(this, "ContainerConfigurationService", context, _secretManager);
        
        // Initialize EcsServiceFactory with all dependencies
        _ecsServiceFactory = new EcsServiceFactory(this, "EcsServiceFactory", context, 
            _secretManager, _ecrRepositoryManager, _loggingManager, _outputExporter, _iamRoleBuilder, _containerConfigurationService);

        // Load configuration including base stack configuration
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dedicated TrialFinderV2 base stack
        var vpc = CreateDedicatedVpcReference(context);

        // Import ALB stack outputs
        var albOutputs = ImportAlbStackOutputs();

        // Import Cognito stack outputs
        var cognitoOutputs = ImportCognitoStackOutputs();

        // Create ECS cluster
        var cluster = CreateEcsCluster(vpc, context);

        // Create ECR repositories from configuration (using service)
        _ecrRepositoryManager.CreateEcrRepositories(context);

        // Create GitHub Actions deployment role (using service)
        _githubActionsRole = _iamRoleBuilder.CreateGitHubActionsRole();

        // Create ECS service using the factory service
        _ecsServiceFactory.CreateServicesAndTasks(cluster, albOutputs, cognitoOutputs);

        // Export outputs using services
        _secretManager.ExportSecretArns();
        _ecrRepositoryManager.ExportEcrRepositoryOutputs();
    }

    /// <summary>
    /// Create VPC reference using dedicated TrialFinderV2 base stack exports
    /// </summary>
    private IVpc CreateDedicatedVpcReference(DeploymentContext context)
    {
        // Import VPC attributes from dedicated TrialFinderV2 base stack
        var vpcId = Fn.ImportValue($"tf2-{context.Environment.Name}-vpc-id");
        
        // Import individual subnet IDs (assuming 3 AZs)
        var publicSubnet1 = Fn.ImportValue($"tf2-{context.Environment.Name}-public-subnet-1-id");
        var publicSubnet2 = Fn.ImportValue($"tf2-{context.Environment.Name}-public-subnet-2-id");
        var publicSubnet3 = Fn.ImportValue($"tf2-{context.Environment.Name}-public-subnet-3-id");
        
        var privateSubnet1 = Fn.ImportValue($"tf2-{context.Environment.Name}-private-subnet-1-id");
        var privateSubnet2 = Fn.ImportValue($"tf2-{context.Environment.Name}-private-subnet-2-id");
        var privateSubnet3 = Fn.ImportValue($"tf2-{context.Environment.Name}-private-subnet-3-id");
        
        var isolatedSubnet1 = Fn.ImportValue($"tf2-{context.Environment.Name}-isolated-subnet-1-id");
        var isolatedSubnet2 = Fn.ImportValue($"tf2-{context.Environment.Name}-isolated-subnet-2-id");
        var isolatedSubnet3 = Fn.ImportValue($"tf2-{context.Environment.Name}-isolated-subnet-3-id");
        
        // Use VPC attributes to create reference with individual subnet IDs
        return Vpc.FromVpcAttributes(this, "TrialFinderV2DedicatedVpc", new VpcAttributes
        {
            VpcId = vpcId,
            VpcCidrBlock = "10.0.0.0/16", // Known CIDR from our base stack
            AvailabilityZones = new[] { "us-east-2a", "us-east-2b", "us-east-2c" },
            PublicSubnetIds = new[] { publicSubnet1, publicSubnet2, publicSubnet3 },
            PrivateSubnetIds = new[] { privateSubnet1, privateSubnet2, privateSubnet3 },
            IsolatedSubnetIds = new[] { isolatedSubnet1, isolatedSubnet2, isolatedSubnet3 }
        });
    }

    /// <summary>
    /// Import outputs from the ALB stack and base stack
    /// </summary>
    private AlbStackOutputs ImportAlbStackOutputs()
    {
        var targetGroupArn
            = Fn.ImportValue(
                $"{_context.Environment.Name}-{_context.Application.Name}-target-group-arn");
        
        // Import ECS security group directly from base stack instead of ALB stack
        var ecsSecurityGroupId
            = Fn.ImportValue($"tf2-{_context.Environment.Name}-ecs-sg-id");

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
        var userPoolArn = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-user-pool-arn");

        return new CognitoStackOutputs
        {
            UserPoolId = userPoolId,
            AppClientId = appClientId,
            DomainUrl = domainUrl,
            DomainName = domainName,
            UserPoolArn = userPoolArn
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
    /// Helper class to hold ALB stack outputs
    /// </summary>
    public class AlbStackOutputs
    {
        public string TargetGroupArn { get; set; } = "";
        public string EcsSecurityGroupId { get; set; } = "";
    }

    /// <summary>
    /// Helper class to hold Cognito stack outputs
    /// </summary>
    public class CognitoStackOutputs
    {
        public string UserPoolId { get; set; } = "";
        public string AppClientId { get; set; } = "";
        public string DomainUrl { get; set; } = "";
        public string DomainName { get; set; } = "";
        public string UserPoolArn { get; set; } = "";
    }
}