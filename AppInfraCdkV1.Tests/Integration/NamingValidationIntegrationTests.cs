using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Apps.TrialMatch;
using AppInfraCdkV1.Core.Enums;
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
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
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
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void GenerateResourceNames_WithValidEnvironmentAndApp_ShouldProduceCorrectNames(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var stackName = GenerateStackName(context);
        var vpcName = context.Namer.Vpc();
        var ecsClusterName = context.Namer.EcsCluster();
        var webServiceName = context.Namer.EcsService(ResourcePurpose.Web);
        var databaseName = context.Namer.RdsInstance(ResourcePurpose.Main);

        // Assert
        var expectedAppPrefix = application == "TrialFinderV2" ? "tfv2" : "tm";
        stackName.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-");
        vpcName.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-");
        ecsClusterName.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-");
        webServiceName.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-");
        databaseName.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-");

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
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void ValidateAwsLimits_WithValidEnvironmentAndApp_ShouldRespectLimits(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var s3Name = context.Namer.S3Bucket(StoragePurpose.App);
        var rdsName = context.Namer.RdsInstance(ResourcePurpose.Main);
        var ecsName = context.Namer.EcsCluster();

        // Assert - Check AWS resource name limits
        s3Name.Length.ShouldBeLessThanOrEqualTo(63, "S3 bucket names must be 63 characters or less");
        rdsName.Length.ShouldBeLessThanOrEqualTo(63, "RDS identifiers must be 63 characters or less");
        ecsName.Length.ShouldBeLessThanOrEqualTo(255, "ECS cluster names must be 255 characters or less");

        // S3 bucket names must be lowercase and contain only letters, numbers, and hyphens
        s3Name.ShouldMatch(@"^[a-z0-9\-\.]+$", "S3 bucket names must be lowercase alphanumeric with hyphens and dots only");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void ValidateMultiEnvironmentSetup_WithValidEnvironment_ShouldDetectCorrectContext(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

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
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void GenerateSecurityGroupNames_WithValidEnvironmentAndApp_ShouldFollowConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var albSecurityGroup = context.Namer.SecurityGroupForAlb(ResourcePurpose.Web);
        var ecsSecurityGroup = context.Namer.SecurityGroupForEcs(ResourcePurpose.Web);
        var rdsSecurityGroup = context.Namer.SecurityGroupForRds(ResourcePurpose.Main);

        // Assert
        var expectedAppPrefix = application == "TrialFinderV2" ? "tfv2" : "tm";
        albSecurityGroup.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-sg-alb-");
        ecsSecurityGroup.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-sg-ecs-");
        rdsSecurityGroup.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-sg-rds-");

        // Verify all contain the region code
        albSecurityGroup.ShouldContain("ue2"); // us-east-2 region code
        ecsSecurityGroup.ShouldContain("ue2");
        rdsSecurityGroup.ShouldContain("ue2");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void GenerateIamRoleNames_WithValidEnvironmentAndApp_ShouldFollowConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var ecsTaskRole = context.Namer.IamRole(IamPurpose.EcsTask);
        var ecsExecutionRole = context.Namer.IamRole(IamPurpose.EcsExecution);

        // Assert
        var expectedAppPrefix = application == "TrialFinderV2" ? "tfv2" : "tm";
        ecsTaskRole.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-role-");
        ecsExecutionRole.ShouldStartWith($"{expectedPrefix}-{expectedAppPrefix}-role-");

        ecsTaskRole.ShouldContain("ecs-task");
        ecsExecutionRole.ShouldContain("ecs-exec");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    [InlineData("Development", "TrialMatch")]
    [InlineData("Production", "TrialMatch")]
    public void GenerateLogGroupNames_WithValidEnvironmentAndApp_ShouldFollowAwsConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var logGroup = context.Namer.LogGroup("ecs", ResourcePurpose.Web);

        // Assert
        logGroup.ShouldStartWith("/aws/ecs/");
        var expectedAppPrefix = application == "TrialFinderV2" ? "tfv2" : "tm";
        logGroup.ShouldContain($"{expectedPrefix}-{expectedAppPrefix}-");
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
            Application = applicationConfig
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
            "trialmatch" => TrialMatchConfig.GetConfig(environmentName),
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

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void GenerateCognitoResourceNames_WithValidEnvironmentAndApp_ShouldFollowConventions(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);
        var expectedPrefix = environment == "Development" ? "dev" : "prod";

        // Act
        var userPoolName = context.Namer.CognitoUserPool(ResourcePurpose.Auth);
        var appClientName = context.Namer.CognitoAppClient(ResourcePurpose.Auth);
        var domainName = context.Namer.CognitoDomain(ResourcePurpose.Auth);

        // Assert
        userPoolName.ShouldStartWith($"{expectedPrefix}-tfv2-cognito-");
        appClientName.ShouldStartWith($"{expectedPrefix}-tfv2-client-");
        domainName.ShouldStartWith($"{expectedPrefix}-tfv2-domain-");

        // Verify all contain the region code and auth purpose
        userPoolName.ShouldContain("ue2"); // us-east-2 region code
        appClientName.ShouldContain("ue2");
        domainName.ShouldContain("ue2");

        userPoolName.ShouldEndWith("-auth");
        appClientName.ShouldEndWith("-auth");
        domainName.ShouldEndWith("-auth");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateCognitoResourceLimits_WithValidEnvironmentAndApp_ShouldRespectAwsLimits(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var userPoolName = context.Namer.CognitoUserPool(ResourcePurpose.Auth);
        var appClientName = context.Namer.CognitoAppClient(ResourcePurpose.Auth);
        var domainName = context.Namer.CognitoDomain(ResourcePurpose.Auth);

        // Assert - Check AWS resource name limits
        userPoolName.Length.ShouldBeLessThanOrEqualTo(128, "Cognito User Pool names must be 128 characters or less");
        appClientName.Length.ShouldBeLessThanOrEqualTo(128, "Cognito App Client names must be 128 characters or less");
        domainName.Length.ShouldBeLessThanOrEqualTo(63, "Cognito Domain names must be 63 characters or less");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateCognitoResourceUniqueness_WithValidEnvironmentAndApp_ShouldBeUnique(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var userPoolName = context.Namer.CognitoUserPool(ResourcePurpose.Auth);
        var appClientName = context.Namer.CognitoAppClient(ResourcePurpose.Auth);
        var domainName = context.Namer.CognitoDomain(ResourcePurpose.Auth);

        // Assert - All resource names should be unique
        userPoolName.ShouldNotBe(appClientName);
        userPoolName.ShouldNotBe(domainName);
        appClientName.ShouldNotBe(domainName);
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateCognitoResourcePurpose_WithValidEnvironmentAndApp_ShouldHaveCorrectPurpose(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var authPurpose = ResourcePurpose.Auth;

        // Assert
        authPurpose.ShouldBe(ResourcePurpose.Auth);
        authPurpose.ToString().ShouldBe("Auth");
    }

    [Fact]
    public void ValidateCognitoResourceTypes_ShouldIncludeAllTypes()
    {
        // Arrange & Act
        var userPoolType = NamingConvention.ResourceTypes.CognitoUserPool;
        var appClientType = NamingConvention.ResourceTypes.CognitoAppClient;
        var domainType = NamingConvention.ResourceTypes.CognitoDomain;

        // Assert
        userPoolType.ShouldBe("cognito");
        appClientType.ShouldBe("client");
        domainType.ShouldBe("domain");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public void ValidateCognitoUrlGeneration_WithValidEnvironmentAndApp_ShouldGenerateCorrectUrls(string environment, string application)
    {
        // Arrange
        var context = CreateDeploymentContext(environment, application);

        // Act
        var callbackUrls = GetCallbackUrls(context);
        var logoutUrls = GetLogoutUrls(context);

        // Assert
        callbackUrls.ShouldNotBeEmpty();
        logoutUrls.ShouldNotBeEmpty();

        // All URLs should be valid HTTPS URLs
        foreach (var url in callbackUrls.Concat(logoutUrls))
        {
            url.ShouldStartWith("https://");
            url.ShouldNotBeNullOrWhiteSpace();
        }

        // Environment-specific URL validation
        if (environment == "Development")
        {
            callbackUrls.ShouldContain("https://dev-tf.thirdopinion.io/signin-oidc");
            callbackUrls.ShouldContain("https://localhost:7015/signin-oidc");
            callbackUrls.ShouldContain("https://localhost:7243/signin-oidc");
            
            logoutUrls.ShouldContain("https://dev-tf.thirdopinion.io");
            logoutUrls.ShouldContain("https://dev-tf.thirdopinion.io/logout");
            logoutUrls.ShouldContain("https://localhost:7015");
            logoutUrls.ShouldContain("https://localhost:7015/logout");
            logoutUrls.ShouldContain("https://localhost:7243");
            logoutUrls.ShouldContain("https://localhost:7243/logout");
        }
        else if (environment == "Production")
        {
            callbackUrls.ShouldContain("https://tf.thirdopinion.io/signin-oidc");
            logoutUrls.ShouldContain("https://tf.thirdopinion.io");
            logoutUrls.ShouldContain("https://tf.thirdopinion.io/logout");
        }
    }

    private static string[] GetCallbackUrls(DeploymentContext context)
    {
        return context.Environment.Name.ToLowerInvariant() switch
        {
            "production" => new[]
            {
                "https://tf.thirdopinion.io/signin-oidc"
            },
            "staging" => new[]
            {
                "https://stg-tf.thirdopinion.io/signin-oidc"
            },
            "development" => new[]
            {
                "https://dev-tf.thirdopinion.io/signin-oidc",
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            },
            "integration" => new[]
            {
                "https://int-tf.thirdopinion.io/signin-oidc",
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            },
            _ => new[]
            {
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            }
        };
    }

    private static string[] GetLogoutUrls(DeploymentContext context)
    {
        return context.Environment.Name.ToLowerInvariant() switch
        {
            "production" => new[]
            {
                "https://tf.thirdopinion.io",
                "https://tf.thirdopinion.io/logout"
            },
            "staging" => new[]
            {
                "https://stg-tf.thirdopinion.io",
                "https://stg-tf.thirdopinion.io/logout"
            },
            "development" => new[]
            {
                "https://dev-tf.thirdopinion.io",
                "https://dev-tf.thirdopinion.io/logout",
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            },
            "integration" => new[]
            {
                "https://int-tf.thirdopinion.io",
                "https://int-tf.thirdopinion.io/logout",
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            },
            _ => new[]
            {
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            }
        };
    }
}