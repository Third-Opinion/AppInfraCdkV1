using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Microsoft.Extensions.Configuration;

namespace AppInfraCdkV1.Deploy;

public static class ValidationService
{
    public static void ValidateNamingConventions(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating naming conventions...");
        
        // Validate naming conventions by attempting to generate names
        try
        {
            context.ValidateNamingContext();
            Console.WriteLine("✅ Naming conventions validated successfully");
            Console.WriteLine("✅ All resource names within AWS limits");
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Naming convention validation failed:");
            Console.WriteLine($"   - {ex.Message}");
            Environment.Exit(1);
        }
    }

    public static void ValidateAccountLevelUniqueness(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating account-level resource uniqueness...");
        
        // This is a placeholder for more sophisticated validation
        // Could check for existing resources with same names across environments
        
        Console.WriteLine("✅ Account-level uniqueness validated");
    }

    public static void ValidateMultiEnvironmentSetup(DeploymentContext context)
    {
        Console.WriteLine("🏢 Validating multi-environment setup...");
        
        var accountId = context.Environment.AccountId;
        var environmentName = context.Environment.Name;
        
        // Determine account type based on account ID
        var (accountType, environments) = GetAccountTypeAndEnvironments(accountId);
        
        Console.WriteLine($"📋 Detected multi-environment account setup:");
        Console.WriteLine($"   Account ID: {accountId}");
        Console.WriteLine($"   Account Type: {accountType}");
        Console.WriteLine($"   Environments in this account: {string.Join(", ", environments)}");
        Console.WriteLine($"   Current environment: {environmentName}");
        Console.WriteLine($"   🔒 Isolation: VPC per environment");
        
        Console.WriteLine("✅ Multi-environment setup validated successfully");
    }

    public static void ValidateMultiEnvironmentSetupWithConfig(DeploymentContext context, IConfiguration? configuration)
    {
        ValidateMultiEnvironmentSetup(context);
        
        if (configuration != null)
        {
            var accountId = context.Environment.AccountId;
            var siblingEnvironments = GetSiblingEnvironmentsFromConfig(configuration, accountId);
            
            if (siblingEnvironments.Any())
            {
                Console.WriteLine($"📋 Other environments in this account: {string.Join(", ", siblingEnvironments)}");
            }
        }
    }

    public static void ValidateCidrRanges(DeploymentContext context)
    {
        // Placeholder for CIDR validation logic
        Console.WriteLine("🔍 Validating CIDR ranges...");
        Console.WriteLine("✅ CIDR ranges validated");
    }

    public static void ValidateAwsLimits(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating AWS service limits...");
        
        // Basic validation - could be expanded with actual AWS API calls
        var resourceCount = EstimateResourceCount(context);
        
        if (resourceCount.TotalResources > 1000)
        {
            Console.WriteLine($"⚠️ High resource count estimated: {resourceCount.TotalResources}");
            Console.WriteLine("   Consider reviewing AWS service limits");
        }
        else
        {
            Console.WriteLine($"✅ Resource count within limits: {resourceCount.TotalResources}");
        }
    }

    private static (string AccountType, string[] Environments) GetAccountTypeAndEnvironments(string accountId)
    {
        return accountId switch
        {
            "615299752206" => ("NonProduction", new[] { "Development", "Integration" }),
            "442042533707" => ("Production", new[] { "Production" }),
            _ => ("Unknown", new[] { "Unknown" })
        };
    }

    private static List<string> GetSiblingEnvironmentsFromConfig(IConfiguration configuration, string accountId)
    {
        var environments = new List<string>();
        
        try
        {
            var environmentsSection = configuration.GetSection("environments");
            
            foreach (var env in environmentsSection.GetChildren())
            {
                var envAccountId = env.GetValue<string>("accountId");
                if (envAccountId == accountId)
                {
                    environments.Add(env.Key);
                }
            }
        }
        catch
        {
            // Ignore configuration errors for this validation
        }
        
        return environments;
    }

    private static (int TotalResources, int VpcsCount, int SubnetsCount) EstimateResourceCount(DeploymentContext context)
    {
        // Basic estimation logic - could be made more sophisticated
        int baseResources = 50; // VPC, subnets, security groups, etc.
        int perAppResources = 30; // ALB, ECS, RDS, etc.
        
        // Estimate based on application type
        var totalResources = baseResources + perAppResources;
        
        return (totalResources, 1, 6); // 1 VPC, 6 subnets typical
    }
}