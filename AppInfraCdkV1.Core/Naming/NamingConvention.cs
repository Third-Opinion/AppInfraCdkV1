using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.Naming;

public static class NamingConvention
{
    // Company domain for S3 buckets
    private const string CompanyDomain = "thirdopinion.io";

    // Environment prefixes - Updated to support multiple environments per account
    private static readonly Dictionary<string, string> EnvironmentPrefixes = new()
    {
        // Non-Production Account Environments
        ["Development"] = "dev",
        ["QA"] = "qa",
        ["Test"] = "test",
        ["Integration"] = "int",

        // Production Account Environments  
        ["Staging"] = "stg",
        ["Production"] = "prod",
        ["PreProduction"] = "preprod",
        ["UAT"] = "uat"
    };

    // Account types for logical grouping
    public static readonly Dictionary<string, AccountType> EnvironmentAccountTypes = new()
    {
        ["Development"] = AccountType.NonProduction,
        ["QA"] = AccountType.NonProduction,
        ["Test"] = AccountType.NonProduction,
        ["Integration"] = AccountType.NonProduction,
        ["Staging"] = AccountType.Production,
        ["Production"] = AccountType.Production,
        ["PreProduction"] = AccountType.Production,
        ["UAT"] = AccountType.Production
    };

    // Application codes
    private static readonly Dictionary<string, string> ApplicationCodes = new()
    {
        ["TrialFinderV2"] = "tfv2",
        ["PatientPortal"] = "pp",
        ["AdminDashboard"] = "ad"
    };

    // Region codes
    private static readonly Dictionary<string, string> RegionCodes = new()
    {
        ["us-east-1"] = "ue1",
        ["us-east-2"] = "ue2",
        ["us-west-1"] = "uw1",
        ["us-west-2"] = "uw2",
        ["eu-west-1"] = "ew1",
        ["eu-central-1"] = "ec1",
        ["ap-southeast-1"] = "as1"
    };

    public static string GetEnvironmentPrefix(string environmentName)
    {
        if (EnvironmentPrefixes.TryGetValue(environmentName, out string? prefix))
            return prefix;
        throw new ArgumentException(
            $"Unknown environment: {environmentName}. Supported environments: {string.Join(", ", EnvironmentPrefixes.Keys)}");
    }

    public static AccountType GetAccountType(string environmentName)
    {
        if (EnvironmentAccountTypes.TryGetValue(environmentName, out AccountType accountType))
            return accountType;
        throw new ArgumentException(
            $"Unknown environment: {environmentName}. Supported environments: {string.Join(", ", EnvironmentAccountTypes.Keys)}");
    }

    public static string GetApplicationCode(string applicationName)
    {
        if (ApplicationCodes.TryGetValue(applicationName, out string? code))
            return code;
        throw new ArgumentException(
            $"Unknown application: {applicationName}. Supported applications: {string.Join(", ", ApplicationCodes.Keys)}");
    }

    public static string GetRegionCode(string regionName)
    {
        if (RegionCodes.TryGetValue(regionName, out string? code))
            return code;
        throw new ArgumentException(
            $"Unknown region: {regionName}. Supported regions: {string.Join(", ", RegionCodes.Keys)}");
    }

    /// <summary>
    ///     Registers a new environment with its account type
    /// </summary>
    public static void RegisterEnvironment(string environmentName,
        string prefix,
        AccountType accountType)
    {
        if (EnvironmentPrefixes.ContainsKey(environmentName))
            throw new InvalidOperationException(
                $"Environment {environmentName} is already registered with prefix {EnvironmentPrefixes[environmentName]}");

        if (EnvironmentPrefixes.ContainsValue(prefix))
            throw new InvalidOperationException($"Environment prefix {prefix} is already in use");

        EnvironmentPrefixes[environmentName] = prefix;
        EnvironmentAccountTypes[environmentName] = accountType;
    }

    /// <summary>
    ///     Registers a new application code for naming conventions
    /// </summary>
    public static void RegisterApplication(string applicationName, string code)
    {
        if (ApplicationCodes.ContainsKey(applicationName))
            throw new InvalidOperationException(
                $"Application {applicationName} is already registered with code {ApplicationCodes[applicationName]}");

        if (ApplicationCodes.ContainsValue(code))
            throw new InvalidOperationException($"Application code {code} is already in use");

        ApplicationCodes[applicationName] = code;
    }

    /// <summary>
    ///     Validates that a resource name follows the naming convention
    /// </summary>
    public static bool ValidateResourceName(string resourceName,
        DeploymentContext context,
        string resourceType,
        string specificName)
    {
        string expectedName = GenerateResourceName(context, resourceType, specificName);
        return resourceName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Generates a standard resource name following the naming convention
    ///     Pattern: {env-prefix}-{app-code}-{resource-type}-{region-code}-{specific-name}
    /// </summary>
    public static string GenerateResourceName(DeploymentContext context,
        string resourceType,
        string specificName)
    {
        string envPrefix = GetEnvironmentPrefix(context.Environment.Name);
        string appCode = GetApplicationCode(context.Application.Name);
        string regionCode = GetRegionCode(context.Environment.Region);

        IEnumerable<string> parts
            = new[] { envPrefix, appCode, resourceType, regionCode, specificName }
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p.ToLowerInvariant());

        return string.Join("-", parts);
    }

