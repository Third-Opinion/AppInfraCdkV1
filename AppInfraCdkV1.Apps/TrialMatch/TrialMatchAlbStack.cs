using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.S3;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch;

public class TrialMatchAlbStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    
    public TrialMatchAlbStack(Construct scope,
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
    /// Import security groups from shared stack
    /// </summary>
    private (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) CreateSecurityGroups(IVpc vpc, DeploymentContext context)
    {
        // Import ALB Security Group from shared stack
        var albSecurityGroupId = Fn.ImportValue($"{context.Environment.Name}-sg-alb-id");
        var albSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "SharedAlbSecurityGroup", albSecurityGroupId);

        // Import ECS Security Group from shared stack
        var ecsSecurityGroupId = Fn.ImportValue($"{context.Environment.Name}-sg-ecs-id");
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "SharedEcsSecurityGroup", ecsSecurityGroupId);

        return (albSecurityGroup, ecsSecurityGroup);
    }

    /// <summary>
    /// Create Application Load Balancer with S3 access logging
    /// </summary>
    private IApplicationLoadBalancer CreateApplicationLoadBalancer(IVpc vpc, ISecurityGroup securityGroup, DeploymentContext context)
    {
        // Create S3 bucket for ALB access logs
        var albLogsBucket = new Bucket(this, "TrialMatchAlbLogsBucket", new BucketProps
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
        var alb = new ApplicationLoadBalancer(this, "TrialMatchAlb", new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancerProps
        {
            Vpc = vpc,
            LoadBalancerName = context.Namer.ApplicationLoadBalancer(ResourcePurpose.Web),
            InternetFacing = true,
            SecurityGroup = securityGroup,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC }
        });

        // Enable access logging to S3
        alb.LogAccessLogs(albLogsBucket, "trial-match-alb-logs");

        return alb;
    }

    /// <summary>
    /// Create target group for ECS service
    /// </summary>
    private IApplicationTargetGroup CreateTargetGroup(IVpc vpc, DeploymentContext context)
    {
        var targetGroup = new ApplicationTargetGroup(this, "TrialMatchTargetGroup", new ApplicationTargetGroupProps
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
            Path = "/health",
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
        // Get certificate ARNs based on environment
        var (defaultCertArn, sniCertArn) = GetCertificateArns(_context);
        
        // Import SSL certificates
        var defaultCert = Certificate.FromCertificateArn(this, "DefaultCert", defaultCertArn);
        var sniCert = Certificate.FromCertificateArn(this, "SniCert", sniCertArn);

        // Create HTTPS listener on port 443
        var httpsListener = alb.AddListener("TrialMatchHttpsListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
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
        var httpListener = alb.AddListener("TrialMatchHttpListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
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
    /// Export stack outputs for ECS stack consumption
    /// </summary>
    private void ExportStackOutputs(IApplicationLoadBalancer alb, IApplicationTargetGroup targetGroup, 
        (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) securityGroups)
    {
        // Export ALB outputs
        new CfnOutput(this, "AlbDnsName", new CfnOutputProps
        {
            Value = alb.LoadBalancerDnsName,
            Description = "TrialMatch Application Load Balancer DNS Name",
            ExportName = $"{_context.Environment.Name}-trial-match-alb-dns-name"
        });

        new CfnOutput(this, "AlbArn", new CfnOutputProps
        {
            Value = alb.LoadBalancerArn,
            Description = "TrialMatch Application Load Balancer ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-alb-arn"
        });

        // Export target group outputs
        new CfnOutput(this, "TargetGroupArn", new CfnOutputProps
        {
            Value = targetGroup.TargetGroupArn,
            Description = "TrialMatch Target Group ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-target-group-arn"
        });

        new CfnOutput(this, "TargetGroupName", new CfnOutputProps
        {
            Value = targetGroup.TargetGroupName,
            Description = "TrialMatch Target Group Name",
            ExportName = $"{_context.Environment.Name}-trial-match-target-group-name"
        });

        // Export security group outputs
        new CfnOutput(this, "AlbSecurityGroupId", new CfnOutputProps
        {
            Value = securityGroups.AlbSecurityGroup.SecurityGroupId,
            Description = "TrialMatch ALB Security Group ID",
            ExportName = $"{_context.Environment.Name}-trial-match-alb-security-group-id"
        });

        new CfnOutput(this, "EcsSecurityGroupId", new CfnOutputProps
        {
            Value = securityGroups.EcsSecurityGroup.SecurityGroupId,
            Description = "TrialMatch ECS Security Group ID",
            ExportName = $"{_context.Environment.Name}-trial-match-ecs-security-group-id"
        });
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
    /// Get certificate ARNs for HTTPS
    /// </summary>
    private (string defaultCertArn, string sniCertArn) GetCertificateArns(DeploymentContext context)
    {
        // Import certificate ARNs from shared stack
        var defaultCertArn = Fn.ImportValue($"{context.Environment.Name}-default-cert-arn");
        var sniCertArn = Fn.ImportValue($"{context.Environment.Name}-sni-cert-arn");

        return (defaultCertArn, sniCertArn);
    }
} 