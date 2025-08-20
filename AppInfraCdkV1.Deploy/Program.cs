using Amazon.CDK;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.Stacks.Base;
using Microsoft.Extensions.Configuration;
using Environment = System.Environment;

namespace AppInfraCdkV1.Deploy;

public abstract class Program
{
    public static void Main(string[] args)
    {
        try
        {
            IConfiguration configuration = ConfigurationService.BuildConfiguration(args);
            string environmentName = ConfigurationService.GetEnvironmentName(args);
            string appName = ConfigurationService.GetApplicationName(args);
            bool validateOnly = ConfigurationService.HasFlag(args, "--validate-only");
            bool showNamesOnly = ConfigurationService.HasFlag(args, "--show-names-only");
            bool listEnvironments = ConfigurationService.HasFlag(args, "--list-environments");
            bool deployBase = ConfigurationService.HasFlag(args, "--deploy-base") || Environment.GetEnvironmentVariable("CDK_DEPLOY_BASE") == "true";

            if (listEnvironments)
            {
                DisplayService.DisplayAvailableEnvironments(configuration);
                return;
            }

            Console.WriteLine($"🚀 Starting CDK deployment for {appName} in {environmentName}");

            EnvironmentConfig environmentConfig = ConfigurationService.GetEnvironmentConfig(configuration, environmentName);
            ApplicationConfig applicationConfig = ConfigurationService.GetApplicationConfig(configuration, appName, environmentName);

            var context = new DeploymentContext
            {
                Environment = environmentConfig,
                Application = applicationConfig
            };

            ValidationService.ValidateNamingConventions(context);
            ValidationService.ValidateMultiEnvironmentSetupWithConfig(context, configuration);

            // If only validation requested, exit after validation
            if (validateOnly)
            {
                Console.WriteLine("✅ Validation completed successfully - no deployment performed");
                return;
            }

            // Show what names will be generated
            DisplayService.DisplayResourceNames(context);

            // Show account information for multi-environment context
            DisplayService.DisplayAccountContext(context);

            if (showNamesOnly) return;

            var app = new App();

            if (deployBase)
            {
                DeployBaseStack(app, context, environmentConfig, environmentName);
                return;
            }

            // Check if we should deploy a specific stack type
            var stackType = Environment.GetEnvironmentVariable("CDK_STACK_TYPE");
            
            if (!string.IsNullOrEmpty(stackType) && IsModularApplication(appName))
            {
                DeploySpecificStackType(app, stackType, context, environmentConfig, appName, environmentName);
                return;
            }

            // Require explicit stack type for supported applications - no monolithic deployments
            if (IsModularApplication(appName))
            {
                var supportedTypes = "ALB, ECS, DATA, COGNITO";
                throw new ArgumentException(
                    $"❌ For {appName}, you must specify a stack type using CDK_STACK_TYPE environment variable.\n" +
                    $"   Supported types: {supportedTypes}\n" +
                    $"   Example: CDK_STACK_TYPE=ECS dotnet run -- --app={appName} --environment={environmentName}\n" +
                    $"   This prevents accidental monolithic deployments and improves deployment speed."
                );
            }

            // Handle LakeFormation and other non-modular applications
            DeployLakeFormationOrOtherApp(app, appName, context, environmentConfig, environmentName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Deployment failed: {ex.Message}");
            if (ConfigurationService.HasFlag(args, "--verbose"))
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            Environment.Exit(1);
        }
    }

    private static void DeployBaseStack(App app, DeploymentContext context, EnvironmentConfig environmentConfig, string environmentName)
    {
        // Deploy base stack for shared environment resources
        string baseStackName = GenerateBaseStackName(context);
        var baseStack = new EnvironmentBaseStack(app, baseStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"Base infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
            Tags = context.GetCommonTags(),
            StackName = baseStackName
        }, context);
        
        Console.WriteLine($"✅ Base stack '{baseStackName}' configured successfully");
        app.Synth();
    }

    private static void DeploySpecificStackType(App app, string stackType, DeploymentContext context, EnvironmentConfig environmentConfig, string appName, string environmentName)
    {
        // Deploy specific stack type for supported applications
        // Create all stacks to establish dependencies, but only deploy the requested one
        var (stack, stackName) = StackFactory.CreateAllStacksWithDependencies(app, stackType, context, environmentConfig, appName, environmentName);
        Console.WriteLine($"✅ {stackType} Stack '{stackName}' configured successfully");
        app.Synth();
    }

    private static void DeployLakeFormationOrOtherApp(App app, string appName, DeploymentContext context, EnvironmentConfig environmentConfig, string environmentName)
    {
        // LakeFormation deployment has been moved to a separate internal app
        // Deploy using: cd AppInfraCdkV1.InternalApps/LakeFormation && dotnet run
        if (appName.ToLower() == "lakeformation")
        {
            throw new ArgumentException(
                "❌ LakeFormation deployment has been moved to a separate internal application.\n" +
                "   To deploy LakeFormation stacks, use:\n" +
                "   cd AppInfraCdkV1.InternalApps/LakeFormation\n" +
                "   dotnet run -- --environment=Development\n"
            );
            // StackFactory.DeployLakeFormationStacks(app, context, environmentConfig, environmentName);
        }
        else
        {
            throw new ArgumentException($"Unknown application: {appName}. Supported applications: TrialFinderV2, TrialMatch");
        }
        
        app.Synth();
    }

    private static bool IsModularApplication(string appName)
    {
        var modularApps = new[] { "trialfinderv2", "trialmatch" };
        return modularApps.Contains(appName.ToLower());
    }

    private static string GenerateBaseStackName(DeploymentContext context)
    {
        var envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        var regionCode = NamingConvention.GetRegionCode(context.Environment.Region);
        
        return $"{envPrefix}-shared-stack-{regionCode}";
    }
}