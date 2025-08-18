using Amazon.CDK;
using AppInfraCdkV1.InternalApps.LakeFormation;
using AppInfraCdkV1.InternalApps.LakeFormation.Stacks;

namespace AppInfraCdkV1.InternalApps.LakeFormation;

public abstract class Program
{
    public static void Main(string[] args)
    {
        try
        {
            var app = new App();
            
            // Get environment from args or default to Development
            string environmentName = GetEnvironmentName(args);
            string accountId = GetAccountId(environmentName);
            
            Console.WriteLine($"🏞️ Deploying Lake Formation stacks for {environmentName}...");
            
            // Load LakeFormation specific configuration
            var config = LakeFormationEnvironmentConfigFactory.CreateConfig(environmentName, accountId);
            
            // Create environment for AWS CDK
            var awsEnvironment = new Amazon.CDK.Environment
            {
                Account = accountId,
                Region = config.Region
            };
            
            string envPrefix = environmentName.ToLower() == "production" ? "prod" : "dev";
            string regionCode = config.Region == "us-east-2" ? "ue2" : "uw1";
            
            // Create storage stack first
            var storageStackName = $"{envPrefix}-lf-storage-{regionCode}";
            var storageStack = new DataLakeStorageStack(app, storageStackName, new StackProps
            {
                Env = awsEnvironment,
                Description = $"Lake Formation data storage infrastructure for {environmentName}",
                StackName = storageStackName
            }, config);
            
            // Create setup stack (depends on storage)
            var setupStackName = $"{envPrefix}-lf-setup-{regionCode}";
            var setupStack = new LakeFormationSetupStack(app, setupStackName, new StackProps
            {
                Env = awsEnvironment,
                Description = $"Lake Formation setup and configuration for {environmentName}",
                StackName = setupStackName
            }, config, storageStack);
            
            // Create permissions stack (depends on setup)
            var permissionsStackName = $"{envPrefix}-lf-permissions-{regionCode}";
            var permissionsStack = new LakeFormationPermissionsStack(app, permissionsStackName, new StackProps
            {
                Env = awsEnvironment,
                Description = $"Lake Formation permissions for {environmentName}",
                StackName = permissionsStackName
            }, config, setupStack);
            
            // Create HealthLake test instance stack (depends on storage)
            if (config.HealthLake != null && config.HealthLake.Count > 0)
            {
                var healthLakeStackName = $"{envPrefix}-healthlake-test-{regionCode}";
                var healthLakeStack = new HealthLakeTestInstanceStack(app, healthLakeStackName, new StackProps
                {
                    Env = awsEnvironment,
                    Description = $"HealthLake test instances for {environmentName}",
                    StackName = healthLakeStackName
                }, config, storageStack);
            }
            
            Console.WriteLine($"✅ LakeFormation stacks configured successfully for {environmentName}");
            app.Synth();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Deployment failed: {ex.Message}");
            System.Environment.Exit(1);
        }
    }
    
    private static string GetEnvironmentName(string[] args)
    {
        // Check command line first
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--environment="))
            {
                return args[i].Substring("--environment=".Length);
            }
            if (args[i] == "--environment" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        
        // Check environment variable
        var envVar = System.Environment.GetEnvironmentVariable("CDK_ENVIRONMENT");
        if (!string.IsNullOrEmpty(envVar))
        {
            return envVar;
        }
        
        // Default to Development
        return "Development";
    }
    
    private static string GetAccountId(string environmentName)
    {
        return environmentName.ToLower() switch
        {
            "development" => "615299752206",
            "production" => "442042533707",
            _ => "615299752206"
        };
    }
}