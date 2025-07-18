using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using System.Text.Json;
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
            "integration" => CreateConfig(environment, ResourceSizing.GetDevelopmentSizing(),
                false),

            // Production Account Environments
            "staging" => CreateConfig(environment, ResourceSizing.GetProductionSizing(), true),
            "production" => CreateConfig(environment, GetProductionSizing(), true),
            _ => throw new ArgumentException($"Unknown environment: {environment}")
        };
    }

    public static ContainerConfiguration GetContainerConfiguration(string environment)
    {
        var configPath = Path.Combine("AppInfraCdkV1.Apps", "TrialFinderV2", "config", $"{environment.ToLower()}.json");
        
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        var jsonContent = File.ReadAllText(configPath);
        var configData = JsonSerializer.Deserialize<ConfigurationRoot>(jsonContent);
        
        return configData?.EcsConfiguration?.TaskDefinition ?? new ContainerConfiguration();
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
            SupportsMultiEnvironmentDeployment = true
        };

        // Configure environment-specific overrides
        var environmentOverride = new EnvironmentOverride
        {
            EnableEnhancedMonitoring = accountType == AccountType.Production,
            BackupRetentionDays = GetBackupRetentionForEnvironment(environment)
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


    private static string GetVersion()
    {
        return Environment.GetEnvironmentVariable("GITHUB_SHA")?[..8] ?? "local";
    }
}

// Configuration classes for container definitions
public class ConfigurationRoot
{
    public EcsConfiguration? EcsConfiguration { get; set; }
}

public class EcsConfiguration
{
    public ContainerConfiguration? TaskDefinition { get; set; }
}

public class ContainerConfiguration
{
    public List<ContainerDefinition> ContainerDefinitions { get; set; } = new();
}

public class ContainerDefinition
{
    public string Name { get; set; } = "";
    public string? Image { get; set; }
    public int Cpu { get; set; }
    public List<PortMapping> PortMappings { get; set; } = new();
    public bool Essential { get; set; } = true;
    public List<EnvironmentVariable> Environment { get; set; } = new();
    public HealthCheck? HealthCheck { get; set; }
}

public class PortMapping
{
    public string Name { get; set; } = "";
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
    public string Protocol { get; set; } = "tcp";
    public string AppProtocol { get; set; } = "http";
}

public class EnvironmentVariable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class HealthCheck
{
    public List<string> Command { get; set; } = new();
    public int Interval { get; set; } = 30;
    public int Timeout { get; set; } = 5;
    public int Retries { get; set; } = 3;
    public int StartPeriod { get; set; } = 60;
}