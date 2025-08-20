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

            // Generate stack name using naming convention
            var envPrefix = NamingConvention.GetEnvironmentPrefix(environment);
            var regionCode = NamingConvention.GetRegionCode(region);
            var stackName = $"{envPrefix}-pto-public-{regionCode}";

            // Create the stack
            var stack = new PublicThirdOpinionStack(app, stackName, deploymentContext, new StackProps
            {
                Env = new Amazon.CDK.Environment
                {
                    Account = accountId,
                    Region = region
                },
                Description = $"PublicThirdOpinion infrastructure for {environment} environment",
                StackName = stackName
            });

            // Add tags
            Tags.Of(stack).Add("Application", "PublicThirdOpinion");
            Tags.Of(stack).Add("Environment", environment);
            Tags.Of(stack).Add("ManagedBy", "CDK");
            Tags.Of(stack).Add("Repository", "AppInfraCdkV1");

            app.Synth();
        }
    }
}