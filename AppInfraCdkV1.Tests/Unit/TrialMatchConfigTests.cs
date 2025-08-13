using AppInfraCdkV1.Apps.TrialMatch;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using System.Text.Json;
using Xunit;
using PortMapping = AppInfraCdkV1.Apps.TrialMatch.Configuration.PortMapping;
using EnvironmentVariable = AppInfraCdkV1.Apps.TrialMatch.Configuration.EnvironmentVariable;

namespace AppInfraCdkV1.Tests.Unit;

public class TrialMatchConfigTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Integration")]
    public void GetConfigWithDevelopmentEnvironmentReturnsDevelopmentConfig(string environment)
    {
        // Act
        var config = TrialMatchConfig.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialMatch");
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
        var config = TrialMatchConfig.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialMatch");
        config.Sizing.ShouldNotBeNull();
        config.Security.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Production")]
    public void GetConfigWithProductionEnvironmentReturnsProductionConfig(string environment)
    {
        // Act
        var config = TrialMatchConfig.GetConfig(environment);

        // Assert
        config.ShouldNotBeNull();
        config.Name.ShouldBe("TrialMatch");
        config.Sizing.ShouldNotBeNull();
        config.Security.ShouldNotBeNull();
    }

    [Fact]
    public void GetConfigWithUnknownEnvironmentThrowsArgumentException()
    {
        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => TrialMatchConfig.GetConfig("UnknownEnvironment"));
        ex.Message.ShouldStartWith("Unknown environment: UnknownEnvironment");
    }

    [Fact]
    public void GetConfigWithDevelopmentConfiguresNonProductionSettings()
    {
        // Act
        var config = TrialMatchConfig.GetConfig("Development");

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
        var config = TrialMatchConfig.GetConfig("Production");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(false); // Production = no detailed logging

        config.Settings.ShouldContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].ShouldBe(false); // Production specific

        config.Settings.ShouldContainKey("EnableHighAvailability");
        config.Settings["EnableHighAvailability"].ShouldBe(true); // Production specific

        config.Settings.ShouldContainKey("EnableDisasterRecovery");
        config.Settings["EnableDisasterRecovery"].ShouldBe(true); // Production specific

        config.Settings.ShouldContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].ShouldBe(true); // Production specific

        config.Settings.ShouldContainKey("EnableAdvancedMonitoring");
        config.Settings["EnableAdvancedMonitoring"].ShouldBe(true); // Production specific
    }

    [Fact]
    public void GetConfigWithStagingConfiguresProductionClassSettings()
    {
        // Act
        var config = TrialMatchConfig.GetConfig("Staging");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(false); // Production class = no detailed logging

        config.Settings.ShouldContainKey("EnableStagingFeatures");
        config.Settings["EnableStagingFeatures"].ShouldBe(true); // Staging specific

        config.Settings.ShouldContainKey("EnableBlueGreenDeployment");
        config.Settings["EnableBlueGreenDeployment"].ShouldBe(true); // Staging specific

        config.Settings.ShouldContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].ShouldBe(true); // Staging specific
    }

    [Fact]
    public void GetConfigWithIntegrationConfiguresDevelopmentClassSettings()
    {
        // Act
        var config = TrialMatchConfig.GetConfig("Integration");

        // Assert
        config.Settings.ShouldContainKey("EnableDetailedLogging");
        config.Settings["EnableDetailedLogging"].ShouldBe(true); // Development class = detailed logging

        config.Settings.ShouldContainKey("EnableMockExternalServices");
        config.Settings["EnableMockExternalServices"].ShouldBe(true); // Integration specific

        config.Settings.ShouldContainKey("EnableDebugMode");
        config.Settings["EnableDebugMode"].ShouldBe(true); // Integration specific

        config.Settings.ShouldContainKey("CacheEnabled");
        config.Settings["CacheEnabled"].ShouldBe(false); // Integration specific
    }

    [Fact]
    public void GetConfigWithDevelopmentConfiguresMultiEnvironmentCorrectly()
    {
        // Act
        var config = TrialMatchConfig.GetConfig("Development");

        // Assert
        config.MultiEnvironment.ShouldNotBeNull();
        config.MultiEnvironment.SupportsMultiEnvironmentDeployment.ShouldBeTrue();
    }

    [Fact]
    public void GetConfigEnvironmentOverridesConfiguresBackupRetention()
    {
        // Act
        var devConfig = TrialMatchConfig.GetConfig("Development");
        var integrationConfig = TrialMatchConfig.GetConfig("Integration");
        var stagingConfig = TrialMatchConfig.GetConfig("Staging");
        var prodConfig = TrialMatchConfig.GetConfig("Production");

        // Assert
        var devOverride = devConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Development");
        devOverride.BackupRetentionDays.ShouldBe(1);

        var integrationOverride = integrationConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Integration");
        integrationOverride.BackupRetentionDays.ShouldBe(1);

        var stagingOverride = stagingConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Staging");
        stagingOverride.BackupRetentionDays.ShouldBe(7);

        var prodOverride = prodConfig.MultiEnvironment.GetEffectiveConfigForEnvironment("Production");
        prodOverride.BackupRetentionDays.ShouldBe(30);
    }

    [Theory]
    [InlineData("Development", AccountType.NonProduction, false)]
    [InlineData("Integration", AccountType.NonProduction, false)]
    [InlineData("Staging", AccountType.Production, true)]
    [InlineData("Production", AccountType.Production, true)]
    public void GetConfigEnvironmentOverridesConfiguresEnhancedMonitoring(string environment, AccountType expectedAccountType, bool expectedMonitoring)
    {
        // Act
        var config = TrialMatchConfig.GetConfig(environment);

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
        var environments = new[] { "Development", "Integration", "Staging", "Production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialMatchConfig.GetConfig(environment);

            // Assert
            config.Name.ShouldBe("TrialMatch", $"because {environment} should have consistent application name");
            config.Version.ShouldNotBeNullOrEmpty($"because {environment} should have a version");
        }
    }

    [Fact]
    public void GetConfigAllEnvironmentsHaveNonNullSizing()
    {
        // Arrange
        var environments = new[] { "Development", "Integration", "Staging", "Production" };

        foreach (var environment in environments)
        {
            // Act
            var config = TrialMatchConfig.GetConfig(environment);

            // Assert
            config.Sizing.ShouldNotBeNull($"because {environment} should have sizing configuration");
        }
    }



}

