using Amazon.CDK;
using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Apps.TrialMatch;
using AppInfraCdkV1.Core.Enums;
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
            IConfiguration configuration = BuildConfiguration(args);
            string environmentName = GetEnvironmentName(args);
            string appName = GetApplicationName(args);
            bool validateOnly = HasFlag(args, "--validate-only");
            bool showNamesOnly = HasFlag(args, "--show-names-only");
            bool listEnvironments = HasFlag(args, "--list-environments");
            bool deployBase = HasFlag(args, "--deploy-base") || Environment.GetEnvironmentVariable("CDK_DEPLOY_BASE") == "true";
            bool deployAppBase = HasFlag(args, "--deploy-app-base") || Environment.GetEnvironmentVariable("CDK_DEPLOY_APP_BASE") == "true";
            bool deployAllStacks = HasFlag(args, "--deploy-all-stacks") || Environment.GetEnvironmentVariable("CDK_DEPLOY_ALL_STACKS") == "true";

            if (listEnvironments)
            {
                DisplayAvailableEnvironments(configuration);
                return;
            }

            Console.WriteLine($"🚀 Starting CDK deployment for {appName} in {environmentName}");

            EnvironmentConfig environmentConfig = GetEnvironmentConfig(configuration, environmentName);
            ApplicationConfig applicationConfig = GetApplicationConfig(configuration, appName, environmentName);

            var context = new DeploymentContext
            {
                Environment = environmentConfig,
                Application = applicationConfig
            };

            ValidateNamingConventions(context);
            ValidateMultiEnvironmentSetupWithConfig(context, configuration);

            // If only validation requested, exit after validation
            if (validateOnly)
            {
                Console.WriteLine("✅ Validation completed successfully - no deployment performed");
                return;
            }

            // Show what names will be generated
            DisplayResourceNames(context);

            // Show account information for multi-environment context
            DisplayAccountContext(context);

            if (showNamesOnly) return;

            var app = new App();

            if (deployBase)
            {
                // Deploy shared base stack for shared environment resources
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
                return;
            }

            if (deployAppBase)
            {
                // Deploy application-specific base stack
                var (baseStack, baseStackName) = CreateApplicationBaseStack(app, context, environmentConfig, appName, environmentName);
                Console.WriteLine($"✅ Application base stack '{baseStackName}' configured successfully");
                app.Synth();
                return;
            }

            // Check if we should deploy a specific stack type
            var stackType = Environment.GetEnvironmentVariable("CDK_STACK_TYPE");
            
            if (!string.IsNullOrEmpty(stackType) && (appName.ToLower() == "trialfinderv2" || appName.ToLower() == "trialmatch"))
            {
                // Deploy specific stack type for supported applications
                var (stack, stackName) = CreateSpecificStack(app, stackType, context, environmentConfig, appName, environmentName);
                Console.WriteLine($"✅ {stackType} Stack '{stackName}' configured successfully");
                app.Synth();
                return;
            }

            if (deployAllStacks && (appName.ToLower() == "trialfinderv2" || appName.ToLower() == "trialmatch"))
            {
                // Deploy all stacks for one application
                var stacks = CreateAllApplicationStacks(app, context, environmentConfig, appName, environmentName);
                Console.WriteLine($"✅ All {stacks.Count} stacks for {appName} configured successfully");
                app.Synth();
                return;
            }

            // Require explicit stack type for supported applications - no monolithic deployments
            if (appName.ToLower() == "trialfinderv2" || appName.ToLower() == "trialmatch")
            {
                var supportedTypes = appName.ToLower() == "trialfinderv2" ? "ALB, ECS, DATA, COGNITO" : "ALB, ECS, DATA, COGNITO";
                throw new ArgumentException(
                    $"{appName} requires explicit stack type or deployment flag. Use one of:\n" +
                    $"  --stack-type=<TYPE> for specific stack (ALB, ECS, DATA, COGNITO)\n" +
                    $"  --deploy-all-stacks for all application stacks\n" +
                    $"  --deploy-app-base for application base stack\n" +
                    $"  --deploy-base for shared base stack");
            }

            // Default behavior for other applications (if any)
            throw new ArgumentException(
                $"Unknown application: {appName}. Register new applications in NamingConvention.cs");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Deployment failed: {ex.Message}");
            if (ex.InnerException != null)
                Console.Error.WriteLine($"   Inner exception: {ex.InnerException.Message}");

            if (ex.Message.Contains("Unknown environment") ||
                ex.Message.Contains("Unknown application") ||
                ex.Message.Contains("Unknown region")) ShowNamingHelp();

            Environment.Exit(1);
        }
    }

    private static (Stack stack, string stackName) CreateSpecificStack(
        App app, 
        string stackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string appName, 
        string environmentName)
    {
        var envPrefix = NamingConvention.GetEnvironmentPrefix(environmentName);
        var appCode = NamingConvention.GetApplicationCode(appName);
        var regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
        
        switch (appName.ToLower())
        {
            case "trialfinderv2":
                return CreateTrialFinderV2Stack(app, stackType, context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode);
            case "trialmatch":
                return CreateTrialMatchStack(app, stackType, context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode);
            default:
                throw new ArgumentException($"Unknown application: {appName}. Supported applications: TrialFinderV2, TrialMatch");
        }
    }

    private static (Stack stack, string stackName) CreateTrialFinderV2Stack(
        App app, 
        string stackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string appName, 
        string environmentName,
        string envPrefix,
        string appCode,
        string regionCode)
    {
        switch (stackType.ToUpper())
        {
            case "ALB":
                {
                    var stackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
                    var stack = new TrialFinderV2AlbStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialFinderV2 ALB infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "ECS":
                {
                    var stackName = $"{envPrefix}-{appCode}-ecs-{regionCode}";
                    var stack = new TrialFinderV2EcsStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialFinderV2 ECS infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "DATA":
                {
                    var stackName = $"{envPrefix}-{appCode}-data-{regionCode}";
                    var stack = new TrialFinderV2DataStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialFinderV2 Data infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "COGNITO":
                {
                    var stackName = $"{envPrefix}-{appCode}-cognito-{regionCode}";
                    var stack = new TrialFinderV2CognitoStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialFinderV2 Cognito infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            default:
                throw new ArgumentException($"Unknown TrialFinderV2 stack type: {stackType}. Supported types: ALB, ECS, DATA, COGNITO");
        }
    }

    private static (Stack stack, string stackName) CreateTrialMatchStack(
        App app, 
        string stackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string appName, 
        string environmentName,
        string envPrefix,
        string appCode,
        string regionCode)
    {
        switch (stackType.ToUpper())
        {
            case "ALB":
                {
                    var stackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
                    var stack = new TrialMatchAlbStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialMatch ALB infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "ECS":
                {
                    var stackName = $"{envPrefix}-{appCode}-ecs-{regionCode}";
                    var stack = new TrialMatchEcsStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialMatch ECS infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "DATA":
                {
                    var stackName = $"{envPrefix}-{appCode}-data-{regionCode}";
                    var stack = new TrialMatchDataStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialMatch Data infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            case "COGNITO":
                {
                    var stackName = $"{envPrefix}-{appCode}-cognito-{regionCode}";
                    var stack = new TrialMatchCognitoStack(app, stackName, new StackProps
                    {
                        Env = environmentConfig.ToAwsEnvironment(),
                        Description = $"TrialMatch Cognito infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                        Tags = context.GetCommonTags(),
                        StackName = stackName
                    }, context);
                    return (stack, stackName);
                }
            default:
                throw new ArgumentException($"Unknown TrialMatch stack type: {stackType}. Supported types: ALB, ECS, DATA, COGNITO");
        }
    }

    private static (Stack stack, string stackName) CreateApplicationBaseStack(
        App app,
        DeploymentContext context,
        EnvironmentConfig environmentConfig,
        string appName,
        string environmentName)
    {
        var envPrefix = NamingConvention.GetEnvironmentPrefix(environmentName);
        var appCode = NamingConvention.GetApplicationCode(appName);
        var regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
        
        string baseStackName = $"{envPrefix}-{appCode}-base-{regionCode}";
        
        switch (appName.ToLower())
        {
            case "trialfinderv2":
                var tf2BaseStack = new TrialFinderV2BaseStack(app, baseStackName, new StackProps
                {
                    Env = environmentConfig.ToAwsEnvironment(),
                    Description = $"TrialFinderV2 base infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                    Tags = context.GetCommonTags(),
                    StackName = baseStackName
                }, context);
                return (tf2BaseStack, baseStackName);
                
            case "trialmatch":
                var tmBaseStack = new TrialMatchBaseStack(app, baseStackName, new StackProps
                {
                    Env = environmentConfig.ToAwsEnvironment(),
                    Description = $"TrialMatch base infrastructure for {environmentName} environment (Account: {environmentConfig.AccountType})",
                    Tags = context.GetCommonTags(),
                    StackName = baseStackName
                }, context);
                return (tmBaseStack, baseStackName);
                
            default:
                throw new ArgumentException($"Unknown application: {appName}. Supported applications: TrialFinderV2, TrialMatch");
        }
    }

    private static List<(Stack stack, string stackName)> CreateAllApplicationStacks(
        App app,
        DeploymentContext context,
        EnvironmentConfig environmentConfig,
        string appName,
        string environmentName)
    {
        var envPrefix = NamingConvention.GetEnvironmentPrefix(environmentName);
        var appCode = NamingConvention.GetApplicationCode(appName);
        var regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
        
        var stacks = new List<(Stack stack, string stackName)>();
        
        switch (appName.ToLower())
        {
            case "trialfinderv2":
                // Create all TrialFinderV2 stacks
                stacks.Add(CreateTrialFinderV2Stack(app, "ALB", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialFinderV2Stack(app, "ECS", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialFinderV2Stack(app, "DATA", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialFinderV2Stack(app, "COGNITO", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                break;
                
            case "trialmatch":
                // Create all TrialMatch stacks
                stacks.Add(CreateTrialMatchStack(app, "ALB", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialMatchStack(app, "ECS", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialMatchStack(app, "DATA", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                stacks.Add(CreateTrialMatchStack(app, "COGNITO", context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode));
                break;
                
            default:
                throw new ArgumentException($"Unknown application: {appName}. Supported applications: TrialFinderV2, TrialMatch");
        }
        
        return stacks;
    }

    private static void ValidateNamingConventions(DeploymentContext context)
    {
        try
        {
            Console.WriteLine("🔍 Validating naming conventions...");
            context.ValidateNamingContext();
            Console.WriteLine("✅ Naming conventions validated successfully");

            // Additional validation for resource name lengths and AWS limits
            ValidateAwsLimits(context);

            // Validate account-level uniqueness for multi-environment scenarios
            ValidateAccountLevelUniqueness(context);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Naming convention validation failed: {ex.Message}");
            ShowNamingHelp();
            throw;
        }
    }

    private static void ValidateMultiEnvironmentSetup(DeploymentContext context)
    {
        ValidateMultiEnvironmentSetupWithConfig(context, null);
    }

    private static void ValidateMultiEnvironmentSetupWithConfig(DeploymentContext context, IConfiguration? configuration)
    {
        try
        {
            Console.WriteLine("🏢 Validating multi-environment setup...");

            List<string> siblingEnvironments;
            
            if (configuration != null)
            {
                // Get actual sibling environments from configuration based on account ID
                siblingEnvironments = GetSiblingEnvironmentsFromConfig(configuration, context.Environment.AccountId);
            }
            else
            {
                // Fallback to naming convention (uses account type)
                siblingEnvironments = context.Environment.GetAccountSiblingEnvironments();
            }
            
            if (siblingEnvironments.Count > 1)
            {
                Console.WriteLine("📋 Detected multi-environment account setup:");
                Console.WriteLine($"   Account ID: {context.Environment.AccountId}");
                Console.WriteLine($"   Account Type: {context.Environment.AccountType}");
                Console.WriteLine(
                    $"   Environments in this account: {string.Join(", ", siblingEnvironments)}");
                Console.WriteLine($"   Current environment: {context.Environment.Name}");

                // Validate isolation strategy
                Console.WriteLine("   🔒 Isolation: VPC per environment");
            }
            else
            {
                Console.WriteLine("📋 Single environment account setup");
            }

            Console.WriteLine("✅ Multi-environment setup validated successfully");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Multi-environment validation failed: {ex.Message}");
            throw;
        }
    }

    private static void ValidateCidrRanges(DeploymentContext context)
    {
        // CIDR validation removed - no longer using IsolationStrategy
        Console.WriteLine("   🌐 VPC CIDR validation skipped - using default VPC configuration");
    }

    private static void ValidateAccountLevelUniqueness(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating account-level resource uniqueness...");

        // Test a few key resource types for uniqueness
        var testResources = new[]
        {
            ("ecs", "main"),
            ("rds", "main"),
            ("vpc", "main")
        };

        foreach ((string resourceType, string purpose) in testResources)
            try
            {
                NamingConvention.ValidateAccountLevelUniqueness(context, resourceType, purpose);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"❌ {ex.Message}");
                throw;
            }

        Console.WriteLine("✅ Account-level uniqueness validated");
    }

    private static void ValidateAwsLimits(DeploymentContext context)
    {
        var violations = new List<string>();

        // Check S3 bucket name length (3-63 characters)
        var s3Name = context.Namer.S3Bucket(StoragePurpose.App);
        if (s3Name.Length > 63)
            violations.Add($"S3 bucket name too long: {s3Name} ({s3Name.Length} chars, max 63)");

        // Check RDS identifier length (max 63 characters)
        var rdsName = context.Namer.RdsInstance(ResourcePurpose.Main);
        if (rdsName.Length > 63)
            violations.Add($"RDS identifier too long: {rdsName} ({rdsName.Length} chars, max 63)");

        // Check ECS cluster name length (max 255 characters)
        var ecsName = context.Namer.EcsCluster();
        if (ecsName.Length > 255)
            violations.Add(
                $"ECS cluster name too long: {ecsName} ({ecsName.Length} chars, max 255)");

        if (violations.Any())
        {
            Console.Error.WriteLine("❌ AWS resource name limit violations:");
            foreach (string violation in violations) Console.Error.WriteLine($"   {violation}");
            throw new InvalidOperationException("Resource names exceed AWS limits");
        }

        Console.WriteLine("✅ All resource names within AWS limits");
    }

    private static void DisplayResourceNames(DeploymentContext context)
    {
        Console.WriteLine("\n📝 Resource names that will be created:");
        Console.WriteLine($"   Stack: {GenerateStackName(context)}");
        Console.WriteLine($"   VPC: {context.Namer.Vpc()}");
        Console.WriteLine($"   ECS Cluster: {context.Namer.EcsCluster()}");
        
        // Show multi-service names for TrialMatch, single service for others
        if (context.Application.Name == "TrialMatch")
        {
            Console.WriteLine($"   API Service: {context.Namer.EcsService(ResourcePurpose.Web)}-api");
            Console.WriteLine($"   Frontend Service: {context.Namer.EcsService(ResourcePurpose.Web)}-frontend");
            Console.WriteLine($"   API Task: {context.Namer.EcsTaskDefinition(ResourcePurpose.Web)}-api");
            Console.WriteLine($"   Frontend Task: {context.Namer.EcsTaskDefinition(ResourcePurpose.Web)}-frontend");
        }
        else
        {
            Console.WriteLine($"   Web Service: {context.Namer.EcsService(ResourcePurpose.Web)}");
            Console.WriteLine($"   Web Task: {context.Namer.EcsTaskDefinition(ResourcePurpose.Web)}");
        }
        
        Console.WriteLine($"   Web ALB: {context.Namer.ApplicationLoadBalancer(ResourcePurpose.Web)}");
        Console.WriteLine($"   Database: {context.Namer.RdsInstance(ResourcePurpose.Main)}");
        Console.WriteLine($"   App Bucket: {context.Namer.S3Bucket(StoragePurpose.App)}");
        Console.WriteLine($"   Uploads Bucket: {context.Namer.S3Bucket(StoragePurpose.Uploads)}");
        Console.WriteLine($"   Backups Bucket: {context.Namer.S3Bucket(StoragePurpose.Backups)}");
        Console.WriteLine("\n📋 Security Groups:");
        Console.WriteLine($"   ALB Security Group: {context.Namer.SecurityGroupForAlb(ResourcePurpose.Web)}");
        Console.WriteLine($"   ECS Security Group: {context.Namer.SecurityGroupForEcs(ResourcePurpose.Web)}");
        Console.WriteLine($"   RDS Security Group: {context.Namer.SecurityGroupForRds(ResourcePurpose.Main)}");
        Console.WriteLine("\n🔐 IAM Roles:");
        
        // Show multi-service roles for TrialMatch
        if (context.Application.Name == "TrialMatch")
        {
            Console.WriteLine($"   API Task Role: {context.Namer.IamRole(IamPurpose.EcsTask)}-api");
            Console.WriteLine($"   Frontend Task Role: {context.Namer.IamRole(IamPurpose.EcsTask)}-frontend");
            Console.WriteLine($"   API Execution Role: {context.Namer.IamRole(IamPurpose.EcsExecution)}-api");
            Console.WriteLine($"   Frontend Execution Role: {context.Namer.IamRole(IamPurpose.EcsExecution)}-frontend");
        }
        else
        {
            Console.WriteLine($"   ECS Task Role: {context.Namer.IamRole(IamPurpose.EcsTask)}");
            Console.WriteLine($"   ECS Execution Role: {context.Namer.IamRole(IamPurpose.EcsExecution)}");
        }
        
        Console.WriteLine("\n📊 CloudWatch:");
        
        // Show multi-service log groups for TrialMatch
        if (context.Application.Name == "TrialMatch")
        {
            Console.WriteLine($"   API Log Group: {context.Namer.LogGroup("ecs", ResourcePurpose.Web)}-api");
            Console.WriteLine($"   Frontend Log Group: {context.Namer.LogGroup("ecs", ResourcePurpose.Web)}-frontend");
        }
        else
        {
            Console.WriteLine($"   Log Group: {context.Namer.LogGroup("ecs", ResourcePurpose.Web)}");
        }

        // Show application-specific resources if applicable
        if (context.Application.Name == "TrialFinderV2")
        {
            Console.WriteLine("\n🔍 TrialFinderV2-Specific Resources:");
            // Console.WriteLine($"   Documents Bucket: {context.Namer.S3Bucket("documents")}");
            // Console.WriteLine($"   Archive Bucket: {context.Namer.S3Bucket("archive")}");
            // Console.WriteLine($"   Processing Queue: {context.Namer.SqsQueue("processing")}");
            // Console.WriteLine($"   Urgent Queue: {context.Namer.SqsQueue("urgent")}");
            // Console.WriteLine(
            //     $"   Trial Updates Topic: {context.Namer.SnsTopics("trial-updates")}");
            // Console.WriteLine(
            //     $"   System Alerts Topic: {context.Namer.SnsTopics("system-alerts")}");
        }
        else if (context.Application.Name == "TrialMatch")
        {
            Console.WriteLine("\n🔍 TrialMatch-Specific Resources:");
            Console.WriteLine($"   Documents Bucket: {context.Namer.S3Bucket(StoragePurpose.Documents)}");
            Console.WriteLine($"   App Bucket: {context.Namer.S3Bucket(StoragePurpose.App)}");
            Console.WriteLine($"   Uploads Bucket: {context.Namer.S3Bucket(StoragePurpose.Uploads)}");
            Console.WriteLine($"   Backups Bucket: {context.Namer.S3Bucket(StoragePurpose.Backups)}");
        }

        Console.WriteLine();
    }

    private static void DisplayAccountContext(DeploymentContext context)
    {
        Console.WriteLine("\n🏢 Account Context:");
        Console.WriteLine($"   Environment: {context.Environment.Name}");
        Console.WriteLine($"   Account ID: {context.Environment.AccountId}");
        Console.WriteLine($"   Account Type: {context.Environment.AccountType}");
        Console.WriteLine($"   Region: {context.Environment.Region}");

        var siblings = context.Environment.GetAccountSiblingEnvironments();
        if (siblings.Count > 1)
            Console.WriteLine(
                $"   Other environments in this account: {string.Join(", ", siblings.Where(e => e != context.Environment.Name))}");

        Console.WriteLine("   VPC Strategy: Dedicated VPC per environment");
        Console.WriteLine();
    }

    private static void DisplayAvailableEnvironments(IConfiguration configuration)
    {
        Console.WriteLine("🌍 Available Environments:");
        Console.WriteLine();

        var environmentsSection = configuration.GetSection("Environments");
        var accountGroups
            = new Dictionary<string,
                List<(string env, string accountId, AccountType accountType)>>();

        foreach (var env in environmentsSection.GetChildren())
        {
            string? accountId = env["AccountId"] ?? "Unknown";
            string? accountTypeStr = env["AccountType"] ?? "NonProduction";
            if (Enum.TryParse<AccountType>(accountTypeStr, out var accountType))
            {
                var accountKey = $"{accountId} ({accountType})";

                if (!accountGroups.ContainsKey(accountKey))
                    accountGroups[accountKey]
                        = new List<(string env, string accountId, AccountType accountType)>();

                accountGroups[accountKey].Add((env.Key, accountId, accountType));
            }
        }

        foreach (var (accountKey, environments) in accountGroups)
        {
            Console.WriteLine($"📋 Account: {accountKey}");
            foreach (var (env, _, accountType) in environments)
            {
                var prefix = NamingConvention.GetEnvironmentPrefix(env);
                Console.WriteLine($"   • {env} (prefix: {prefix})");
            }

            Console.WriteLine();
        }

        Console.WriteLine("Example usage:");
        Console.WriteLine("  dotnet run -- --app=TrialFinderV2 --environment=Staging");
        Console.WriteLine("  dotnet run -- --app=TrialFinderV2 --environment=Production");
        Console.WriteLine("  dotnet run -- --app=TrialMatch --environment=Staging");
        Console.WriteLine("  dotnet run -- --app=TrialMatch --environment=Production");
        Console.WriteLine("  dotnet run -- --list-environments");
    }

    private static void ShowNamingHelp()
    {
        Console.Error.WriteLine("\n📋 Naming Convention Help:");
        Console.Error.WriteLine("Available environments:");
        Console.Error.WriteLine("  Non-Production Account: Development, Integration");
        Console.Error.WriteLine("  Production Account: Staging, Production, PreProduction, UAT");
        Console.Error.WriteLine("Available applications: TrialFinderV2, TrialMatch");
        Console.Error.WriteLine(
            "Available regions: us-east-1, us-east-2, us-west-1, us-west-2");
        Console.Error.WriteLine("\nTo add new applications or regions, update NamingConvention.cs");
        Console.Error.WriteLine(
            "To add new environments, update appsettings.json and NamingConvention.cs");
        Console.Error.WriteLine("\nExample usage:");
        Console.Error.WriteLine("  dotnet run -- --app=TrialFinderV2 --environment=Development");
        Console.Error.WriteLine(
            "  dotnet run -- --app=TrialFinderV2 --environment=Staging --validate-only");
        Console.Error.WriteLine(
            "  dotnet run -- --app=TrialFinderV2 --environment=Production --show-names-only");
        Console.Error.WriteLine("  dotnet run -- --app=TrialMatch --environment=Development");
        Console.Error.WriteLine(
            "  dotnet run -- --app=TrialMatch --environment=Staging --validate-only");
        Console.Error.WriteLine(
            "  dotnet run -- --app=TrialMatch --environment=Production --show-names-only");
        Console.Error.WriteLine("  dotnet run -- --list-environments");
    }

    private static List<string> GetSiblingEnvironmentsFromConfig(IConfiguration configuration, string accountId)
    {
        var environmentsSection = configuration.GetSection("Environments");
        var siblingEnvironments = new List<string>();

        foreach (var env in environmentsSection.GetChildren())
        {
            var envAccountId = env["AccountId"];
            if (envAccountId == accountId)
            {
                siblingEnvironments.Add(env.Key);
            }
        }

        return siblingEnvironments;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateStackName(DeploymentContext context)
    {
        // Stack names follow the same convention but without resource type
        var envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        var appCode = NamingConvention.GetApplicationCode(context.Application.Name);
        var regionCode = NamingConvention.GetRegionCode(context.Environment.Region);

        return $"{envPrefix}-{appCode}-stack-{regionCode}";
    }

    private static string GenerateBaseStackName(DeploymentContext context)
    {
        // Base stack names follow shared resource convention
        var envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        var regionCode = NamingConvention.GetRegionCode(context.Environment.Region);

        return $"{envPrefix}-shared-stack-{regionCode}";
    }

    private static IConfiguration BuildConfiguration(string[] args)
    {
        string environment = GetEnvironmentName(args);

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    private static string GetEnvironmentName(string[] args)
    {
        string? envArg = args.FirstOrDefault(a => a.StartsWith("--environment="));
        if (envArg != null)
            return envArg.Split('=')[1];

        return Environment.GetEnvironmentVariable("CDK_ENVIRONMENT") ?? "Development";
    }

    private static string GetApplicationName(string[] args)
    {
        string? appArg = args.FirstOrDefault(a => a.StartsWith("--app="));
        if (appArg != null)
            return appArg.Split('=')[1];

        return Environment.GetEnvironmentVariable("CDK_APPLICATION") ?? "TrialFinderV2";
    }

    private static EnvironmentConfig GetEnvironmentConfig(IConfiguration config,
        string environmentName)
    {
        var section = config.GetSection($"Environments:{environmentName}");
        if (!section.Exists())
            throw new ArgumentException($"Environment configuration not found: {environmentName}");

        var envConfig = new EnvironmentConfig
        {
            Name = environmentName,
            AccountId
                = section["AccountId"] ?? throw new ArgumentException("AccountId is required"),
            Region = section["Region"] ?? "us-east-1",
            Tags = section.GetSection("Tags").Get<Dictionary<string, string>>() ?? new()
        };

        // Parse account type
        if (Enum.TryParse<AccountType>(section["AccountType"], out var accountType))
            envConfig.AccountType = accountType;
        else
            // Default based on environment name
            envConfig.AccountType = NamingConvention.GetAccountType(environmentName);

        // IsolationStrategy parsing removed - no longer used

        // Validate the region is supported
        try
        {
            NamingConvention.GetRegionCode(envConfig.Region);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"Unsupported region '{envConfig.Region}' in environment '{environmentName}'. " +
                "Add the region to NamingConvention.RegionCodes or use a supported region.");
        }

        return envConfig;
    }

    private static ApplicationConfig GetApplicationConfig(IConfiguration config,
        string appName,
        string environmentName)
    {
        // Validate the application is supported by naming conventions
        try
        {
            NamingConvention.GetApplicationCode(appName);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Unsupported application '{appName}'. " +
                                        "Add the application to NamingConvention.ApplicationCodes or use a supported application.");
        }

        return appName.ToLower() switch
        {
            "trialfinderv2" => TrialFinderV2Config.GetConfig(environmentName),
            "trialmatch" => TrialMatchConfig.GetConfig(environmentName),
            _ => throw new ArgumentException($"Unknown application configuration: {appName}")
        };
    }
}