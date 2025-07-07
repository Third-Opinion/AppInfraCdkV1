using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Integration;

/// <summary>
/// Integration tests that validate the naming functionality that would be used by 
/// the --show-names-only command. These tests verify the core logic without 
/// executing the full CDK deployment process.
/// </summary>
public class NamingValidationIntegrationTests
{
    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateNamingConventions_WithValidEnvironmentAndApp_ShouldSucceed(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act & Assert - Should not throw
        Should.NotThrow(() => context.ValidateNamingContext());
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void GenerateResourceNames_WithValidEnvironmentAndApp_ShouldProduceCorrectNames(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var stackName = GenerateStackName(context);
        var vpcName = context.Namer.Vpc();
        var ecsClusterName = context.Namer.EcsCluster();
        var webServiceName = context.Namer.EcsService("web");
        var databaseName = context.Namer.RdsInstance("main");

        // Assert
        stackName.ShouldStartWith($"{expectedPrefix}-tfv2-");
        vpcName.ShouldStartWith($"{expectedPrefix}-tfv2-");
        ecsClusterName.ShouldStartWith($"{expectedPrefix}-tfv2-");
        webServiceName.ShouldStartWith($"{expectedPrefix}-tfv2-");
        databaseName.ShouldStartWith($"{expectedPrefix}-tfv2-");

        // Verify all names follow the expected pattern
        stackName.ShouldContain("stack");
        vpcName.ShouldContain("vpc");
        ecsClusterName.ShouldContain("ecs");
        webServiceName.ShouldContain("svc");
        databaseName.ShouldContain("rds");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateAwsLimits_WithValidEnvironmentAndApp_ShouldRespectLimits(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var s3Name = context.Namer.S3Bucket("app");
        var rdsName = context.Namer.RdsInstance("main");
        var ecsName = context.Namer.EcsCluster();

        // Assert - Check AWS resource name limits
        s3Name.Length.ShouldBeLessThanOrEqualTo(63, "S3 bucket names must be 63 characters or less");
        rdsName.Length.ShouldBeLessThanOrEqualTo(63, "RDS identifiers must be 63 characters or less");
        ecsName.Length.ShouldBeLessThanOrEqualTo(255, "ECS cluster names must be 255 characters or less");

        // S3 bucket names must be lowercase and contain only letters, numbers, and hyphens
        s3Name.ShouldMatch(@"^[a-z0-9\-\.]+$", "S3 bucket names must be lowercase alphanumeric with hyphens and dots only");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ValidateMultiEnvironmentSetup_WithValidEnvironment_ShouldDetectCorrectContext(string environment)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, "TrialFinderV2");

        // Act
        var siblingEnvironments = context.Environment.GetAccountSiblingEnvironments();
        var accountType = context.Environment.AccountType;

        // Assert
        siblingEnvironments.ShouldNotBeEmpty("Should detect sibling environments in the account");
        siblingEnvironments.ShouldContain(environment, "Should include the current environment");

        if (environment == "Development")
        {
            accountType.ShouldBe(AccountType.NonProduction);
            siblingEnvironments.ShouldContain("Integration");
        }
        else if (environment == "Production")
        {
            accountType.ShouldBe(AccountType.Production);
        }
    }

    [Fact]
    public void ValidateAccountLevelUniqueness_WithDifferentResourceTypes_ShouldNotConflict()
    {
        // Arrange
        var devContext = CreateDeploymentContext("Development", "TrialFinderV2");
        var testResources = new[] { ("ecs", "main"), ("rds", "main"), ("vpc", "main") };

        // Act & Assert - Should not throw for account-level uniqueness
        foreach (var (resourceType, purpose) in testResources)
        {
            Should.NotThrow(() => 
                NamingConvention.ValidateAccountLevelUniqueness(devContext, resourceType, purpose),
                $"Resource {resourceType}:{purpose} should pass uniqueness validation"
            );
        }
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void GenerateSecurityGroupNames_WithValidEnvironmentAndApp_ShouldFollowConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var albSecurityGroup = context.Namer.SecurityGroupForAlb("web");
        var ecsSecurityGroup = context.Namer.SecurityGroupForEcs("web");
        var rdsSecurityGroup = context.Namer.SecurityGroupForRds("main");

        // Assert
        albSecurityGroup.ShouldStartWith($"{expectedPrefix}-tfv2-sg-alb-");
        ecsSecurityGroup.ShouldStartWith($"{expectedPrefix}-tfv2-sg-ecs-");
        rdsSecurityGroup.ShouldStartWith($"{expectedPrefix}-tfv2-sg-rds-");

        // Verify all contain the region code
        albSecurityGroup.ShouldContain("ue2"); // us-east-2 region code
        ecsSecurityGroup.ShouldContain("ue2");
        rdsSecurityGroup.ShouldContain("ue2");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void GenerateIamRoleNames_WithValidEnvironmentAndApp_ShouldFollowConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var ecsTaskRole = context.Namer.IamRole("ecs-task");
        var ecsExecutionRole = context.Namer.IamRole("ecs-execution");

        // Assert
        ecsTaskRole.ShouldStartWith($"{expectedPrefix}-tfv2-role-");
        ecsExecutionRole.ShouldStartWith($"{expectedPrefix}-tfv2-role-");

        ecsTaskRole.ShouldContain("ecs-task");
        ecsExecutionRole.ShouldContain("ecs-execution");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void GenerateLogGroupNames_WithValidEnvironmentAndApp_ShouldFollowAwsConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var logGroup = context.Namer.LogGroup("ecs", "web");

        // Assert
        logGroup.ShouldStartWith("/aws/ecs/");
        logGroup.ShouldContain($"{expectedPrefix}-tfv2-");
    }

    private static DeploymentContext CreateDeploymentContext(string environment, string application)
    {
        // Create configuration similar to how Program.cs does it
        var configuration = new ConfigurationBuilder()
            .SetBasePath(GetSolutionRoot())
            .AddJsonFile("AppInfraCdkV1.Deploy/appsettings.json")
            .Build();

        var environmentConfig = GetEnvironmentConfig(configuration, environment);
        var applicationConfig = GetApplicationConfig(application, environment);

        return new DeploymentContext
        {
            Environment = environmentConfig,
            Application = applicationConfig,
            DeployedBy = "IntegrationTest"
        };
    }

    private static string GetSolutionRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any())
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find solution root");
    }

    private static EnvironmentConfig GetEnvironmentConfig(IConfiguration config, string environmentName)
    {
        var section = config.GetSection($"Environments:{environmentName}");
        if (!section.Exists())
            throw new ArgumentException($"Environment configuration not found: {environmentName}");

        var envConfig = new EnvironmentConfig
        {
            Name = environmentName,
            AccountId = section["AccountId"] ?? throw new ArgumentException("AccountId is required"),
            Region = section["Region"] ?? "us-east-1",
            Tags = section.GetSection("Tags").Get<Dictionary<string, string>>() ?? new()
        };

        // Parse account type
        if (Enum.TryParse<AccountType>(section["AccountType"], out var accountType))
            envConfig.AccountType = accountType;
        else
            envConfig.AccountType = NamingConvention.GetAccountType(environmentName);

        return envConfig;
    }

    private static ApplicationConfig GetApplicationConfig(string appName, string environmentName)
    {
        return appName.ToLower() switch
        {
            "trialfinderv2" => TrialFinderV2Config.GetConfig(environmentName),
            _ => throw new ArgumentException($"Unknown application configuration: {appName}")
        };
    }

    private static string GenerateStackName(DeploymentContext context)
    {
        var envPrefix = NamingConvention.GetEnvironmentPrefix(context.Environment.Name);
        var appCode = NamingConvention.GetApplicationCode(context.Application.Name);
        var regionCode = NamingConvention.GetRegionCode(context.Environment.Region);
        return $"{envPrefix}-{appCode}-stack-{regionCode}";
    }
}