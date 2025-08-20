using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.CertificateManager;
using Constructs;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.Core.Enums;
using System.Collections.Generic;

namespace AppInfraCdkV1.PublicThirdOpinion.Constructs
{
    public class PublicWebsiteConstruct : Construct
    {
        public IBucket WebsiteBucket { get; private set; }
        public IDistribution CloudFrontDistribution { get; private set; }
        public IHostedZone HostedZone { get; private set; }
        public ICertificate Certificate { get; private set; }

        public PublicWebsiteConstruct(Construct scope, string id, DeploymentContext context)
            : base(scope, id)
        {
            var resourceNamer = new ResourceNamer(context);
            
            // Determine domain name based on environment
            var domainName = context.Environment.Name.ToLower() == "production" 
                ? "public.thirdopinion.io" 
                : "dev-public.thirdopinion.io";

            // Create S3 bucket for static website hosting using ResourceNamer
            var bucketName = resourceNamer.S3Bucket(StoragePurpose.PublicWebsite);
            WebsiteBucket = new Bucket(this, "WebsiteBucket", new BucketProps
            {
                BucketName = bucketName,
                WebsiteIndexDocument = "index.html",
                WebsiteErrorDocument = "error.html",
                PublicReadAccess = false, // Will use CloudFront OAI
                BlockPublicAccess = new BlockPublicAccess(new BlockPublicAccessOptions
                {
                    BlockPublicAcls = false,
                    BlockPublicPolicy = false,
                    IgnorePublicAcls = false,
                    RestrictPublicBuckets = false
                }),
                RemovalPolicy = RemovalPolicy.RETAIN,
                Versioned = true,
                Cors = new[]
                {
                    new CorsRule
                    {
                        AllowedMethods = new[] { HttpMethods.GET, HttpMethods.HEAD },
                        AllowedOrigins = new[] { "*" },
                        AllowedHeaders = new[] { "*" },
                        MaxAge = 3000
                    }
                }
            });

            // Create Origin Access Identity for CloudFront
            var oai = new OriginAccessIdentity(this, "OAI", new OriginAccessIdentityProps
            {
                Comment = $"OAI for {domainName}"
            });

            // Grant read permissions to CloudFront OAI
            WebsiteBucket.GrantRead(oai);

            // Create hosted zone
            HostedZone = new HostedZone(this, "HostedZone", new HostedZoneProps
            {
                ZoneName = domainName,
                Comment = $"Hosted zone for {context.Application.Name} {context.Environment.Name}"
            });

            // Create ACM certificate for HTTPS
            Certificate = new Certificate(this, "Certificate", new CertificateProps
            {
                DomainName = domainName,
                Validation = CertificateValidation.FromDns(HostedZone),
                SubjectAlternativeNames = new[] { $"*.{domainName}" }
            });

            // Create CloudFront distribution using ResourceNamer for custom naming
            var distributionName = resourceNamer.Custom("cloudfront", ResourcePurpose.PublicWebsite);
            CloudFrontDistribution = new Distribution(this, "Distribution", new DistributionProps
            {
                Comment = distributionName,
                DefaultBehavior = new BehaviorOptions
                {
                    Origin = S3BucketOrigin.WithOriginAccessIdentity(WebsiteBucket, new S3BucketOriginWithOAIProps
                    {
                        OriginAccessIdentity = oai
                    }),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                    CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                    Compress = true
                },
                DomainNames = new[] { domainName },
                Certificate = Certificate,
                DefaultRootObject = "index.html",
                ErrorResponses = new[]
                {
                    new ErrorResponse
                    {
                        HttpStatus = 403,
                        ResponseHttpStatus = 200,
                        ResponsePagePath = "/index.html",
                        Ttl = Duration.Minutes(5)
                    },
                    new ErrorResponse
                    {
                        HttpStatus = 404,
                        ResponseHttpStatus = 404,
                        ResponsePagePath = "/error.html",
                        Ttl = Duration.Minutes(5)
                    }
                },
                PriceClass = PriceClass.PRICE_CLASS_100,
                Enabled = true,
                HttpVersion = HttpVersion.HTTP2_AND_3,
                MinimumProtocolVersion = SecurityPolicyProtocol.TLS_V1_2_2021
            });

            // Create A record pointing to CloudFront
            new ARecord(this, "SiteAliasRecord", new ARecordProps
            {
                RecordName = domainName,
                Zone = HostedZone,
                Target = RecordTarget.FromAlias(new CloudFrontTarget(CloudFrontDistribution))
            });


            // Output important values
            new CfnOutput(this, "BucketName", new CfnOutputProps
            {
                Value = WebsiteBucket.BucketName,
                Description = "Name of the S3 bucket"
            });

            new CfnOutput(this, "DistributionId", new CfnOutputProps
            {
                Value = CloudFrontDistribution.DistributionId,
                Description = "CloudFront Distribution ID"
            });

            new CfnOutput(this, "DistributionDomainName", new CfnOutputProps
            {
                Value = CloudFrontDistribution.DistributionDomainName,
                Description = "CloudFront Distribution Domain Name"
            });

            new CfnOutput(this, "WebsiteUrl", new CfnOutputProps
            {
                Value = $"https://{domainName}",
                Description = "Website URL"
            });

            new CfnOutput(this, "HostedZoneId", new CfnOutputProps
            {
                Value = HostedZone.HostedZoneId,
                Description = "Route53 Hosted Zone ID"
            });
        }
    }
}