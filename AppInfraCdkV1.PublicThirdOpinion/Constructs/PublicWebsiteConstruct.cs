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
using System.Linq;

namespace AppInfraCdkV1.PublicThirdOpinion.Constructs
{
    public class PublicWebsiteConstruct : Construct
    {
        public IBucket WebsiteBucket { get; private set; }
        public IDistribution CloudFrontDistribution { get; private set; }
        public IHostedZone HostedZone { get; private set; }
        public ICertificate Certificate { get; private set; }

        /// <summary>
        /// Creates public website infrastructure
        /// </summary>
        /// <param name="scope">CDK scope</param>
        /// <param name="id">Construct ID</param>
        /// <param name="context">Deployment context</param>
        /// <param name="existingCertificateArn">Optional: ARN of existing certificate from CertificateStack</param>
        /// <param name="existingHostedZoneId">Optional: ID of existing hosted zone from CertificateStack</param>
        public PublicWebsiteConstruct(Construct scope, string id, DeploymentContext context, 
            string? existingCertificateArn = null, string? existingHostedZoneId = null)
            : base(scope, id)
        {
            var resourceNamer = new ResourceNamer(context);
            var stack = Stack.Of(this);
            
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

            // Try to find existing hosted zone by domain name
            IHostedZone? existingZone = null;
            if (string.IsNullOrEmpty(existingHostedZoneId))
            {
                // For production, use the known hosted zone ID to avoid lookup issues
                if (domainName == "public.thirdopinion.io" && context.Environment.Name.ToLower() == "production")
                {
                    existingHostedZoneId = "Z09476051323EX0YIALJW";
                }
                // Skip lookup to avoid synthesis issues - will create new zone if not provided
            }

            // Use existing hosted zone if provided or found
            if (!string.IsNullOrEmpty(existingHostedZoneId))
            {
                HostedZone = Amazon.CDK.AWS.Route53.HostedZone.FromHostedZoneAttributes(this, "ImportedHostedZone", new HostedZoneAttributes
                {
                    HostedZoneId = existingHostedZoneId,
                    ZoneName = domainName
                });
            }
            else if (existingZone != null)
            {
                HostedZone = existingZone;
            }
            else
            {
                // Create hosted zone
                HostedZone = new HostedZone(this, "HostedZone", new HostedZoneProps
                {
                    ZoneName = domainName,
                    Comment = $"Hosted zone for {context.Application.Name} {context.Environment.Name}"
                });
            }

            // Try to find existing certificate by domain name
            ICertificate? existingCert = null;
            if (string.IsNullOrEmpty(existingCertificateArn))
            {
                // For production, use the known certificate ARN for public.thirdopinion.io
                if (domainName == "public.thirdopinion.io" && context.Environment.Name.ToLower() == "production")
                {
                    existingCertificateArn = "arn:aws:acm:us-east-1:442042533707:certificate/f7644724-861c-4a11-be2f-bc591d628fc2";
                }
                // Note: CDK doesn't provide a direct lookup for certificates by domain name
                // We would need to use custom resources or context lookups for this
                // For now, we use known certificate ARNs for specific domains
            }

            // Use existing certificate if provided or found
            if (!string.IsNullOrEmpty(existingCertificateArn))
            {
                Certificate = Amazon.CDK.AWS.CertificateManager.Certificate.FromCertificateArn(this, "ImportedCertificate", existingCertificateArn);
            }
            else if (existingCert != null)
            {
                Certificate = existingCert;
            }
            else
            {
                // Create ACM certificate for HTTPS
                // Note: For CloudFront, the certificate must be in us-east-1
                // We'll use DnsValidatedCertificate despite deprecation warning as it supports cross-region
                Certificate = new DnsValidatedCertificate(this, "Certificate", new DnsValidatedCertificateProps
                {
                    DomainName = domainName,
                    HostedZone = HostedZone,
                    SubjectAlternativeNames = new[] { $"*.{domainName}" },
                    // CloudFront requires certificates to be in us-east-1
                    Region = "us-east-1",
                    Validation = CertificateValidation.FromDns(HostedZone)
                });
            }

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