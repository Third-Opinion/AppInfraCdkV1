using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Environment = System.Environment;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public static class TrialFinderV2Config
{
    public static ApplicationConfig GetConfig(string environment)
    {
        return environment.ToLower() switch
        {
            // Non-Production Account Environments
            "development" => CreateConfig(environment, ResourceSizing.GetDevelopmentSizing(),
                false),

            // Production Account Environments
            "staging" => CreateConfig(environment, ResourceSizing.GetProductionSizing(), true),
            "production" => CreateConfig(environment, GetProductionSizing(), true),
            _ => throw new ArgumentException($"Unknown environment: {environment}")
        };
    }

    private static ApplicationConfig CreateConfig(string environment,
        ResourceSizing sizing,
        bool isProductionClass)
    {
        var accountType = NamingConvention.GetAccountType(environment);

        return new ApplicationConfig
        {
            Name = "TrialFinderV2",
            Version = GetVersion(),
            Sizing = sizing,
            Security = SecurityConfig.GetSecurityConfigForAccountType(accountType),
            Settings = GetEnvironmentSettings(environment, isProductionClass),
            MultiEnvironment = GetMultiEnvironmentConfig(environment, accountType)
        };
    }

    private static ResourceSizing GetProductionSizing()
    {
        return new ResourceSizing
        {
            // InstanceType = "t3.large",
            // MinCapacity = 3,
            // MaxCapacity = 15,
            // DatabaseInstanceClass = "db.t3.medium"
        };
    }

    private static Dictionary<string, object> GetEnvironmentSettings(string environment,
        bool isProductionClass)
    {
        var baseSettings = new Dictionary<string, object>
        {
            ["EnableDetailedLogging"] = !isProductionClass,
            ["ExternalApiTimeout"] = isProductionClass ? 10 : 30,
            ["TrialDataRefreshInterval"]
                = isProductionClass
                    ? "0 0 * * *"
                    : "0 */6 * * *", // Production: daily, Dev: every 6 hours
            ["MaxConcurrentTrialProcessing"] = isProductionClass ? 50 : 10
        };

        // Environment-specific settings
        Dictionary<string, object> environmentSpecific = environment.ToLower() switch
        {
            "development" => new Dictionary<string, object>
            {
                ["EnableMockExternalServices"] = true,
                ["UseLocalDatabase"] = false,
                ["EnableDebugMode"] = true,
                ["CacheEnabled"] = false
            },
            "staging" => new Dictionary<string, object>
            {
                ["EnableMockExternalServices"] = false,
                ["UseProductionConfiguration"] = true,
                ["EnableStagingFeatures"] = true,
                ["CacheEnabled"] = true,
                ["EnableBlueGreenDeployment"] = true
            },
            "production" => new Dictionary<string, object>
            {
                ["EnableMockExternalServices"] = false,
                ["UseProductionConfiguration"] = true,
                ["EnableHighAvailability"] = true,
                ["CacheEnabled"] = true,
                ["EnableDisasterRecovery"] = true,
                ["EnableAdvancedMonitoring"] = true
            },
            _ => new Dictionary<string, object>()
        };

        // Merge base settings with environment-specific settings
        foreach (KeyValuePair<string, object> kvp in environmentSpecific)
            baseSettings[kvp.Key] = kvp.Value;

        return baseSettings;
    }

    private static MultiEnvironmentConfig GetMultiEnvironmentConfig(string environment,
        AccountType accountType)
    {
        var config = new MultiEnvironmentConfig
        {
            SupportsMultiEnvironmentDeployment = true,
            SharedResources = new SharedResourcesConfig
            {
                VpcSharing = new VpcSharingConfig
                {
                    IsShared = false, // Each environment gets its own VPC for better isolation
                    SharingEnvironments = new List<string>()
                },
                DatabaseSharing = new DatabaseSharingConfig
                {
                    ShareInstances = false, // Each environment gets its own database
                    UseSeparateSchemas = true,
                    SharingEnvironments = new List<string>()
                }
            }
        };

        // Configure environment-specific overrides
        var environmentOverride = new EnvironmentOverride
        {
            EnableEnhancedMonitoring = accountType == AccountType.Production,
            BackupRetentionDays = GetBackupRetentionForEnvironment(environment),
            FeatureFlags = GetFeatureFlagsForEnvironment(environment)
        };

        config.EnvironmentOverrides[environment] = environmentOverride;

        return config;
    }

    private static int GetBackupRetentionForEnvironment(string environment)
    {
        return environment.ToLower() switch
        {
            "development" => 1,
            "staging" => 7,
            "production" => 30,
            _ => 7
        };
    }

    private static Dictionary<string, bool> GetFeatureFlagsForEnvironment(string environment)
    {
        return environment.ToLower() switch
        {
            "development" => new Dictionary<string, bool>
            {
                ["EnableBetaFeatures"] = true,
                ["EnableExperimentalUI"] = true,
                ["EnableDebugPanel"] = true,
                ["EnableTrialSimulation"] = true
            },
            "staging" => new Dictionary<string, bool>
            {
                ["EnableBetaFeatures"] = false,
                ["EnableExperimentalUI"] = false,
                ["EnableDebugPanel"] = false,
                ["EnableStagingFeatures"] = true
            },
            "production" => new Dictionary<string, bool>
            {
                ["EnableBetaFeatures"] = false,
                ["EnableExperimentalUI"] = false,
                ["EnableDebugPanel"] = false,
                ["EnableProductionOnlyFeatures"] = true
            },
            _ => new Dictionary<string, bool>()
        };
    }

    private static string GetVersion()
    {
        return Environment.GetEnvironmentVariable("GITHUB_SHA")?[..8] ?? "local";
    }
}