    /// <summary>
    ///     Generates a security group name with protected resource context
    ///     Pattern: {env-prefix}-{app-code}-sg-{protected-resource-type}-{specific-purpose}-{region-code}
    /// </summary>
    public static string GenerateSecurityGroupName(DeploymentContext context,
        string protectedResourceType,
        string specificPurpose)
    {
        string envPrefix = GetEnvironmentPrefix(context.Environment.Name);
        string appCode = GetApplicationCode(context.Application.Name);
        string regionCode = GetRegionCode(context.Environment.Region);

        IEnumerable<string> parts = new[]
            {
                envPrefix, appCode, ResourceTypes.SecurityGroup, protectedResourceType,
                specificPurpose, regionCode
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p.ToLowerInvariant());

        return string.Join("-", parts);
    }

    /// <summary>
    ///     Generates an S3 bucket name with company domain prefix
    ///     Pattern: thirdopinion.io-{env-prefix}-{app-code}-{purpose}-{region-code}
    /// </summary>
    public static string GenerateS3BucketName(DeploymentContext context, string purpose)
    {
        string envPrefix = GetEnvironmentPrefix(context.Environment.Name);
        string appCode = GetApplicationCode(context.Application.Name);
        string regionCode = GetRegionCode(context.Environment.Region);

        IEnumerable<string> parts = new[] { CompanyDomain, envPrefix, appCode, purpose, regionCode }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p.ToLowerInvariant());

        return string.Join("-", parts);
    }

    /// <summary>
    ///     Generates a CloudWatch log group name
    ///     Pattern: /aws/{service-type}/{env-prefix}-{app-code}-{specific-name}
    /// </summary>
    public static string GenerateLogGroupName(DeploymentContext context,
        string serviceType,
        string specificName)
    {
        string envPrefix = GetEnvironmentPrefix(context.Environment.Name);
        string appCode = GetApplicationCode(context.Application.Name);

        return
            $"/aws/{serviceType.ToLowerInvariant()}/{envPrefix}-{appCode}-{specificName.ToLowerInvariant()}";
    }

    /// <summary>
    ///     Generates a VPC name with environment isolation for multi-environment accounts
    ///     Pattern: {env-prefix}-{app-code}-vpc-{region-code}-{purpose}
    ///     Note: When multiple environments share an account, each gets its own VPC for isolation
    /// </summary>
    public static string GenerateVpcName(DeploymentContext context, string purpose = "main")
    {
        return GenerateResourceName(context, ResourceTypes.Vpc, purpose);
    }

    /// <summary>
    ///     Gets environments that share the same account as the given environment
    /// </summary>
    public static List<string> GetEnvironmentsInSameAccount(string environmentName)
    {
        AccountType accountType = GetAccountType(environmentName);
        return EnvironmentAccountTypes
            .Where(kvp => kvp.Value == accountType)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    ///     Validates that resource names will be unique within the account
    /// </summary>
    public static void ValidateAccountLevelUniqueness(DeploymentContext context,
        string resourceType,
        string specificName)
    {
        string currentResourceName = GenerateResourceName(context, resourceType, specificName);
        List<string> environmentsInAccount = GetEnvironmentsInSameAccount(context.Environment.Name);

        foreach (string env in environmentsInAccount.Where(e => e != context.Environment.Name))
        {
            var otherContext = new DeploymentContext
            {
                Environment = new EnvironmentConfig
                {
                    Name = env,
                    Region = context.Environment.Region,
                    AccountId = context.Environment.AccountId
                },
                Application = context.Application
            };

            string otherResourceName
                = GenerateResourceName(otherContext, resourceType, specificName);

            if (currentResourceName.Equals(otherResourceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Resource name collision detected: '{currentResourceName}' would be the same for environments '{context.Environment.Name}' and '{env}' in the same account");
        }
    }

    // Resource type codes
    public static class ResourceTypes
    {
        public const string EcsCluster = "ecs";
        public const string EcsTask = "task";
        public const string EcsService = "svc";
        public const string ApplicationLoadBalancer = "alb";
        public const string NetworkLoadBalancer = "nlb";
        public const string Vpc = "vpc";
        public const string SecurityGroup = "sg";
        public const string IamRole = "role";
        public const string IamUser = "user";
        public const string IamPolicy = "policy";
        public const string RdsInstance = "rds";
        public const string ElastiCache = "cache";
        public const string Lambda = "lambda";
        public const string ElasticFileSystem = "efs";
        public const string OpenSearch = "search";
        public const string BastionHost = "bastion";
        public const string S3Bucket = "bucket";
        public const string CloudWatchLogGroup = "logs";
        public const string SnsTopics = "sns";
        public const string SqsQueue = "sqs";
        public const string SecretsManager = "secret";
        public const string ParameterStore = "param";
    }

    // Security group protected resource types
    public static class SecurityGroupProtectedResources
    {
        public const string ApplicationLoadBalancer = "alb";
        public const string NetworkLoadBalancer = "nlb";
        public const string EcsService = "ecs";
        public const string RdsInstance = "rds";
        public const string ElastiCache = "cache";
        public const string Lambda = "lambda";
        public const string ElasticFileSystem = "efs";
        public const string OpenSearch = "search";
        public const string BastionHost = "bastion";
        public const string Vpc = "vpc";
    }
}