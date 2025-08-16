using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

/// <summary>
/// Manager for creating and managing security groups for ECS services
/// </summary>
public class SecurityGroupManager : Construct
{
    private readonly DeploymentContext _context;

    public SecurityGroupManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Import shared database security group
    /// </summary>
    public ISecurityGroup ImportSharedDatabaseSecurityGroup()
    {
        var securityGroupId = Fn.ImportValue($"{_context.Environment.Name}-shared-database-security-group-id");
        return SecurityGroup.FromSecurityGroupId(this, "SharedDatabaseSecurityGroup", securityGroupId);
    }

    /// <summary>
    /// Import ECS security group from ALB stack
    /// </summary>
    public ISecurityGroup ImportEcsSecurityGroup()
    {
        var securityGroupId = Fn.ImportValue($"{_context.Environment.Name}-trial-match-ecs-security-group-id");
        return SecurityGroup.FromSecurityGroupId(this, "EcsSecurityGroup", securityGroupId);
    }
}
