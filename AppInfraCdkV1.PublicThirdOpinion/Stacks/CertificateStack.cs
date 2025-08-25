using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.Route53;
using Constructs;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.PublicThirdOpinion.Stacks
{
    /// <summary>
    /// Separate stack for certificate creation to allow manual DNS validation
    /// Deploy this stack first, validate DNS, then deploy main stack
    /// </summary>
    public class CertificateStack : Stack
    {
        public ICertificate Certificate { get; private set; }
        public IHostedZone HostedZone { get; private set; }
        
        public CertificateStack(Construct scope, string id, DeploymentContext context, IStackProps? props = null)
            : base(scope, id, props)
        {
            var resourceNamer = new ResourceNamer(context);
            
            // Determine domain name based on environment
            var domainName = context.Environment.Name.ToLower() == "production" 
                ? "public.thirdopinion.io"
                : $"dev-public.thirdopinion.io";

            // Create hosted zone
            HostedZone = new HostedZone(this, "HostedZone", new HostedZoneProps
            {
                ZoneName = domainName,
                Comment = $"Hosted zone for {context.Application.Name} {context.Environment.Name}"
            });

            // Create ACM certificate for HTTPS in us-east-1 (required for CloudFront)
            // Using Certificate with cross-region support
            Certificate = new Certificate(this, "Certificate", new CertificateProps
            {
                DomainName = domainName,
                SubjectAlternativeNames = new[] { $"*.{domainName}" },
                Validation = CertificateValidation.FromDns(HostedZone)
            });

            // Export values for use in main stack
            new CfnOutput(this, "CertificateArn", new CfnOutputProps
            {
                Value = Certificate.CertificateArn,
                Description = "Certificate ARN for CloudFront",
                ExportName = $"{context.Environment.Name}-PublicWebsite-CertificateArn"
            });

            new CfnOutput(this, "HostedZoneId", new CfnOutputProps
            {
                Value = HostedZone.HostedZoneId,
                Description = "Hosted Zone ID",
                ExportName = $"{context.Environment.Name}-PublicWebsite-HostedZoneId"
            });

            new CfnOutput(this, "HostedZoneName", new CfnOutputProps
            {
                Value = HostedZone.ZoneName,
                Description = "Hosted Zone Name",
                ExportName = $"{context.Environment.Name}-PublicWebsite-HostedZoneName"
            });

            new CfnOutput(this, "NameServers", new CfnOutputProps
            {
                Value = Fn.Join(",", HostedZone.HostedZoneNameServers ?? new string[0]),
                Description = "Name servers for DNS delegation"
            });

            new CfnOutput(this, "ValidationStatus", new CfnOutputProps
            {
                Value = "IMPORTANT: Add NS records to parent domain and wait for certificate validation before deploying main stack",
                Description = "Next steps"
            });
        }
    }
}