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
    public string EcsCluster(string purpose = "main")
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsCluster, purpose);
    }

    public string EcsTaskDefinition(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsTask, purpose);
    }

    public string EcsService(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.EcsService, purpose);
    }

    public string ApplicationLoadBalancer(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ApplicationLoadBalancer, purpose);
    }

    public string NetworkLoadBalancer(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.NetworkLoadBalancer, purpose);
    }

    public string Vpc(string purpose = "main")
    {
        return NamingConvention.GenerateResourceName(_context, NamingConvention.ResourceTypes.Vpc,
            purpose);
    }

    public string RdsInstance(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.RdsInstance, purpose);
    }

    public string ElastiCache(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ElastiCache, purpose);
    }

    public string Lambda(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.Lambda, purpose);
    }

    public string OpenSearch(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.OpenSearch, purpose);
    }

    public string ElasticFileSystem(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ElasticFileSystem, purpose);
    }

    // IAM resources
    public string IamRole(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamRole, purpose);
    }

    public string IamUser(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamUser, purpose);
    }

    public string IamPolicy(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.IamPolicy, purpose);
    }

    // Security groups with protected resource context
    public string SecurityGroupForAlb(string albPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.ApplicationLoadBalancer, albPurpose);
    }

    public string SecurityGroupForEcs(string ecsPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.EcsService, ecsPurpose);
    }

    public string SecurityGroupForRds(string rdsPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.RdsInstance, rdsPurpose);
    }

    public string SecurityGroupForCache(string cachePurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.ElastiCache, cachePurpose);
    }

    public string SecurityGroupForLambda(string lambdaPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.Lambda, lambdaPurpose);
    }

    public string SecurityGroupForOpenSearch(string searchPurpose)
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.OpenSearch, searchPurpose);
    }

    public string SecurityGroupForBastion(string purpose = "admin")
    {
        return NamingConvention.GenerateSecurityGroupName(_context,
            NamingConvention.SecurityGroupProtectedResources.BastionHost, purpose);
    }

    // S3 buckets with company domain
    public string S3Bucket(string purpose)
    {
        return NamingConvention.GenerateS3BucketName(_context, purpose);
    }

    // CloudWatch log groups
    public string LogGroup(string serviceType, string purpose)
    {
        return NamingConvention.GenerateLogGroupName(_context, serviceType, purpose);
    }

    // Messaging
    public string SnsTopics(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SnsTopics, purpose);
    }

    public string SqsQueue(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SqsQueue, purpose);
    }

    // Secrets and parameters
    public string SecretsManager(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.SecretsManager, purpose);
    }

    public string ParameterStore(string purpose)
    {
        return NamingConvention.GenerateResourceName(_context,
            NamingConvention.ResourceTypes.ParameterStore, purpose);
    }

    /// <summary>
    ///     Custom resource name for resources not covered by standard types
    /// </summary>
    public string Custom(string resourceType, string purpose)
    {
        return NamingConvention.GenerateResourceName(_context, resourceType, purpose);
    }
}