using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Core.Models;
using FluentAssertions;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class TrialFinderV2ConfigTests
{
    [Theory]
    [InlineData("development")]
    [InlineData("Development")]
    [InlineData("DEVELOPMENT")]
    public void GetConfig_WithDevelopmentEnvironment_ReturnsDevelopmentConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Should().NotBeNull();
        config.Name.Should().Be("TrialFinderV2");
        config.Version.Should().NotBeNullOrEmpty();
        config.Sizing.Should().NotBeNull();
        config.Security.Should().NotBeNull();
        config.Settings.Should().NotBeNull();
        config.MultiEnvironment.Should().NotBeNull();
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("Staging")]
    [InlineData("STAGING")]
    public void GetConfig_WithStagingEnvironment_ReturnsStagingConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Should().NotBeNull();
        config.Name.Should().Be("TrialFinderV2");
        config.Sizing.Should().NotBeNull();
        config.Security.Should().NotBeNull();
    }

    [Theory]
    [InlineData("production")]
    [InlineData("Production")]
    [InlineData("PRODUCTION")]
    public void GetConfig_WithProductionEnvironment_ReturnsProductionConfig(string environment)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Should().NotBeNull();
        config.Name.Should().Be("TrialFinderV2");
        config.Sizing.Should().NotBeNull();
        config.Security.Should().NotBeNull();
    }

    [Fact]
    public void GetConfig_WithUnknownEnvironment_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => TrialFinderV2Config.GetConfig("UnknownEnvironment");
        action.Should().Throw<ArgumentException>()
            .WithMessage("Unknown environment: UnknownEnvironment");
    }

    [Fact]
    public void GetConfig_WithDevelopment_ConfiguresNonProductionSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("development");

        // Assert
        config.Settings.Should().ContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].Should().Be(true); // Non-production = detailed logging

        config.Settings.Should().ContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].Should().Be(30); // Non-production = longer timeout

        config.Settings.Should().ContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].Should().Be(true); // Development specific

        config.Settings.Should().ContainKey("EnableDebugMode");
        config.Settings["EnableDebugMode"].Should().Be(true); // Development specific

        config.Settings.Should().ContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].Should().Be(false); // Development specific
    }

    [Fact]
    public void GetConfig_WithProduction_ConfiguresProductionSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("production");

        // Assert
        config.Settings.Should().ContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].Should().Be(false); // Production = no detailed logging

        config.Settings.Should().ContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].Should().Be(10); // Production = shorter timeout

        config.Settings.Should().ContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].Should().Be(false); // Production specific

        config.Settings.Should().ContainKey("EnableHighAvailability");
        config.Settings["EnableHighAvailability"].Should().Be(true); // Production specific

        config.Settings.Should().ContainKey("EnableDisasterRecovery");
        config.Settings["EnableDisasterRecovery"].Should().Be(true); // Production specific

        config.Settings.Should().ContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].Should().Be(true); // Production specific
    }

    [Fact]
    public void GetConfig_WithStaging_ConfiguresProductionClassSettings()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("staging");

        // Assert
        config.Settings.Should().ContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].Should().Be(false); // Production class = no detailed logging

        config.Settings.Should().ContainKey("ExternalApiTimeout");
        config.Settings["ExternalApiTimeout"].Should().Be(10); // Production class = shorter timeout

        config.Settings.Should().ContainKey("EnableStagingFeatures");
        config.Settings["EnableStagingFeatures"].Should().Be(true); // Staging specific

        config.Settings.Should().ContainKey("EnableBlueGreenDeployment");
        config.Settings["EnableBlueGreenDeployment"].Should().Be(true); // Staging specific
    }

    [Theory]
    [InlineData("development", "0 */6 * * *")] // Every 6 hours for dev
    [InlineData("production", "0 0 * * *")]   // Daily for production
    [InlineData("staging", "0 0 * * *")]      // Daily for staging (production class)
    public void GetConfig_ConfiguresCorrectTrialDataRefreshInterval(string environment, string expectedInterval)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Settings.Should().ContainKey("TrialDataRefreshInterval");
        config.Settings["TrialDataRefreshInterval"].Should().Be(expectedInterval);
    }

    [Theory]
    [InlineData("development", 10)]  // Non-production = lower capacity
    [InlineData("production", 50)]   // Production = higher capacity
    [InlineData("staging", 50)]      // Production class = higher capacity
    public void GetConfig_ConfiguresCorrectMaxConcurrentProcessing(string environment, int expectedMax)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        config.Settings.Should().ContainKey("MaxConcurrentTrialProcessing");
        config.Settings["MaxConcurrentTrialProcessing"].Should().Be(expectedMax);
    }

    [Fact]
    public void GetConfig_WithDevelopment_ConfiguresMultiEnvironmentCorrectly()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("development");

        // Assert
        config.MultiEnvironment.Should().NotBeNull();
        config.MultiEnvironment.SupportsMultiEnvironmentDeployment.Should().BeTrue();
        config.MultiEnvironment.SharedResources.Should().NotBeNull();
        config.MultiEnvironment.SharedResources.VpcSharing.IsShared.Should().BeFalse(); // Each env gets own VPC
        config.MultiEnvironment.SharedResources.DatabaseSharing.ShareInstances.Should().BeFalse(); // Each env gets own DB
    }

    [Fact]
    public void GetConfig_EnvironmentOverrides_ConfiguresBackupRetention()
    {
        // Act
        var devConfig = TrialFinderV2Config.GetConfig("development");
        var stagingConfig = TrialFinderV2Config.GetConfig("staging");
        var prodConfig = TrialFinderV2Config.GetConfig("production");

        // Assert
        var devOverride = devConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("development");
        devOverride.BackupRetentionDays.Should().Be(1);

        var stagingOverride = stagingConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("staging");
        stagingOverride.BackupRetentionDays.Should().Be(7);

        var prodOverride = prodConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("production");
        prodOverride.BackupRetentionDays.Should().Be(30);
    }

    [Theory]
    [InlineData("development", AccountType.NonProduction, false)]
    [InlineData("staging", AccountType.Production, true)]
    [InlineData("production", AccountType.Production, true)]
    public void GetConfig_EnvironmentOverrides_ConfiguresEnhancedMonitoring(string environment, AccountType expectedAccountType, bool expectedMonitoring)
    {
        // Act
        var config = TrialFinderV2Config.GetConfig(environment);

        // Assert
        var environmentOverride = config.MultiEnvironment.GetEffectiveConfigForEnvironment(environment);
        environmentOverride.EnableEnhancedMonitoring.Should().Be(expectedMonitoring);
    }

    [Fact]
    public void GetConfig_AllEnvironments_HaveValidApplicationName()
    {
        // Arrange
        var environments = new[] { "development", "staging", "production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialFinderV2Config.GetConfig(environment);

            // Assert
            config.Name.Should().Be("TrialFinderV2", $"because {environment} should have consistent application name");
            config.Version.Should().NotBeNullOrEmpty($"because {environment} should have a version");
        }
    }

    [Fact]
    public void GetConfig_AllEnvironments_HaveNonNullSizing()
    {
        // Arrange
        var environments = new[] { "development", "staging", "production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialFinderV2Config.GetConfig(environment);

            // Assert
            config.Sizing.Should().NotBeNull($"because {environment} should have sizing configuration");
        }
    }
}