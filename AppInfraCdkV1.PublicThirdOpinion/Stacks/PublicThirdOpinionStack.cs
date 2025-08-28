using Amazon.CDK;
using Constructs;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.PublicThirdOpinion.Constructs;
using System.Collections.Generic;

namespace AppInfraCdkV1.PublicThirdOpinion.Stacks
{
    public class PublicThirdOpinionStack : Stack
    {
        public PublicWebsiteConstruct PublicWebsite { get; private set; }
        public JwkLambdaConstruct JwkLambda { get; private set; }

        public PublicThirdOpinionStack(Construct scope, string id, DeploymentContext context, IStackProps? props = null, bool useCertificateStack = false)
            : base(scope, id, props)
        {
            // Add tags

            // Import certificate and hosted zone from CertificateStack if configured
            string? certificateArn = null;
            string? hostedZoneId = null;
            
            if (useCertificateStack)
            {
                // Import from cross-stack exports
                certificateArn = Fn.ImportValue($"{context.Environment.Name}-PublicWebsite-CertificateArn");
                hostedZoneId = Fn.ImportValue($"{context.Environment.Name}-PublicWebsite-HostedZoneId");
            }

            // Create the public website infrastructure
            PublicWebsite = new PublicWebsiteConstruct(this, "PublicWebsite", context, certificateArn, hostedZoneId);

            // Create the JWK Lambda function
            JwkLambda = new JwkLambdaConstruct(this, "JwkLambda", context, PublicWebsite.WebsiteBucket);

            // Stack outputs
            new CfnOutput(this, "StackName", new CfnOutputProps
            {
                Value = this.StackName,
                Description = "Stack name"
            });

            new CfnOutput(this, "Region", new CfnOutputProps
            {
                Value = this.Region,
                Description = "AWS Region"
            });
        }
    }
}