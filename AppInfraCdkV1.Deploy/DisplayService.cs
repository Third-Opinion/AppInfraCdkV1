using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Microsoft.Extensions.Configuration;

namespace AppInfraCdkV1.Deploy;

public static class DisplayService
{
    public static void DisplayResourceNames(DeploymentContext context)
    {
        Console.WriteLine();
        Console.WriteLine("📝 Resource names that will be created:");
        
        var envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        var regionCode = NamingConvention.GetRegionCode(context.Environment.Region);
        
        var appName = context.Application.Name.ToLower();
        var appCode = GetApplicationCode(appName);
        
        // Stack name
        Console.WriteLine($"   Stack: {envPrefix}-{appCode}-stack-{regionCode}");
        
        // VPC and networking
        Console.WriteLine($"   VPC: {envPrefix}-{appCode}-vpc-{regionCode}-main");
        
        // ECS resources
        Console.WriteLine($"   ECS Cluster: {envPrefix}-{appCode}-ecs-{regionCode}-main");
        Console.WriteLine($"   Web Service: {envPrefix}-{appCode}-svc-{regionCode}-web");
        Console.WriteLine($"   Web Task: {envPrefix}-{appCode}-task-{regionCode}-web");
        Console.WriteLine($"   Web ALB: {envPrefix}-{appCode}-alb-{regionCode}-web");
        
        // Database
        Console.WriteLine($"   Database: {envPrefix}-{appCode}-rds-{regionCode}-main");
        
        // S3 Buckets
        Console.WriteLine($"   App Bucket: thirdopinion.io-{envPrefix}-{appCode}-app-{regionCode}");
        Console.WriteLine($"   Uploads Bucket: thirdopinion.io-{envPrefix}-{appCode}-uploads-{regionCode}");
        Console.WriteLine($"   Backups Bucket: thirdopinion.io-{envPrefix}-{appCode}-backups-{regionCode}");
        
        Console.WriteLine();
        Console.WriteLine("📋 Security Groups:");
        Console.WriteLine($"   ALB Security Group: {envPrefix}-{appCode}-sg-alb-web-{regionCode}");
        Console.WriteLine($"   ECS Security Group: {envPrefix}-{appCode}-sg-ecs-web-{regionCode}");
        Console.WriteLine($"   RDS Security Group: {envPrefix}-{appCode}-sg-rds-main-{regionCode}");
        
        Console.WriteLine();
        Console.WriteLine("🔐 IAM Roles:");
        Console.WriteLine($"   ECS Task Role: {envPrefix}-{appCode}-role-{regionCode}-ecs-task");
        Console.WriteLine($"   ECS Execution Role: {envPrefix}-{appCode}-role-{regionCode}-ecs-exec");
        
        Console.WriteLine();
        Console.WriteLine("📊 CloudWatch:");
        Console.WriteLine($"   Log Group: /aws/ecs/{envPrefix}-{appCode}-web");
        
        Console.WriteLine();
        Console.WriteLine($"🔍 {context.Application.Name}-Specific Resources:");
        DisplayApplicationSpecificResources(context);
    }

    public static void DisplayAccountContext(DeploymentContext context)
    {
        Console.WriteLine();
        Console.WriteLine("🏢 Account Context:");
        Console.WriteLine($"   Environment: {context.Environment.Name}");
        Console.WriteLine($"   Account ID: {context.Environment.AccountId}");
        Console.WriteLine($"   Account Type: {context.Environment.AccountType}");
        Console.WriteLine($"   Region: {context.Environment.Region}");
        
        var siblingEnvironments = GetSiblingEnvironments(context.Environment.AccountId);
        if (siblingEnvironments.Any())
        {
            Console.WriteLine($"   Other environments in this account: {string.Join(", ", siblingEnvironments)}");
        }
        
        Console.WriteLine($"   VPC Strategy: Dedicated VPC per environment");
    }

