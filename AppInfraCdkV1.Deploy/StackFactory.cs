using Amazon.CDK;
using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Apps.TrialMatch;
using AppInfraCdkV1.PublicThirdOpinion.Stacks;
// LakeFormation is now a separate internal app - deploy using AppInfraCdkV1.InternalApps/LakeFormation
// using AppInfraCdkV1.InternalApps.LakeFormation;
// using AppInfraCdkV1.InternalApps.LakeFormation.Stacks;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.Stacks.Base;

namespace AppInfraCdkV1.Deploy;

public static class StackFactory
{
    public static (Stack stack, string stackName) CreateSpecificStack(
        App app, 
        string stackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string appName, 
        string environmentName)
    {
        string envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        string regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
        string appCode = NamingConvention.GetApplicationCode(appName);
        
        return appName.ToUpper() switch
        {
            "TRIALFINDERV2" => CreateTrialFinderV2Stack(app, stackType, context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode),
            "TRIALMATCH" => CreateTrialMatchStack(app, stackType, context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode),
            "PUBLICTHIRDOPINION" => CreatePublicThirdOpinionStack(app, stackType, context, environmentConfig, appName, environmentName, envPrefix, appCode, regionCode),
            _ => throw new ArgumentException($"Unknown application: {appName}. Supported applications: TrialFinderV2, TrialMatch, PublicThirdOpinion")
        };
    }

    public static (Stack stack, string stackName) CreateAllStacksWithDependencies(
        App app, 
        string targetStackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string appName, 
        string environmentName)
    {
        string envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        string regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
        string baseStackName = $"{envPrefix}-shared-stack-{regionCode}";
        
        return appName.ToUpper() switch
        {
            "TRIALFINDERV2" => CreateAllTrialFinderV2StacksWithDependencies(app, targetStackType, context, environmentConfig, environmentName, envPrefix, regionCode, baseStackName),
            "TRIALMATCH" => CreateAllTrialMatchStacksWithDependencies(app, targetStackType, context, environmentConfig, environmentName, envPrefix, regionCode, baseStackName),
            "PUBLICTHIRDOPINION" => CreatePublicThirdOpinionStack(app, targetStackType, context, environmentConfig, "PublicThirdOpinion", environmentName, envPrefix, "pto", regionCode),
            _ => throw new ArgumentException($"Unknown application: {appName}")
        };
    }

    private static (Stack stack, string stackName) CreateAllTrialFinderV2StacksWithDependencies(
        App app, 
        string targetStackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string environmentName,
        string envPrefix,
        string regionCode,
        string baseStackName)
    {
        string appCode = "tfv2";
        
        // Create all stacks
        var albStackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
        var albStack = new TrialFinderV2AlbStack(app, albStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialFinderV2 ALB infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = albStackName
        }, context);

