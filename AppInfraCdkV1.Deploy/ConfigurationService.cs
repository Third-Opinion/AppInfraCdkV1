using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Microsoft.Extensions.Configuration;

namespace AppInfraCdkV1.Deploy;

public static class ConfigurationService
{
    public static IConfiguration BuildConfiguration(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        return builder.Build();
    }

    public static string GetEnvironmentName(string[] args)
    {
        // Check command line first
        for (int i = 0; i < args.Length; i++)
        {
            // Handle --environment=value format
            if (args[i].StartsWith("--environment="))
            {
                return args[i].Substring("--environment=".Length);
            }
            // Handle --environment value format
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

    public static string GetApplicationName(string[] args)
    {
        // Check command line first
        for (int i = 0; i < args.Length; i++)
        {
            // Handle --app=value format
            if (args[i].StartsWith("--app="))
            {
                return args[i].Substring("--app=".Length);
            }
            // Handle --app value format
            if (args[i] == "--app" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        // Check environment variable
        var envVar = System.Environment.GetEnvironmentVariable("CDK_APPLICATION");
        if (!string.IsNullOrEmpty(envVar))
        {
            return envVar;
        }

        // Default to TrialFinderV2
        return "TrialFinderV2";
    }

    public static EnvironmentConfig GetEnvironmentConfig(IConfiguration configuration, string environmentName)
    {
        var environmentSection = configuration.GetSection($"environments:{environmentName}");
        
        if (!environmentSection.Exists())
        {
            throw new InvalidOperationException($"Environment '{environmentName}' not found in configuration.");
        }

        return new EnvironmentConfig
        {
            Name = environmentName,
            AccountId = environmentSection.GetValue<string>("accountId") ?? throw new InvalidOperationException($"AccountId not found for environment '{environmentName}'"),
            Region = environmentSection.GetValue<string>("region") ?? "us-east-2",
            AccountType = DetermineAccountType(environmentSection.GetValue<string>("accountId")!)
        };
    }

    public static ApplicationConfig GetApplicationConfig(IConfiguration configuration, string appName, string environmentName)
    {
        return new ApplicationConfig
        {
            Name = appName
        };
    }

    public static bool HasFlag(string[] args, string flag)
    {
        return args.Contains(flag, StringComparer.OrdinalIgnoreCase);
    }

    public static string GenerateStackName(DeploymentContext context)
    {
        var envPrefix = GetEnvironmentPrefix(context.Environment.Name);
        var appPrefix = GetApplicationPrefix(context.Application.Name);
        var regionCode = GetRegionCode(context.Environment.Region);
        
        return $"{envPrefix}-{appPrefix}-stack-{regionCode}";
    }

    private static string GetEnvironmentPrefix(string environmentName)
    {
        return NamingConvention.GetEnvironmentPrefix(environmentName);
    }

    private static string GetApplicationPrefix(string appName)
    {
        return NamingConvention.GetApplicationCode(appName);
    }

    private static string GetRegionCode(string region)
    {
        return NamingConvention.GetRegionCode(region);
    }

    private static AccountType DetermineAccountType(string accountId)
    {
        return accountId switch
        {
            "615299752206" => AccountType.NonProduction,
            "442042533707" => AccountType.Production, 
            _ => AccountType.NonProduction
        };
    }
}