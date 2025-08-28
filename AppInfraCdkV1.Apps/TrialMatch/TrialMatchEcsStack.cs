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
using AppInfraCdkV1.Apps.TrialMatch.Services;
using AppInfraCdkV1.Apps.TrialMatch.Builders;
using AppInfraCdkV1.Core.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;
using System.Text.RegularExpressions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.ECR;
using Amazon.ECR.Model;
using System.Threading.Tasks;

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
/// - Dedicated base stack infrastructure for complete isolation
/// 
/// Secret Management:
/// The stack checks if secrets already exist in AWS Secrets Manager before creating new ones.
/// This prevents CDK from attempting to recreate secrets that already exist, which would cause
/// deployment failures. Existing secrets are imported and referenced, while missing secrets
/// are created with generated values.
/// 
/// Base Stack Integration:
/// This stack now uses the dedicated TrialMatch base stack for VPC, security groups,
/// database, and other shared infrastructure, ensuring complete isolation from other applications.
/// </summary>
public class TrialMatchEcsStack : Stack
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

    public TrialMatchEcsStack(Construct scope,
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
        _outputExporter = new OutputExporter(this, context);
        _loggingManager = new LoggingManager(this, context);
        _ecsServiceFactory = new EcsServiceFactory(this, "EcsServiceFactory", context, _secretManager, _ecrRepositoryManager, _loggingManager, _outputExporter);
        _securityGroupManager = new SecurityGroupManager(this, "SecurityGroupManager", context);
        _iamRoleBuilder = new IamRoleBuilder(this, "IamRoleBuilder", context);

        // Load configuration including base stack configuration
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dedicated TrialMatch base stack
        var vpc = CreateDedicatedVpcReference(context);

        // Import ALB stack outputs
        var albOutputs = ImportAlbStackOutputs();

        // Import Cognito stack outputs for frontend environment variables
        var cognitoOutputs = ImportCognitoStackOutputs();

        // Create ECR repositories for API and frontend
        _ecrRepositoryManager.CreateEcrRepositories(context);

        // Create ECS cluster
        _cluster = CreateEcsCluster(vpc, context);

        // Create ECS services with containers from configuration
        _ecsServiceFactory.CreateEcsServices(_cluster, albOutputs, cognitoOutputs, context);

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
    /// Create VPC reference using dedicated TrialMatch base stack exports
    /// </summary>
    private IVpc CreateDedicatedVpcReference(DeploymentContext context)
    {
        // Import VPC attributes from dedicated TrialMatch base stack
        var vpcId = Fn.ImportValue($"tm-{context.Environment.Name}-vpc-id");
        
        // Import subnet IDs from TrialMatch base stack
        var publicSubnetIds = new[]
        {
            Fn.ImportValue($"tm-{context.Environment.Name}-public-subnet-1-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-public-subnet-2-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-public-subnet-3-id")
        };
        
        var privateSubnetIds = new[]
        {
            Fn.ImportValue($"tm-{context.Environment.Name}-private-subnet-1-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-private-subnet-2-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-private-subnet-3-id")
        };
        
        var isolatedSubnetIds = new[]
        {
            Fn.ImportValue($"tm-{context.Environment.Name}-isolated-subnet-1-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-isolated-subnet-2-id"),
            Fn.ImportValue($"tm-{context.Environment.Name}-isolated-subnet-3-id")
        };
        
        // Use VPC attributes to create reference
        var vpc = Vpc.FromVpcAttributes(this, "TrialMatchDedicatedVpc", new VpcAttributes
        {
            VpcId = vpcId,
            VpcCidrBlock = "10.1.0.0/16", // TrialMatch VPC CIDR
            AvailabilityZones = new[] { "us-east-2a", "us-east-2b", "us-east-2c" },
            PublicSubnetIds = publicSubnetIds,
            PrivateSubnetIds = privateSubnetIds,
            IsolatedSubnetIds = isolatedSubnetIds
        });

        Console.WriteLine($"🔗 Using dedicated TrialMatch VPC: {vpc.VpcId}");
        return vpc;
    }

    /// <summary>
    /// Import ALB stack outputs
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
    /// Import Cognito stack outputs for frontend environment variables
    /// </summary>
    private CognitoStackOutputs ImportCognitoStackOutputs()
    {
        return new CognitoStackOutputs
        {
            UserPoolId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-user-pool-id"),
            UserPoolClientId = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-app-client-id"),
            UserPoolDomain = Fn.ImportValue($"{_context.Environment.Name}-{_context.Application.Name}-cognito-domain-name")
        };
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
    /// Create GitHub Actions ECS deployment role using IamRoleBuilder
    /// </summary>
    private void CreateGitHubActionsEcsDeployRole(DeploymentContext context)
    {
        var role = _iamRoleBuilder.CreateGitHubActionsEcsDeployRole(this);

        // Export the role ARN using OutputExporter service
        _outputExporter.ExportGitHubActionsEcsDeployRole(role);
    }
} 