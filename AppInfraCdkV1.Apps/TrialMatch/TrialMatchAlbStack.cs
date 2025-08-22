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

/// <summary>
/// ALB Stack for TrialMatch application
/// 
/// This stack manages Application Load Balancer with the following features:
/// - SSL/TLS termination with environment-specific certificates
/// - S3 access logging with lifecycle management
/// - Integration with dedicated TrialMatch base stack
/// - Security group management for ALB and ECS services
/// - Environment-specific resource configuration
/// 
/// Base Stack Integration:
/// This stack now uses the dedicated TrialMatch base stack for VPC and security groups,
/// ensuring complete isolation from other applications.
/// </summary>
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

        // Load configuration including base stack configuration
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create VPC reference using dedicated TrialMatch base stack
        var vpc = CreateDedicatedVpcReference(context);
        
        // Create security groups
        var securityGroups = CreateSecurityGroups(vpc, context);
        
        // Create ALB with S3 logging
        var alb = CreateApplicationLoadBalancer(vpc, securityGroups.AlbSecurityGroup, context);
        
        // Create target groups for ECS services
        var targetGroups = CreateTargetGroups(vpc, context);
        
        // Create ALB listeners (HTTP and HTTPS)
        CreateListeners(alb, targetGroups);
        
        // Export outputs for ECS stack consumption
        ExportStackOutputs(alb, targetGroups, securityGroups);
    }

    /// <summary>
    /// Import security groups from dedicated TrialMatch base stack
    /// </summary>
    private (ISecurityGroup AlbSecurityGroup, ISecurityGroup EcsSecurityGroup) CreateSecurityGroups(IVpc vpc, DeploymentContext context)
    {
        // Import ALB Security Group from dedicated TrialMatch base stack
        var albSecurityGroupId = Fn.ImportValue($"tm-{context.Environment.Name}-sg-alb-id");
        var albSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "TrialMatchAlbSecurityGroup", albSecurityGroupId);

        // Import ECS Security Group from dedicated TrialMatch base stack
        var ecsSecurityGroupId = Fn.ImportValue($"tm-{context.Environment.Name}-sg-ecs-id");
        var ecsSecurityGroup = SecurityGroup.FromSecurityGroupId(this, "TrialMatchEcsSecurityGroup", ecsSecurityGroupId);

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
    /// Create target groups for ECS services
    /// </summary>
    private Dictionary<string, IApplicationTargetGroup> CreateTargetGroups(IVpc vpc, DeploymentContext context)
    {
        var targetGroups = new Dictionary<string, IApplicationTargetGroup>();

        // Create API target group
        var apiTargetGroup = new ApplicationTargetGroup(this, "TrialMatchApiTargetGroup", new ApplicationTargetGroupProps
        {
            Port = 8080,
            Protocol = ApplicationProtocol.HTTP,
            Vpc = vpc,
            TargetGroupName = context.Namer.Custom("tg-api", ResourcePurpose.Web),
            TargetType = TargetType.IP
        });
        
        // Configure health check for API
        apiTargetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
        {
            Path = "/health",
            Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.Protocol.HTTP,
            Interval = Duration.Seconds(30),
            HealthyThresholdCount = 2,
            UnhealthyThresholdCount = 3,
            Port = "traffic-port"
        });

        // Create Frontend target group
        var frontendTargetGroup = new ApplicationTargetGroup(this, "TrialMatchFrontendTargetGroup", new ApplicationTargetGroupProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP,
            Vpc = vpc,
            TargetGroupName = context.Namer.Custom("tg-frontend", ResourcePurpose.Web),
            TargetType = TargetType.IP
        });
        
        // Configure health check for Frontend
        frontendTargetGroup.ConfigureHealthCheck(new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
        {
            Path = "/health",
            Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.Protocol.HTTP,
            Interval = Duration.Seconds(30),
            HealthyThresholdCount = 2,
            UnhealthyThresholdCount = 3,
            Port = "traffic-port"
        });

        targetGroups["api"] = apiTargetGroup;
        targetGroups["frontend"] = frontendTargetGroup;

        return targetGroups;
    }

    /// <summary>
    /// Create ALB listeners for HTTP and HTTPS with routing rules
    /// </summary>
    private void CreateListeners(IApplicationLoadBalancer alb, Dictionary<string, IApplicationTargetGroup> targetGroups)
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

        // Add routing rules for HTTPS listener - API routes
        httpsListener.AddTargetGroups("HttpsApiRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroups["api"] },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/api/*" }) },
            Priority = 100
        });

        // Add routing rules for HTTPS listener - Frontend routes
        httpsListener.AddTargetGroups("HttpsFrontendRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroups["frontend"] },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/*" }) },
            Priority = 200
        });

        // Default action for HTTPS - route to frontend
        httpsListener.AddAction("HttpsDefaultAction", new Amazon.CDK.AWS.ElasticLoadBalancingV2.AddApplicationActionProps
        {
            Action = ListenerAction.Forward(new[] { targetGroups["frontend"] })
        });

        // Create HTTP listener on port 80
        var httpListener = alb.AddListener("TrialMatchHttpListener", new Amazon.CDK.AWS.ElasticLoadBalancingV2.BaseApplicationListenerProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP
        });

        // Add routing rules for HTTP listener - API routes (matching HTTPS)
        httpListener.AddTargetGroups("HttpApiRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroups["api"] },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/api/*" }) },
            Priority = 100
        });

        // Add routing rules for HTTP listener - Frontend routes (matching HTTPS)
        httpListener.AddTargetGroups("HttpFrontendRule", new AddApplicationTargetGroupsProps
        {
            TargetGroups = new[] { targetGroups["frontend"] },
            Conditions = new[] { ListenerCondition.PathPatterns(new[] { "/*" }) },
            Priority = 200
        });

        // Default action for HTTP - route to frontend
        httpListener.AddAction("HttpDefaultAction", new Amazon.CDK.AWS.ElasticLoadBalancingV2.AddApplicationActionProps
        {
            Action = ListenerAction.Forward(new[] { targetGroups["frontend"] })
        });
    }

    /// <summary>
    /// Export stack outputs for ECS stack consumption
    /// </summary>
    private void ExportStackOutputs(IApplicationLoadBalancer alb, Dictionary<string, IApplicationTargetGroup> targetGroups, 
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
        new CfnOutput(this, "ApiTargetGroupArn", new CfnOutputProps
        {
            Value = targetGroups["api"].TargetGroupArn,
            Description = "TrialMatch API Target Group ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-api-target-group-arn"
        });

        new CfnOutput(this, "ApiTargetGroupName", new CfnOutputProps
        {
            Value = targetGroups["api"].TargetGroupName,
            Description = "TrialMatch API Target Group Name",
            ExportName = $"{_context.Environment.Name}-trial-match-api-target-group-name"
        });

        new CfnOutput(this, "FrontendTargetGroupArn", new CfnOutputProps
        {
            Value = targetGroups["frontend"].TargetGroupArn,
            Description = "TrialMatch Frontend Target Group ARN",
            ExportName = $"{_context.Environment.Name}-trial-match-frontend-target-group-arn"
        });

        new CfnOutput(this, "FrontendTargetGroupName", new CfnOutputProps
        {
            Value = targetGroups["frontend"].TargetGroupName,
            Description = "TrialMatch Frontend Target Group Name",
            ExportName = $"{_context.Environment.Name}-trial-match-frontend-target-group-name"
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
    /// Create VPC reference using dedicated TrialMatch base stack exports
    /// </summary>
    private IVpc CreateDedicatedVpcReference(DeploymentContext context)
    {
        // Import VPC attributes from dedicated TrialMatch base stack
        var vpcId = Fn.ImportValue($"tm-{context.Environment.Name}-vpc-id");
        var vpcCidr = Fn.ImportValue($"{context.Environment.Name}-vpc-cidr"); // Keep existing CIDR export for compatibility
        var availabilityZones = Fn.ImportListValue($"{context.Environment.Name}-vpc-azs", 3); // Keep existing AZ export for compatibility
        var publicSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-public-subnet-ids", 3); // Keep existing subnet exports for compatibility
        var privateSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-private-subnet-ids", 3);
        var isolatedSubnetIds = Fn.ImportListValue($"{context.Environment.Name}-isolated-subnet-ids", 3);
        
        // Use VPC attributes to create reference
        return Vpc.FromVpcAttributes(this, "TrialMatchDedicatedVpc", new VpcAttributes
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
        // Production certificates
        if (context.Environment.Name.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "arn:aws:acm:us-east-2:442042533707:certificate/cb2cde98-92db-4e3c-84db-7b869aa7627a", // tm.thirdopinion.io
                "arn:aws:acm:us-east-2:442042533707:certificate/9666bc78-d52e-499f-a6e8-ebc00d3e1cc3"  // *.thirdopinion.io
            );
        }
        
        // Development/Staging certificates - using same certificates as TrialFinderV2 for now
        return (
            "arn:aws:acm:us-east-2:615299752206:certificate/087ea311-2df9-4f71-afc1-b995a8576533",
            "arn:aws:acm:us-east-2:615299752206:certificate/e9d39d56-c08c-4880-9c1a-da8361ee4f3e"
        );
    }
} 