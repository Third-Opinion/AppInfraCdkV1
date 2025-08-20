using Amazon.CDK;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.PublicThirdOpinion.Configuration;
using AppInfraCdkV1.PublicThirdOpinion.Stacks;

namespace AppInfraCdkV1.PublicThirdOpinion
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            // Get environment from CDK context or environment variable
            var environment = app.Node.TryGetContext("environment")?.ToString() 
                ?? System.Environment.GetEnvironmentVariable("CDK_ENVIRONMENT") 
                ?? "Development";

            // Get region from CDK context or environment variable
            var region = app.Node.TryGetContext("region")?.ToString() 
                ?? System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION") 
                ?? "us-east-2";

            // Get account from CDK context or environment variable
            var accountId = app.Node.TryGetContext("account")?.ToString() 
                ?? System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT");

            // Configure account ID based on environment
            if (string.IsNullOrEmpty(accountId))
            {
                accountId = environment.ToLower() == "production" 
                    ? "442042533707"  // Production account
                    : "615299752206"; // Development account
            }

            System.Console.WriteLine($"🚀 Deploying PublicThirdOpinion to {environment} environment");
            System.Console.WriteLine($"   Account: {accountId}");
            System.Console.WriteLine($"   Region: {region}");

            // Create deployment context
            var applicationConfig = PublicThirdOpinionConfig.GetApplicationConfig();
            
            var environmentConfig = new EnvironmentConfig
            {
                Name = environment,
                AccountId = accountId,
                Region = region,
                AccountType = environment.ToLower() == "production" 
                    ? AccountType.Production 
                    : AccountType.NonProduction
            };

            var deploymentContext = new DeploymentContext
            {
                Application = applicationConfig,
                Environment = environmentConfig
            };

            // Check if we're deploying certificate stack or main stack
            var stackType = app.Node.TryGetContext("stack-type")?.ToString() 
                ?? System.Environment.GetEnvironmentVariable("PTO_STACK_TYPE") 
                ?? "main";

            // Generate stack name using naming convention
            var envPrefix = NamingConvention.GetEnvironmentPrefix(environment);
            var regionCode = NamingConvention.GetRegionCode(region);
            
            if (stackType.ToLower() == "certificate")
            {
                // Deploy certificate stack first for manual DNS validation
                var certStackName = $"{envPrefix}-pto-cert-{regionCode}";
                System.Console.WriteLine($"📜 Deploying Certificate Stack: {certStackName}");
                System.Console.WriteLine($"   This stack creates the certificate and hosted zone only.");
                System.Console.WriteLine($"   After deployment, configure DNS delegation and wait for validation.");
                
                var certStack = new CertificateStack(app, certStackName, deploymentContext, new StackProps
                {
                    Env = new Amazon.CDK.Environment
                    {
                        Account = accountId,
                        Region = region
                    },
                    Description = $"Certificate and DNS for PublicThirdOpinion {environment} environment",
                    StackName = certStackName
                });
                
                Tags.Of(certStack).Add("Application", "PublicThirdOpinion");
                Tags.Of(certStack).Add("Environment", environment);
                Tags.Of(certStack).Add("ManagedBy", "CDK");
                Tags.Of(certStack).Add("Repository", "AppInfraCdkV1");
                Tags.Of(certStack).Add("StackType", "Certificate");
            }
            else
            {
                // Deploy main stack (with or without certificate import)
                var mainStackName = $"{envPrefix}-pto-public-{regionCode}";
                var useCertificateStack = app.Node.TryGetContext("use-cert-stack")?.ToString()?.ToLower() == "true"
                    || System.Environment.GetEnvironmentVariable("PTO_USE_CERT_STACK")?.ToLower() == "true";
                
                if (useCertificateStack)
                {
                    System.Console.WriteLine($"🚀 Deploying Main Stack with Certificate Import: {mainStackName}");
                    System.Console.WriteLine($"   Using certificate from {envPrefix}-pto-cert-{regionCode}");
                }
                else
                {
                    System.Console.WriteLine($"🚀 Deploying Full Stack: {mainStackName}");
                }

                // Create the main stack
                var stack = new PublicThirdOpinionStack(app, mainStackName, deploymentContext, new StackProps
                {
                    Env = new Amazon.CDK.Environment
                    {
                        Account = accountId,
                        Region = region
                    },
                    Description = $"PublicThirdOpinion infrastructure for {environment} environment",
                    StackName = mainStackName
                }, useCertificateStack);

                // Add tags
                Tags.Of(stack).Add("Application", "PublicThirdOpinion");
                Tags.Of(stack).Add("Environment", environment);
                Tags.Of(stack).Add("ManagedBy", "CDK");
                Tags.Of(stack).Add("Repository", "AppInfraCdkV1");
                Tags.Of(stack).Add("StackType", "Main");
            }

            app.Synth();
        }
    }
}