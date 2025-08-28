using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace AppInfraCdkV1.Deploy;

public static class ConfigurationService
{
    public static IConfiguration BuildConfiguration(string[] args)
    {
        // Get the directory where the executing assembly is located
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var basePath = Path.GetDirectoryName(assemblyLocation) ?? Directory.GetCurrentDirectory();
        
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
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

        throw new ArgumentException("Application name is required. Please specify using --app or set the CDK_APPLICATION environment variable.");
    }

    public static EnvironmentConfig GetEnvironmentConfig(IConfiguration configuration, string environmentName)
    {
        var environmentSection = configuration.GetSection($"Environments:{environmentName}");
        
        if (!environmentSection.Exists())
        {
            // List available environments for debugging
            var availableEnvironments = configuration.GetSection("Environments").GetChildren().Select(x => x.Key);
            var envList = string.Join(", ", availableEnvironments);
            throw new InvalidOperationException($"Environment '{environmentName}' not found in configuration. Available environments: {envList}");
        }

        var accountId = environmentSection.GetValue<string>("AccountId") ?? throw new InvalidOperationException($"AccountId not found for environment '{environmentName}'");
        var accountTypeString = environmentSection.GetValue<string>("AccountType");
        
        return new EnvironmentConfig
        {
            Name = environmentName,
            AccountId = accountId,
            Region = environmentSection.GetValue<string>("Region") ?? "us-east-2",
            AccountType = !string.IsNullOrEmpty(accountTypeString) 
                ? Enum.Parse<AccountType>(accountTypeString) 
                : DetermineAccountType(configuration, accountId)
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

    private static AccountType DetermineAccountType(IConfiguration configuration, string accountId)
    {
        // Search through all environments in configuration to find the account type
        var environmentsSection = configuration.GetSection("Environments");
        
        foreach (var environment in environmentsSection.GetChildren())
        {
            var envAccountId = environment.GetValue<string>("AccountId");
            if (envAccountId == accountId)
            {
                var accountTypeString = environment.GetValue<string>("AccountType");
                if (!string.IsNullOrEmpty(accountTypeString))
                {
                    return Enum.Parse<AccountType>(accountTypeString);
                }
            }
        }
        
        // Fallback to hardcoded values if not found in configuration
        return accountId switch
        {
            "615299752206" => AccountType.NonProduction,
            "442042533707" => AccountType.Production, 
            _ => AccountType.NonProduction
        };
    }
}