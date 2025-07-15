using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.S3;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public class TrialFinderV2AlbStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    
    public TrialFinderV2AlbStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();

        // Load configuration including VPC name pattern
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dynamic lookup by name
        var vpc = CreateVpcReference(fullConfig.VpcNamePattern, context);
        
        // Create security groups
        var securityGroups = CreateSecurityGroups(vpc, context);
        
        // Create ALB with S3 logging
        var alb = CreateApplicationLoadBalancer(vpc, securityGroups.AlbSecurityGroup, context);
        
        // Create target group for ECS service
        var targetGroup = CreateTargetGroup(vpc, context);
        
        // Create ALB listeners (HTTP and HTTPS)
        CreateListeners(alb, targetGroup);
        
        // Export outputs for ECS stack consumption
        ExportStackOutputs(alb, targetGroup, securityGroups);
    }

    /// <summary>
    /// Create security groups for ALB and ECS based on existing patterns
    /// </summary>
    private (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) CreateSecurityGroups(IVpc vpc, DeploymentContext context)
    {
        // ALB Security Group - allows HTTPS traffic from internet
        var albSecurityGroup = new SecurityGroup(this, "TrialFinderAlbSecurityGroup", new SecurityGroupProps
        {
            Vpc = vpc,
            SecurityGroupName = context.Namer.SecurityGroupForAlb(ResourcePurpose.Web),
            Description = "Security group for TrialFinder ALB - allows HTTPS from internet",
            AllowAllOutbound = true
        });

        // Allow HTTPS inbound from anywhere
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "HTTPS from internet");
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "HTTP from internet (redirect to HTTPS)");

        // Import existing ECS Security Group instead of creating new one
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "TrialFinderEcsSecurityGroup", "sg-0e7ddc9391c2220f4");

        // Allow ALB to communicate with ECS on port 8080
        ecsSecurityGroup.AddIngressRule(albSecurityGroup, Port.Tcp(8080), "Load balancer to target");

        return (albSecurityGroup, ecsSecurityGroup);
    }

    /// <summary>
    /// Create Application Load Balancer with S3 access logging
    /// </summary>
    private IApplicationLoadBalancer CreateApplicationLoadBalancer(IVpc vpc, ISecurityGroup securityGroup, DeploymentContext context)
    {
        // Create S3 bucket for ALB access logs
        var albLogsBucket = new Bucket(this, "TrialFinderAlbLogsBucket", new BucketProps
        {
            BucketName = context.Namer.Custom("alb-logs", ResourcePurpose.Web),
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
            LifecycleRules = new[]
            {
                new Amazon.CDK.AWS.S3.LifecycleRule
                {
                    Id = "DeleteOldLogs",
                    Enabled = true,
                    Expiration = Duration.Days(30)
                }
            }
        });

        // Create Application Load Balancer
        var alb = new ApplicationLoadBalancer(this, "TrialFinderAlb", new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancerProps
        {
            Vpc = vpc,
            LoadBalancerName = context.Namer.ApplicationLoadBalancer(ResourcePurpose.Web),
            InternetFacing = true,
            SecurityGroup = securityGroup,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC }
        });

        // Enable access logging to S3
        alb.LogAccessLogs(albLogsBucket, "trial-finder-alb-logs");

        return alb;
    }

    /// <summary>
    /// Create target group for ECS service
    /// </summary>
    private IApplicationTargetGroup CreateTargetGroup(IVpc vpc, DeploymentContext context)
    {
        var targetGroup = new ApplicationTargetGroup(this, "TrialFinderTargetGroup", new ApplicationTargetGroupProps
        {
            Port = 8080,
            Protocol = ApplicationProtocol.HTTP,
            Vpc = vpc,
            TargetGroupName = context.Namer.Custom("tg", ResourcePurpose.Web),
            TargetType = TargetType.IP
        });
        
        // Configure health check
        targetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
        {
            Path = "/",
            Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.Protocol.HTTP,
            Interval = Duration.Seconds(30),
            HealthyThresholdCount = 2,
            UnhealthyThresholdCount = 3,
            Port = "traffic-port"
        });

        return targetGroup;
    }

    /// <summary>
    /// Create ALB listeners for HTTP and HTTPS with routing rules
    /// </summary>
    private void CreateListeners(IApplicationLoadBalancer alb, IApplicationTargetGroup targetGroup)
    {
        // Import SSL certificates
        var defaultCert = Certificate.FromCertificateArn(this, "DefaultCert", 
            "arn:aws:acm:us-east-2:615299752206:certificate/087ea311-2df9-4f71-afc1-b995a8576533");
        var sniCert = Certificate.FromCertificateArn(this, "SniCert", 
            "arn:aws:acm:us-east-2:615299752206:certificate/e9d39d56-c08c-4880-9c1a-da8361ee4f3e");

        // Create HTTPS listener on port 443
        var httpsListener = alb.AddListener("TrialFinderHttpsListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
        {
            Port = 443,
            Protocol = ApplicationProtocol.HTTPS,
            Certificates = new[] { ListenerCertificate.FromArn(defaultCert.CertificateArn) },
            SslPolicy = SslPolicy.RECOMMENDED_TLS
        });

        // Add SNI certificate
        httpsListener.AddCertificates("SniCertificates", new[] { ListenerCertificate.FromArn(sniCert.CertificateArn) });

        // Add routing rules for HTTPS listener
        httpsListener.AddTargetGroups("HttpsAppRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroup },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/app/*" }) },
            Priority = 100
        });

        // Default action for HTTPS
        httpsListener.AddAction("HttpsDefaultAction", new Amazon.CDK.AWS.ElasticLoadBalancingV2.AddApplicationActionProps
        {
            Action = ListenerAction.Forward(new[] { targetGroup })
        });

        // Create HTTP listener on port 80
        // Keep original logical ID to maintain existing resource
        var httpListener = alb.AddListener("TrialFinderListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP
        });

        // Add routing rules for HTTP listener (matching HTTPS)
        httpListener.AddTargetGroups("HttpAppRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroup },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/app/*" }) },
            Priority = 100
        });

        // Default action for HTTP
        httpListener.AddAction("HttpDefaultAction", new Amazon.CDK.AWS.ElasticLoadBalancingV2.AddApplicationActionProps
        {
            Action = ListenerAction.Forward(new[] { targetGroup })
        });
    }

    /// <summary>
    /// Export stack outputs for consumption by other stacks
    /// </summary>
    private void ExportStackOutputs(IApplicationLoadBalancer alb, IApplicationTargetGroup targetGroup, 
        (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) securityGroups)
    {
        // Export ALB ARN
        new CfnOutput(this, "AlbArn", new CfnOutputProps
        {
            Value = alb.LoadBalancerArn,
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-alb-arn",
            Description = "ARN of the Application Load Balancer"
        });

        // Export Target Group ARN
        new CfnOutput(this, "TargetGroupArn", new CfnOutputProps
        {
            Value = targetGroup.TargetGroupArn,
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-target-group-arn",
            Description = "ARN of the Target Group"
        });

        // Export ALB Security Group ID
        new CfnOutput(this, "AlbSecurityGroupId", new CfnOutputProps
        {
            Value = securityGroups.AlbSecurityGroup.SecurityGroupId,
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-alb-sg-id",
            Description = "ID of the ALB Security Group"
        });

        // Export ECS Security Group ID
        new CfnOutput(this, "EcsSecurityGroupId", new CfnOutputProps
        {
            Value = securityGroups.EcsSecurityGroup.SecurityGroupId,
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-ecs-sg-id",
            Description = "ID of the ECS Security Group"
        });

        // Export ALB DNS Name
        new CfnOutput(this, "AlbDnsName", new CfnOutputProps
        {
            Value = alb.LoadBalancerDnsName,
            ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-alb-dns",
            Description = "DNS name of the Application Load Balancer"
        });
    }

    /// <summary>
    /// Create VPC reference using dynamic lookup by name or fallback to hardcoded ID
    /// </summary>
    private IVpc CreateVpcReference(string vpcNamePattern, DeploymentContext context)
    {
        // Use dynamic lookup by VPC name tag
        return Vpc.FromLookup(this, "ExistingVpc", new VpcLookupOptions
        {
            Tags = new Dictionary<string, string>
            {
                { "Name", vpcNamePattern }
            }
        });
    }
}