    public static void DisplayAvailableEnvironments(IConfiguration configuration)
    {
        Console.WriteLine("📋 Available Environments:");
        Console.WriteLine();
        
        var environmentsSection = configuration.GetSection("environments");
        
        foreach (var env in environmentsSection.GetChildren())
        {
            var accountId = env.GetValue<string>("accountId");
            var region = env.GetValue<string>("region");
            var accountType = DetermineAccountType(accountId);
            
            Console.WriteLine($"   🌍 {env.Key}");
            Console.WriteLine($"      Account ID: {accountId}");
            Console.WriteLine($"      Account Type: {accountType}");
            Console.WriteLine($"      Region: {region}");
            Console.WriteLine();
        }
        
        Console.WriteLine("Usage: dotnet run -- --environment <name> --app <application>");
        Console.WriteLine("Available applications: TrialFinderV2, TrialMatch, LakeFormation");
    }

    public static void ShowNamingHelp()
    {
        Console.WriteLine("📚 CDK Naming Convention Help");
        Console.WriteLine("============================");
        Console.WriteLine();
        Console.WriteLine("🏗️ Stack Naming Pattern:");
        Console.WriteLine("   {env-prefix}-{app-code}-{component}-{region-code}");
        Console.WriteLine("   Example: dev-tfv2-ecs-ue2");
        Console.WriteLine();
        Console.WriteLine("🔤 Environment Prefixes:");
        Console.WriteLine("   Development → dev");
        Console.WriteLine("   Integration → int");
        Console.WriteLine("   Staging → stg");
        Console.WriteLine("   Production → prod");
        Console.WriteLine();
        Console.WriteLine("📱 Application Codes:");
        Console.WriteLine("   TrialFinderV2 → tfv2");
        Console.WriteLine("   TrialMatch → tm");
        Console.WriteLine("   LakeFormation → lf");
        Console.WriteLine();
        Console.WriteLine("🌍 Region Codes:");
        Console.WriteLine("   us-east-1 → ue1");
        Console.WriteLine("   us-east-2 → ue2");
        Console.WriteLine("   us-west-1 → uw1");
        Console.WriteLine("   us-west-2 → uw2");
        Console.WriteLine();
        Console.WriteLine("📦 Component Types:");
        Console.WriteLine("   alb → Application Load Balancer");
        Console.WriteLine("   ecs → Elastic Container Service");
        Console.WriteLine("   rds → Relational Database Service");
        Console.WriteLine("   cognito → AWS Cognito");
        Console.WriteLine("   data → Data/Storage resources");
        Console.WriteLine();
        Console.WriteLine("📏 AWS Limits Enforced:");
        Console.WriteLine("   - Stack names: 128 characters max");
        Console.WriteLine("   - Resource names: 64 characters max");
        Console.WriteLine("   - S3 bucket names: 63 characters max, globally unique");
        Console.WriteLine("   - IAM role names: 64 characters max");
    }

    private static void DisplayApplicationSpecificResources(DeploymentContext context)
    {
        var appName = context.Application.Name.ToLower();
        
        switch (appName)
        {
            case "trialfinderv2":
                Console.WriteLine("   - Trial search and matching engine");
                Console.WriteLine("   - Patient eligibility assessment");
                Console.WriteLine("   - Healthcare provider integration");
                break;
                
            case "trialmatch":
                Console.WriteLine("   - Advanced trial matching algorithms");
                Console.WriteLine("   - Real-time matching service");
                Console.WriteLine("   - Analytics and reporting");
                break;
                
            case "lakeformation":
                Console.WriteLine("   - Data lake storage and organization");
                Console.WriteLine("   - HealthLake integration");
                Console.WriteLine("   - Data governance and permissions");
                break;
                
            default:
                Console.WriteLine("   - Application-specific resources");
                break;
        }
    }

    private static string GetApplicationCode(string appName)
    {
        return NamingConvention.GetApplicationCode(appName);
    }

    private static List<string> GetSiblingEnvironments(string accountId)
    {
        return accountId switch
        {
            "615299752206" => new List<string> { "Integration" },
            "442042533707" => new List<string>(),
            _ => new List<string>()
        };
    }

    private static string DetermineAccountType(string? accountId)
    {
        return accountId switch
        {
            "615299752206" => "NonProduction",
            "442042533707" => "Production",
            _ => "Unknown"
        };
    }
}