using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Builders;

/// <summary>
/// Manages security group configurations for the TrialFinderV2 application
/// 
/// This service handles:
/// - Security group import from other stacks
/// - Security group rule configuration
/// - Cross-stack security group references
/// </summary>
public class SecurityGroupManager : Construct
{
    private readonly DeploymentContext _context;

    public SecurityGroupManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Import security groups from other stacks
    /// </summary>
    public ISecurityGroup ImportEcsSecurityGroup(string securityGroupId)
    {
        return SecurityGroup.FromSecurityGroupId(this, "ImportedEcsSecurityGroup", securityGroupId);
    }

    /// <summary>
    /// Import shared database security group from EnvironmentBaseStack
    /// </summary>
    public ISecurityGroup ImportSharedDatabaseSecurityGroup()
    {
        var rdsSecurityGroupId = Fn.ImportValue($"{_context.Environment.Name}-sg-rds-id");
        return SecurityGroup.FromSecurityGroupId(this, "ImportedRdsSecurityGroup", rdsSecurityGroupId);
    }

    /// <summary>
    /// Configure security group rules for ECS tasks
    /// </summary>
    public void ConfigureSecurityGroupRules(ISecurityGroup ecsSecurityGroup, ISecurityGroup? databaseSecurityGroup = null)
    {
        // ECS security group should allow outbound traffic to database if needed
        if (databaseSecurityGroup != null)
        {
            // Allow ECS tasks to connect to database
            ecsSecurityGroup.AddEgressRule(
                databaseSecurityGroup,
                Port.Tcp(5432), // PostgreSQL default port
                "Allow ECS tasks to connect to database"
            );
        }

        // Allow all outbound traffic from ECS tasks
        ecsSecurityGroup.AddEgressRule(
            Peer.AnyIpv4(),
            Port.AllTraffic(),
            "Allow all outbound traffic from ECS tasks"
        );
    }

    /// <summary>
    /// Get ECS security group from ALB stack outputs
    /// </summary>
    public ISecurityGroup GetEcsSecurityGroup(string securityGroupId)
    {
        return SecurityGroup.FromSecurityGroupId(this, "EcsSecurityGroup", securityGroupId);
    }

    /// <summary>
    /// Create a new security group for ECS tasks if needed
    /// </summary>
    public ISecurityGroup CreateEcsSecurityGroup(IVpc vpc, string securityGroupName)
    {
        return new SecurityGroup(this, securityGroupName, new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = securityGroupName,
            Description = $"Security group for {_context.Application.Name} ECS tasks in {_context.Environment.Name}",
            AllowAllOutbound = true
        });
    }
}
