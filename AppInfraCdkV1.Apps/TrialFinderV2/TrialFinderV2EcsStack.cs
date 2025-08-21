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
        _containerConfigurationService = new ContainerConfigurationService(this, "ContainerConfigurationService", context);
        
        // Initialize EcsServiceFactory with all dependencies
        _ecsServiceFactory = new EcsServiceFactory(this, "EcsServiceFactory", context, 
            _secretManager, _ecrRepositoryManager, _loggingManager, _outputExporter, _iamRoleBuilder, _containerConfigurationService);

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