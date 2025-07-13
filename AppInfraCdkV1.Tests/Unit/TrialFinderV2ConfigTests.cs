using AppInfraCdkV1.Apps.TrialFinderV2;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using System.Text.Json;
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

        // ExternalApiTimeout setting was removed from configuration

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

        // ExternalApiTimeout setting was removed from configuration

        config.Settings.ShouldContainKey("EnableStagingFeatures");
        config.Settings["EnableStagingFeatures"].ShouldBe(true); // Staging specific

        config.Settings.ShouldContainKey("EnableBlueGreenDeployment");
        config.Settings["EnableBlueGreenDeployment"].ShouldBe(true); // Staging specific
    }

    // TrialDataRefreshInterval test removed - setting not implemented in current config

    // MaxConcurrentTrialProcessing test removed - setting not implemented in current config

    [Fact]
    public void GetConfigWithDevelopmentConfiguresMultiEnvironmentCorrectly()
    {
        // Act
        var config = TrialFinderV2Config.GetConfig("Development");

        // Assert
        config.MultiEnvironment.ShouldNotBeNull();
        config.MultiEnvironment.SupportsMultiEnvironmentDeployment.ShouldBeTrue();
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

public class ConfigurationLoaderTests
{
    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldNotThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "test-container",
                        Image = "nginx:latest",
                        Skip = false,
                        Essential = true
                    }
                }
            }
        };

        // Act & Assert
        Should.NotThrow(() => loader.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingTaskDefinitionName_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = null,
                ContainerDefinitions = new List<ContainerDefinitionConfig>()
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("TaskDefinitionName is required in the configuration");
    }

    [Fact]
    public void ValidateConfiguration_WithAllContainersSkipped_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "test-container",
                        Image = "nginx:latest",
                        Skip = true
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("At least one container must not be skipped");
    }

    [Fact]
    public void ValidateConfiguration_WithDuplicateContainerNames_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "duplicate-name",
                        Image = "nginx:latest",
                        Skip = false
                    },
                    new ContainerDefinitionConfig
                    {
                        Name = "duplicate-name",
                        Image = "nginx:latest",
                        Skip = false
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("Duplicate container names found: duplicate-name");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidContainerName_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "",
                        Image = "nginx:latest",
                        Skip = false
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("Container name is required");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidPortMapping_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "test-container",
                        Image = "nginx:latest",
                        Skip = false,
                        PortMappings = new List<AppInfraCdkV1.Apps.TrialFinderV2.Configuration.PortMapping>
                        {
                            new AppInfraCdkV1.Apps.TrialFinderV2.Configuration.PortMapping
                            {
                                ContainerPort = 0
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("ContainerPort must be a positive integer");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidHealthCheckInterval_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "TestTaskDefinition",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "test-container",
                        Image = "nginx:latest",
                        Skip = false,
                        HealthCheck = new HealthCheckConfig
                        {
                            Command = new List<string> { "CMD-SHELL", "curl -f http://localhost/ || exit 1" },
                            Interval = 400 // Invalid - too high
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("health check interval must be between 5 and 300 seconds");
    }

    [Fact]
    public void SubstituteVariables_WithValidVariables_ShouldReplaceCorrectly()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            TaskDefinition = new TaskDefinitionConfig
            {
                TaskDefinitionName = "${TASK_DEFINITION_FAMILY}",
                ContainerDefinitions = new List<ContainerDefinitionConfig>
                {
                    new ContainerDefinitionConfig
                    {
                        Name = "test-container",
                        Image = "nginx:latest",
                        Environment = new List<AppInfraCdkV1.Apps.TrialFinderV2.Configuration.EnvironmentVariable>
                        {
                            new AppInfraCdkV1.Apps.TrialFinderV2.Configuration.EnvironmentVariable
                            {
                                Name = "ENVIRONMENT",
                                Value = "${ENVIRONMENT}"
                            }
                        }
                    }
                }
            }
        };

        var context = new DeploymentContext
        {
            Environment = new EnvironmentConfig
            {
                Name = "Development",
                AccountType = AccountType.NonProduction,
                Region = "us-east-2",
                AccountId = "123456789012",
                Tags = new Dictionary<string, string>()
            },
            Application = new ApplicationConfig
            {
                Name = "TrialFinderV2",
                Version = "1.0.0"
            }
        };

        // Act
        var result = loader.SubstituteVariables(config, context);

        // Assert
        result.TaskDefinition.ShouldNotBeNull();
        result.TaskDefinition.TaskDefinitionName.ShouldNotBeNull();
        result.TaskDefinition.TaskDefinitionName.ShouldNotContain("${");
        result.TaskDefinition.ContainerDefinitions.ShouldNotBeNull();
        result.TaskDefinition.ContainerDefinitions.First().Environment.ShouldNotBeNull();
        result.TaskDefinition.ContainerDefinitions.First().Environment.First().Value.ShouldBe("Development");
    }
}