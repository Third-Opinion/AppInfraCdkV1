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

namespace AppInfraCdkV1.Apps.TrialMatch;

public class TrialMatchStack : WebApplicationStack
{
    private readonly DeploymentContext _context;
    
    public TrialMatchStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props, context)
    {
        _context = context;
        
        // This stack should no longer be used for monolithic deployments
        // Individual stacks (ALB, ECS, DATA) should be used instead
        throw new InvalidOperationException(
            "TrialMatchStack is deprecated. Use individual stacks (TrialMatchAlbStack, TrialMatchEcsStack, TrialMatchDataStack) instead.");
    }

    // All methods below are deprecated - use individual stacks instead
    private void DeployIndividualStack(string stackType, DeploymentContext context)
    {
        // This method is intentionally empty - use individual stacks instead
    }

    private void CreateTrialMatchSpecificResources(DeploymentContext context)
    {
        // This method is intentionally empty - use individual stacks instead
    }
} 