        var cognitoStackName = $"{envPrefix}-{appCode}-cognito-{regionCode}";
        var cognitoStack = new TrialFinderV2CognitoStack(app, cognitoStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialFinderV2 Cognito infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = cognitoStackName
        }, context);

        var dataStackName = $"{envPrefix}-{appCode}-data-{regionCode}";
        var dataStack = new TrialFinderV2DataStack(app, dataStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialFinderV2 Data infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = dataStackName
        }, context);

        var ecsStackName = $"{envPrefix}-{appCode}-ecs-{regionCode}";
        var ecsStack = new TrialFinderV2EcsStack(app, ecsStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialFinderV2 ECS infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = ecsStackName
        }, context);

        // Get base stack reference for dependencies
        var baseStackPlaceholder = app.Node.TryFindChild(baseStackName) as Stack ??
            new Stack(app, baseStackName); // Placeholder for dependency

        // Add explicit dependencies
        albStack.AddDependency(baseStackPlaceholder);
        cognitoStack.AddDependency(baseStackPlaceholder);
        dataStack.AddDependency(baseStackPlaceholder);
        ecsStack.AddDependency(baseStackPlaceholder);
        ecsStack.AddDependency(albStack);
        ecsStack.AddDependency(dataStack);

        // Return the requested stack type
        return targetStackType.ToUpper() switch
        {
            "ALB" => (albStack, albStackName),
            "COGNITO" => (cognitoStack, cognitoStackName),
            "DATA" => (dataStack, dataStackName),
            "ECS" => (ecsStack, ecsStackName),
            _ => throw new ArgumentException($"Unknown TrialFinderV2 stack type: {targetStackType}")
        };
    }

    private static (Stack stack, string stackName) CreateAllTrialMatchStacksWithDependencies(
        App app, 
        string targetStackType, 
        DeploymentContext context, 
        EnvironmentConfig environmentConfig, 
        string environmentName,
        string envPrefix,
        string regionCode,
        string baseStackName)
    {
        string appCode = "tm";
        
        // Create all stacks
        var albStackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
        var albStack = new TrialMatchAlbStack(app, albStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialMatch ALB infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = albStackName
        }, context);

        var cognitoStackName = $"{envPrefix}-{appCode}-cognito-{regionCode}";
        var cognitoStack = new TrialMatchCognitoStack(app, cognitoStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialMatch Cognito infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = cognitoStackName
        }, context);

        var dataStackName = $"{envPrefix}-{appCode}-data-{regionCode}";
        var dataStack = new TrialMatchDataStack(app, dataStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialMatch Data infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = dataStackName
        }, context);

        var ecsStackName = $"{envPrefix}-{appCode}-ecs-{regionCode}";
        var ecsStack = new TrialMatchEcsStack(app, ecsStackName, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"TrialMatch ECS infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = ecsStackName
        }, context);

        // Get base stack reference for dependencies
        var baseStackPlaceholder = app.Node.TryFindChild(baseStackName) as Stack ??
            new Stack(app, baseStackName); // Placeholder for dependency

        // Add explicit dependencies
        albStack.AddDependency(baseStackPlaceholder);
        cognitoStack.AddDependency(baseStackPlaceholder);
        dataStack.AddDependency(baseStackPlaceholder);
        ecsStack.AddDependency(baseStackPlaceholder);
        ecsStack.AddDependency(albStack);
        ecsStack.AddDependency(dataStack);

        // Return the requested stack type
        return targetStackType.ToUpper() switch
        {
            "ALB" => (albStack, albStackName),
            "COGNITO" => (cognitoStack, cognitoStackName),
            "DATA" => (dataStack, dataStackName),
            "ECS" => (ecsStack, ecsStackName),
            _ => throw new ArgumentException($"Unknown TrialMatch stack type: {targetStackType}")
        };
    }

    public static (Stack stack, string stackName) CreateTrialFinderV2Stack(
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
        // Get base stack name for dependency reference
        var baseStackName = $"{envPrefix}-shared-stack-{regionCode}";
        
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
                    
                    // Add dependency on base stack (VPC, security groups)
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
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
                    
                    // Add dependencies on base stack, ALB stack, and DATA stack
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    var albStackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
                    var albStack = app.Node.TryFindChild(albStackName) as Stack;
                    var dataStackName = $"{envPrefix}-{appCode}-data-{regionCode}";
                    var dataStack = app.Node.TryFindChild(dataStackName) as Stack;
                    
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    if (albStack != null)
                    {
                        stack.AddDependency(albStack);
                    }
                    if (dataStack != null)
                    {
                        stack.AddDependency(dataStack);
                    }
                    
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
                    
                    // Add dependency on base stack only (data stores need VPC but not ECS)
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
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
                    
                    // Add dependency on base stack
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
                    return (stack, stackName);
                }
            default:
                throw new ArgumentException($"Unknown TrialFinderV2 stack type: {stackType}. Supported types: ALB, ECS, DATA, COGNITO");
        }
    }

    public static (Stack stack, string stackName) CreateTrialMatchStack(
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
        // Get base stack name for dependency reference
        var baseStackName = $"{envPrefix}-shared-stack-{regionCode}";
        
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
                    
                    // Add dependency on base stack (VPC, security groups)
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
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
                    
                    // Add dependencies on base stack, ALB stack, and DATA stack
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    var albStackName = $"{envPrefix}-{appCode}-alb-{regionCode}";
                    var albStack = app.Node.TryFindChild(albStackName) as Stack;
                    var dataStackName = $"{envPrefix}-{appCode}-data-{regionCode}";
                    var dataStack = app.Node.TryFindChild(dataStackName) as Stack;
                    
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    if (albStack != null)
                    {
                        stack.AddDependency(albStack);
                    }
                    if (dataStack != null)
                    {
                        stack.AddDependency(dataStack);
                    }
                    
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
                    
                    // Add dependency on base stack only (data stores need VPC but not ECS)
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
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
                    
                    // Add dependency on base stack
                    var baseStack = app.Node.TryFindChild(baseStackName) as Stack;
                    if (baseStack != null)
                    {
                        stack.AddDependency(baseStack);
                    }
                    
                    return (stack, stackName);
                }
            default:
                throw new ArgumentException($"Unknown TrialMatch stack type: {stackType}. Supported types: ALB, ECS, DATA, COGNITO");
        }
    }

    // LakeFormation deployment has been moved to a separate internal app
    // Deploy using: cd AppInfraCdkV1.InternalApps/LakeFormation && dotnet run
    /*
    public static void DeployLakeFormationStacks(App app, DeploymentContext context, EnvironmentConfig environmentConfig, string environmentName)
    {
        try
        {
            Console.WriteLine("🏞️ Deploying LakeFormation stacks...");
            
            var config = LakeFormationEnvironmentConfigFactory.CreateConfig(environmentName, environmentConfig.AccountId);
            
            string envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
            string regionCode = NamingConvention.GetRegionCode(environmentConfig.Region);
            
            // Create storage stack first
            var storageStackName = $"{envPrefix}-lf-storage-{regionCode}";
            var storageStack = new DataLakeStorageStack(app, storageStackName, new StackProps
            {
                Env = environmentConfig.ToAwsEnvironment(),
                Description = $"Lake Formation data storage infrastructure for {environmentName}",
                Tags = context.GetCommonTags(),
                StackName = storageStackName
            }, config);
            
            // Create setup stack (depends on storage)
            var setupStackName = $"{envPrefix}-lf-setup-{regionCode}";
            var setupStack = new LakeFormationSetupStack(app, setupStackName, new StackProps
            {
                Env = environmentConfig.ToAwsEnvironment(),
                Description = $"Lake Formation setup and configuration for {environmentName}",
                Tags = context.GetCommonTags(),
                StackName = setupStackName
            }, config, storageStack);
            
            // Create permissions stack (depends on setup)
            var permissionsStackName = $"{envPrefix}-lf-permissions-{regionCode}";
            var permissionsStack = new LakeFormationPermissionsStack(app, permissionsStackName, new StackProps
            {
                Env = environmentConfig.ToAwsEnvironment(),
                Description = $"Lake Formation permissions and access control for {environmentName}",
                Tags = context.GetCommonTags(),
                StackName = permissionsStackName
            }, config, setupStack);
            
            // Create HealthLake test instance (depends on storage)
            var healthLakeStackName = $"{envPrefix}-lf-healthlake-test-{regionCode}";
            var healthLakeStack = new HealthLakeTestInstanceStack(app, healthLakeStackName, new StackProps
            {
                Env = environmentConfig.ToAwsEnvironment(),
                Description = $"HealthLake test instance for {environmentName}",
                Tags = context.GetCommonTags(),
                StackName = healthLakeStackName
            }, config, storageStack);

            Console.WriteLine("✅ LakeFormation stacks configured successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error configuring LakeFormation stacks: {ex.Message}");
            throw;
        }
    }
    */

    public static (Stack stack, string stackName) CreatePublicThirdOpinionStack(
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
        // PublicThirdOpinion is a single stack, no sub-stacks
        if (stackType.ToUpper() != "PUBLIC" && stackType.ToUpper() != "MAIN")
        {
            throw new ArgumentException($"PublicThirdOpinion only supports 'PUBLIC' or 'MAIN' stack type, got: {stackType}");
        }
        
        var stackName = $"{envPrefix}-{appCode}-public-{regionCode}";
        var stack = new PublicThirdOpinionStack(app, stackName, context, new StackProps
        {
            Env = environmentConfig.ToAwsEnvironment(),
            Description = $"PublicThirdOpinion public website infrastructure for {environmentName} environment",
            Tags = context.GetCommonTags(),
            StackName = stackName
        });
        
        return (stack, stackName);
    }

    private static string GetApplicationCode(string appName)
    {
        return NamingConvention.GetApplicationCode(appName);
    }
}