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

        // Load configuration including VPC name pattern
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dynamic lookup by name
        var vpc = CreateVpcReference(fullConfig.VpcNamePattern, context);

        // Import ALB stack outputs
        var albOutputs = ImportAlbStackOutputs();

        // Create ECR repositories for API and frontend
        _ecrRepositoryManager.CreateEcrRepositories(context);

        // Create ECS cluster
        _cluster = CreateEcsCluster(vpc, context);

        // Create ECS services with containers from configuration
        _ecsServiceFactory.CreateEcsServices(_cluster, albOutputs, context);

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