public class TrialMatchConfigurationLoaderTests
{
    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldNotThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    Secrets = new List<string> { "test-secret", "api-key" }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        Should.NotThrow(() => loader.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingServiceName_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "",
                    TaskDefinition = new List<TaskDefinitionConfig>()
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("ServiceName is required in the configuration");
    }

    [Fact]
    public void ValidateConfiguration_WithEmptyServicesList_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>()
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("At least one Service configuration is required");
    }

    [Fact]
    public void ValidateConfiguration_WithDuplicateServiceNames_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "duplicate-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition1",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>()
                        }
                    }
                },
                new ServiceConfig
                {
                    ServiceName = "duplicate-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition2",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>()
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("Duplicate service names found: duplicate-service");
    }

    [Fact]
    public void ValidateConfiguration_WithMissingTaskDefinitionName_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>()
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("TaskDefinitionName is required in the configuration for service 'test-service'");
    }

    [Fact]
    public void ValidateConfiguration_WithEmptyTaskDefinitionList_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>()
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldBe("At least one TaskDefinition configuration is required for service 'test-service'");
    }

    [Fact]
    public void ValidateConfiguration_WithDuplicateContainerNames_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "duplicate-name",
                                    Image = "nginx:latest",
                                    Essential = true
                                },
                                new ContainerDefinitionConfig
                                {
                                    Name = "duplicate-name",
                                    Image = "nginx:latest",
                                    Essential = true
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("Duplicate container names found in task 'TestTaskDefinition' for service 'test-service': duplicate-name");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidContainerName_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "",
                                    Image = "nginx:latest",
                                    Essential = true
                                }
                            }
                        }
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
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    PortMappings = new List<PortMapping>
                                    {
                                        new PortMapping
                                        {
                                            ContainerPort = -1,
                                            Protocol = "tcp"
                                        }
                                    }
                                }
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
    public void ValidateConfiguration_WithMissingProtocol_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    PortMappings = new List<PortMapping>
                                    {
                                        new PortMapping
                                        {
                                            ContainerPort = 8080,
                                            Protocol = null
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("Protocol is required");
    }

    [Fact]
    public void SubstituteVariables_WithValidVariables_ShouldReplaceCorrectly()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    Environment = new List<EnvironmentVariable>
                                    {
                                        new EnvironmentVariable
                                        {
                                            Name = "API_URL",
                                            Value = "http://${SERVICE_NAME}-api.example.com"
                                        }
                                    }
                                }
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
                Name = "TrialMatch",
                Version = "1.0.0"
            }
        };

        // Act
        var result = loader.SubstituteVariables(config, context);

        // Assert
        result.Services[0].TaskDefinition[0].ContainerDefinitions[0].Environment[0].Value.ShouldBe("http://dev-tm-svc-ue2-web-api.example.com");
    }

    [Fact]
    public void ValidateConfiguration_WithSecrets_ShouldNotThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    Secrets = new List<string> { "test-secret", "api-key", "db-password" }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        Should.NotThrow(() => loader.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_WithEmptySecrets_ShouldNotThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    Secrets = new List<string>()
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        Should.NotThrow(() => loader.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_WithNullSecrets_ShouldNotThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    Secrets = null
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        Should.NotThrow(() => loader.ValidateConfiguration(config));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidProtocol_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    PortMappings = new List<PortMapping>
                                    {
                                        new PortMapping
                                        {
                                            ContainerPort = 8080,
                                            Protocol = "invalid-protocol"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("Protocol must be 'tcp' or 'udp'");
    }

    [Fact]
    public void ValidateConfiguration_WithPortExceedingMaxValue_ShouldThrow()
    {
        // Arrange
        var loader = new ConfigurationLoader();
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "test-service",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TestTaskDefinition",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "nginx:latest",
                                    Essential = true,
                                    PortMappings = new List<PortMapping>
                                    {
                                        new PortMapping
                                        {
                                            ContainerPort = 70000,
                                            Protocol = "tcp"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() => loader.ValidateConfiguration(config));
        exception.Message.ShouldContain("ContainerPort must be <= 65535");
    }
} 