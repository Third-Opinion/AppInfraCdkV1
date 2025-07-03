using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class TrialFinderV2ConfigTests
{
    [Theory]
    [InlineData("Development")]
    public void GetConfigWithDevelopmentEnvironmentReturnsDevelopmentConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialFinderV2");
        config.Version.ShouldNotBeNullOrEmpty();
        config.Sizing.ShouldNotBeNull();
        config.Security.ShouldNotBeNull();
        config.Settings.ShouldNotBeNull();
        config.MultiEnvironment.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Staging")]
    public void GetConfigWithStagingEnvironmentReturnsStagingConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialFinderV2");
        config.Sizing.ShouldNotBeNull();
        config.Security.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Production")]
    public void GetConfigWithProductionEnvironmentReturnsProductionConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialFinderV2");
        config.Sizing.ShouldNotBeNull();
        config.Security.ShouldNotBeNull();
    }

    [Fact]
    public void GetConfigWithUnknownEnvironmentThrowsArgumentException()
    {
        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => TrialFinderV2Config.GetConfig("UnknownEnvironment"));
        ex.Message.ShouldStartWith("Unknown environment: UnknownEnvironment");
    }

    [Fact]
    public void GetConfigWithDevelopmentConfiguresNonProductionSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("Development");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(true); // Non-production = detailed logging

        config.Settings.ShouldContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].ShouldBe(30); // Non-production = longer timeout

        config.Settings.ShouldContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].ShouldBe(true); // Development specific

        config.Settings.ShouldContainKey("EnableDebugMode");
        config.Settings["EnableDebugMode"].ShouldBe(true); // Development specific

        config.Settings.ShouldContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].ShouldBe(false); // Development specific
    }

    [Fact]
    public void GetConfigWithProductionConfiguresProductionSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("Production");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(false); // Production = no detailed logging

        config.Settings.ShouldContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].ShouldBe(10); // Production = shorter timeout

        config.Settings.ShouldContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].ShouldBe(false); // Production specific

        config.Settings.ShouldContainKey("EnableHighAvailability");
        config.Settings["EnableHighAvailability"].ShouldBe(true); // Production specific

        config.Settings.ShouldContainKey("EnableDisasterRecovery");
        config.Settings["EnableDisasterRecovery"].ShouldBe(true); // Production specific

        config.Settings.ShouldContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].ShouldBe(true); // Production specific
    }

    [Fact]
    public void GetConfigWithStagingConfiguresProductionClassSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("Staging");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(false); // Production class = no detailed logging

        config.Settings.ShouldContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].ShouldBe(10); // Production class = shorter timeout

        config.Settings.ShouldContainKey("EnableStagingFeatures");
        config.Settings["EnableStagingFeatures"].ShouldBe(true); // Staging specific

        config.Settings.ShouldContainKey("EnableBlueGreenDeployment");
        config.Settings["EnableBlueGreenDeployment"].ShouldBe(true); // Staging specific
    }

    [Theory]
    [InlineData("Development", "0 */6 * * *")] // Every 6 hours for dev
    [InlineData("Production", "0 0 * * *")]   // Daily for production
    [InlineData("Staging", "0 0 * * *")]      // Daily for staging (production class)
    public void GetConfigConfiguresCorrectTrialDataRefreshInterval(string environment, string expectedInterval)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Settings.ShouldContainKey("TrialDataRefreshInterval");
        config.Settings["TrialDataRefreshInterval"].ShouldBe(expectedInterval);
    }

    [Theory]
    [InlineData("Development", 10)]  // Non-production = lower capacity
    [InlineData("Production", 50)]   // Production = higher capacity
    [InlineData("Staging", 50)]      // Production class = higher capacity
    public void GetConfigConfiguresCorrectMaxConcurrentProcessing(string environment, int expectedMax)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Settings.ShouldContainKey("MaxConcurrentTrialProcessing");
        config.Settings["MaxConcurrentTrialProcessing"].ShouldBe(expectedMax);
    }

    [Fact]
    public void GetConfigWithDevelopmentConfiguresMultiEnvironmentCorrectly()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("Development");

        // Assert
        config.MultiEnvironment.ShouldNotBeNull();
        config.MultiEnvironment.SupportsMultiEnvironmentDeployment.ShouldBeTrue();
        config.MultiEnvironment.SharedResources.ShouldNotBeNull();
        config.MultiEnvironment.SharedResources.VpcSharing.IsShared.ShouldBeFalse(); // Each env gets own VPC
        config.MultiEnvironment.SharedResources.DatabaseSharing.ShareInstances.ShouldBeFalse(); // Each env gets own DB
    }

    [Fact]
    public void GetConfigEnvironmentOverridesConfiguresBackupRetention()
    {
        // Act
        var devConfig = TrialFinderV2Config.GetConfig("Development");
        var stagingConfig = TrialFinderV2Config.GetConfig("Staging");
        var prodConfig = TrialFinderV2Config.GetConfig("Production");

        // Assert
        var devOverride = devConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Development");
        devOverride.BackupRetentionDays.ShouldBe(1);

        var stagingOverride = stagingConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Staging");
        stagingOverride.BackupRetentionDays.ShouldBe(7);

        var prodOverride = prodConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Production");
        prodOverride.BackupRetentionDays.ShouldBe(30);
    }

    [Theory]
    [InlineData("Development", AccountType.NonProduction, false)]
    [InlineData("Staging", AccountType.Production, true)]
    [InlineData("Production", AccountType.Production, true)]
    public void GetConfigEnvironmentOverridesConfiguresEnhancedMonitoring(string environment, AccountType expectedAccountType, bool expectedMonitoring)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        var environmentOverride = config.MultiEnvironment.GetEffectiveConfigForEnvironment(environment);
        environmentOverride.EnableEnhancedMonitoring.ShouldBe(expectedMonitoring);
        
        // Verify that the account type expectation is consistent with the enhanced monitoring setting
        // (This validates the test data consistency without accessing internal implementation details)
        var accountType = NamingConvention.GetAccountType(environment);
        accountType.ShouldBe(expectedAccountType);
    }

    [Fact]
    public void GetConfigAllEnvironmentsHaveValidApplicationName()
    {
        // Arrange
        var environments = new[] { "Development", "Staging", "Production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialFinderV2Config.GetConfig(environment);

            // Assert
            config.Name.ShouldBe("TrialFinderV2", $"because {environment} should have consistent application name");
            config.Version.ShouldNotBeNullOrEmpty($"because {environment} should have a version");
        }
    }

    [Fact]
    public void GetConfigAllEnvironmentsHaveNonNullSizing()
    {
        // Arrange
        var environments = new[] { "Development", "Staging", "Production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialFinderV2Config.GetConfig(environment);

            // Assert
            config.Sizing.ShouldNotBeNull($"because {environment} should have sizing configuration");
        }
    }
}