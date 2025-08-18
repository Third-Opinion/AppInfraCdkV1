using System.Linq;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
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
/// - Modular architecture using focused service classes
/// - Two-task architecture: web application + scheduled loader
/// - Automatic secret management with existence checking
/// - Container configuration from JSON files
/// - Integration with Application Load Balancer
/// - Environment-specific resource sizing
/// - Comprehensive IAM roles and permissions
/// 
/// Architecture:
/// - Web Application: Continuous ECS service with ALB integration
/// - Loader: ECS scheduled task using EventBridge rules
/// - All logic delegated to specialized service classes
/// </summary>
public class TrialFinderV2EcsStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    private readonly SecretManager _secretManager;
    private readonly EcrRepositoryManager _ecrRepositoryManager;
    private readonly EcsServiceFactory _ecsServiceFactory;
    private readonly SecurityGroupManager _securityGroupManager;
    private readonly IamRoleBuilder _iamRoleBuilder;
    private readonly OutputExporter _outputExporter;
    private readonly LoggingManager _loggingManager;
    private ICluster? _cluster;

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
        _outputExporter = new OutputExporter(this, "OutputExporter", context);
        _loggingManager = new LoggingManager(this, "LoggingManager", context);
        _ecsServiceFactory = new EcsServiceFactory(this, "EcsServiceFactory", context, _secretManager, _ecrRepositoryManager, _loggingManager, _outputExporter);
        _securityGroupManager = new SecurityGroupManager(this, "SecurityGroupManager", context);
        _iamRoleBuilder = new IamRoleBuilder(this, "IamRoleBuilder", context);

        // Load configuration including VPC name pattern
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dynamic lookup by name
        var vpc = CreateVpcReference(fullConfig.VpcNamePattern, context);

        // Import ALB stack outputs
        var albOutputs = ImportAlbStackOutputs();

        // Import Cognito stack outputs
        var cognitoOutputs = ImportCognitoStackOutputs();

        // Create ECS cluster
        _cluster = CreateEcsCluster(vpc, context);

        // Create ECR repositories for TrialFinder containers
        _ecrRepositoryManager.CreateEcrRepositories(context);

        // Create ECS services with containers from configuration
        _ecsServiceFactory.CreateServicesAndTasks(_cluster, albOutputs, cognitoOutputs);

        // Export secret ARNs for all created secrets
        _secretManager.ExportSecretArns();

        // Export cluster information
        _outputExporter.ExportClusterOutputs(_cluster);

        // Export ECR repository information
        _ecrRepositoryManager.ExportEcrRepositoryOutputs();

        // Create GitHub Actions ECS deployment role
        CreateGitHubActionsEcsDeployRole(context);
    }

    /// <summary>
    /// Create GitHub Actions ECS deployment role using IamRoleBuilder
    /// </summary>
    private void CreateGitHubActionsEcsDeployRole(DeploymentContext context)
    {
        var role = _iamRoleBuilder.CreateGitHubActionsRole();

        // Export the role ARN using OutputExporter service
        _outputExporter.ExportGitHubActionsEcsDeployRole(role);
    }

    /// <summary>
    /// Create VPC reference using shared stack exports
    /// </summary>
    private IVpc CreateVpcReference(string? vpcNamePattern, DeploymentContext context)
    {
        if (string.IsNullOrWhiteSpace(vpcNamePattern))
        {
            throw new ArgumentException("VPC name pattern is required for VPC lookup", nameof(vpcNamePattern));
        }

        // Import VPC attributes from shared stack
        var vpcId = Fn.ImportValue($"{context.Environment.Name}-vpc-id");
        var vpcCidr = Fn.ImportValue($"{context.Environment.Name}-vpc-cidr");
        var availabilityZones = Fn.ImportListValue($"{context.Environment.Name}-vpc-azs", 3);
        var publicSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-public-subnet-ids", 3);
        var privateSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-private-subnet-ids", 3);
        var isolatedSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-isolated-subnet-ids", 3);
        
        // Use VPC attributes to create reference
        var vpc = Vpc.FromVpcAttributes(this, "SharedVpc", new VpcAttributes
        {
            VpcId = vpcId,
            VpcCidrBlock = vpcCidr,
            AvailabilityZones = availabilityZones,
            PublicSubnetIds = publicSubnetIds,
            PrivateSubnetIds = privateSubnetIds,
            IsolatedSubnetIds = isolatedSubnetIds
        });

        Console.WriteLine($"🔗 Using VPC: {vpc.VpcId}");
        return vpc;
    }

    /// <summary>
    /// Import outputs from the ALB stack
    /// </summary>
    private AppInfraCdkV1.Apps.TrialFinderV2.Services.AlbStackOutputs ImportAlbStackOutputs()
    {
        var targetGroupArn
            = Fn.ImportValue(
                $"{_context.Environment.Name}-{_context.Application.Name}-target-group-arn");
        var ecsSecurityGroupId
            = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-ecs-sg-id");

        return new AppInfraCdkV1.Apps.TrialFinderV2.Services.AlbStackOutputs
        {
            TargetGroupArn = targetGroupArn,
            EcsSecurityGroupId = ecsSecurityGroupId
        };
    }

    /// <summary>
    /// Import outputs from the Cognito stack
    /// </summary>
    private AppInfraCdkV1.Apps.TrialFinderV2.Services.CognitoStackOutputs ImportCognitoStackOutputs()
    {
        var userPoolId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-user-pool-id");
        var appClientId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-app-client-id");
        var domainUrl = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-domain-url");
        var domainName = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-domain-name");
        var userPoolArn = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-user-pool-arn");

        return new AppInfraCdkV1.Apps.TrialFinderV2.Services.CognitoStackOutputs
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
        return new Cluster(this, "TrialFinderCluster", new ClusterProps
        {
            Vpc = vpc,
            ClusterName = context.Namer.EcsCluster()
        });
    }
}