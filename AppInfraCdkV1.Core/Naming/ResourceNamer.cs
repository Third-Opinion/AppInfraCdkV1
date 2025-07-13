using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Extensions;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.Naming;

/// <summary>
///     Fluent interface for generating resource names with naming convention enforcement
///     Think of this as a name generator that ensures consistency like a style guide
/// </summary>
public class ResourceNamer
{
    private readonly DeploymentContext _context;

    public ResourceNamer(DeploymentContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // Standard resources
    public string EcsCluster(ResourcePurpose purpose = ResourcePurpose.Main)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsCluster, purpose.ToStringValue());
    }

    public string EcsTaskDefinition(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsTask, purpose.ToStringValue());
    }

    public string EcsService(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsService, purpose.ToStringValue());
    }

    public string ApplicationLoadBalancer(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ApplicationLoadBalancer, purpose.ToStringValue());
    }

    public string NetworkLoadBalancer(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.NetworkLoadBalancer, purpose.ToStringValue());
    }

    public string Vpc(ResourcePurpose purpose = ResourcePurpose.Main)
    {
        return NamingConvention.GenerateResourceName(_context, NamingConvention.ResourceTypes.Vpc,
            purpose.ToStringValue());
    }

    public string RdsInstance(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.RdsInstance, purpose.ToStringValue());
    }

    public string ElastiCache(StoragePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ElastiCache, purpose.ToStringValue());
    }

    public string Lambda(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.Lambda, purpose.ToStringValue());
    }

    public string OpenSearch(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.OpenSearch, purpose.ToStringValue());
    }

    public string ElasticFileSystem(StoragePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ElasticFileSystem, purpose.ToStringValue());
    }

    // IAM resources
    public string IamRole(IamPurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamRole, purpose.ToStringValue());
    }

    public string IamUser(IamPurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamUser, purpose.ToStringValue());
    }

    public string IamPolicy(IamPurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamPolicy, purpose.ToStringValue());
    }

    // Security groups with protected resource context
    public string SecurityGroupForAlb(ResourcePurpose albPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.ApplicationLoadBalancer, albPurpose.ToStringValue());
    }

    public string SecurityGroupForEcs(ResourcePurpose ecsPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.EcsService, ecsPurpose.ToStringValue());
    }

    public string SecurityGroupForRds(ResourcePurpose rdsPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.RdsInstance, rdsPurpose.ToStringValue());
    }

    public string SecurityGroupForCache(StoragePurpose cachePurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.ElastiCache, cachePurpose.ToStringValue());
    }

    public string SecurityGroupForLambda(ResourcePurpose lambdaPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.Lambda, lambdaPurpose.ToStringValue());
    }

    public string SecurityGroupForOpenSearch(ResourcePurpose searchPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.OpenSearch, searchPurpose.ToStringValue());
    }

    public string SecurityGroupForBastion(ResourcePurpose purpose = ResourcePurpose.Admin)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.BastionHost, purpose.ToStringValue());
    }

    // S3 buckets with company domain
    public string S3Bucket(StoragePurpose purpose)
    {
        return NamingConvention.GenerateS3BucketName(_context, purpose.ToStringValue());
    }

    // CloudWatch log groups
    public string LogGroup(string serviceType, ResourcePurpose purpose)
    {
        return NamingConvention.GenerateLogGroupName(_context, serviceType, purpose.ToStringValue());
    }

    // Messaging
    public string SnsTopics(NotificationPurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SnsTopics, purpose.ToStringValue());
    }

    public string SqsQueue(QueuePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SqsQueue, purpose.ToStringValue());
    }

    // Secrets and parameters
    public string SecretsManager(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SecretsManager, purpose.ToStringValue());
    }

    public string ParameterStore(ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ParameterStore, purpose.ToStringValue());
    }

    // ECR repositories
    public string EcrRepository(string repositoryType)
    {
        var applicationName = _context.Application.Name.ToLowerInvariant();
        return $"thirdopinion/{applicationName}/{repositoryType}";
    }

    /// <summary>
    ///     Custom resource name for resources not covered by standard types
    /// </summary>
    public string Custom(string resourceType, ResourcePurpose purpose)
    {
        return NamingConvention.GenerateResourceName(_context, resourceType, purpose.ToStringValue());
    }

    // ARN Generation Methods
    
    /// <summary>
    /// Generates the expected ARN for an IAM role
    /// </summary>
    public string IamRoleArn(IamPurpose purpose)
    {
        var roleName = IamRole(purpose);
        return $"arn:aws:iam::{_context.Environment.AccountId}:role/{roleName}";
    }

    /// <summary>
    /// Generates the expected ARN for an IAM policy
    /// </summary>
    public string IamPolicyArn(IamPurpose purpose)
    {
        var policyName = IamPolicy(purpose);
        return $"arn:aws:iam::{_context.Environment.AccountId}:policy/{policyName}";
    }

    /// <summary>
    /// Generates the expected ARN for an IAM user
    /// </summary>
    public string IamUserArn(IamPurpose purpose)
    {
        var userName = IamUser(purpose);
        return $"arn:aws:iam::{_context.Environment.AccountId}:user/{userName}";
    }

    /// <summary>
    /// Generates the expected ARN for an S3 bucket
    /// </summary>
    public string S3BucketArn(StoragePurpose purpose)
    {
        var bucketName = S3Bucket(purpose);
        return $"arn:aws:s3:::{bucketName}";
    }

    /// <summary>
    /// Generates the expected ARN for an SQS queue
    /// </summary>
    public string SqsQueueArn(QueuePurpose purpose)
    {
        var queueName = SqsQueue(purpose);
        return $"arn:aws:sqs:{_context.Environment.Region}:{_context.Environment.AccountId}:{queueName}";
    }

    /// <summary>
    /// Generates the expected ARN for an SNS topic
    /// </summary>
    public string SnsTopicArn(NotificationPurpose purpose)
    {
        var topicName = SnsTopics(purpose);
        return $"arn:aws:sns:{_context.Environment.Region}:{_context.Environment.AccountId}:{topicName}";
    }

    // Shared resource naming methods
    
    /// <summary>
    /// Generates a shared VPC name using shared resource naming convention
    /// </summary>
    public string SharedVpc(string specificName = "main")
    {
        return NamingConvention.GenerateSharedResourceName(_context, NamingConvention.ResourceTypes.Vpc, specificName);
    }

    /// <summary>
    /// Generates a shared security group name using shared resource naming convention
    /// </summary>
    public string SharedSecurityGroup(string specificName)
    {
        return NamingConvention.GenerateSharedResourceName(_context, NamingConvention.ResourceTypes.SecurityGroup, specificName);
    }

    /// <summary>
    /// Generates a shared log group name using shared resource naming convention
    /// Pattern: /aws/{service-type}/{env-prefix}-shared-{specific-name}
    /// </summary>
    public string SharedLogGroup(string serviceType = "shared", string specificName = "main")
    {
        string envPrefix = NamingConvention.GetEnvironmentPrefix(_context.Environment.Name);
        return $"/aws/{serviceType.ToLowerInvariant()}/{envPrefix}-shared-{specificName.ToLowerInvariant()}";
    }
}