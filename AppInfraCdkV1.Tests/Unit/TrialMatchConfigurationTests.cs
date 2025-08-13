using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class ConfigurationModelsTests
{
    [Fact]
    public void EcsTaskConfiguration_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
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
                                    Essential = true
                                }
                            }
                        }
                    }
                }
            }
        };

        // Assert
        config.ShouldNotBeNull();
        config.Services.ShouldNotBeNull();
        config.Services.Count.ShouldBe(1);
        config.Services[0].ServiceName.ShouldBe("test-service");
    }

    [Fact]
    public void ServiceConfig_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var serviceConfig = new ServiceConfig
        {
            ServiceName = "test-service",
            TaskDefinition = new List<TaskDefinitionConfig>()
        };

        // Assert
        serviceConfig.ShouldNotBeNull();
        serviceConfig.ServiceName.ShouldBe("test-service");
        serviceConfig.TaskDefinition.ShouldNotBeNull();
    }

    [Fact]
    public void TaskDefinitionConfig_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var taskDefinitionConfig = new TaskDefinitionConfig
        {
            TaskDefinitionName = "TestTaskDefinition",
            ContainerDefinitions = new List<ContainerDefinitionConfig>()
        };

        // Assert
        taskDefinitionConfig.ShouldNotBeNull();
        taskDefinitionConfig.TaskDefinitionName.ShouldBe("TestTaskDefinition");
        taskDefinitionConfig.ContainerDefinitions.ShouldNotBeNull();
    }

    [Fact]
    public void ContainerDefinitionConfig_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var containerDefinitionConfig = new ContainerDefinitionConfig
        {
            Name = "test-container",
            Image = "nginx:latest",
            Essential = true,
            PortMappings = new List<PortMapping>(),
            Environment = new List<EnvironmentVariable>(),
            Secrets = new List<string>()
        };

        // Assert
        containerDefinitionConfig.ShouldNotBeNull();
        containerDefinitionConfig.Name.ShouldBe("test-container");
        containerDefinitionConfig.Image.ShouldBe("nginx:latest");
        containerDefinitionConfig.Essential.ShouldBe(true);
    }

    [Fact]
    public void PortMapping_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var portMapping = new PortMapping
        {
            ContainerPort = 80,
            Protocol = "tcp"
        };

        // Assert
        portMapping.ShouldNotBeNull();
        portMapping.ContainerPort.ShouldBe(80);
        portMapping.Protocol.ShouldBe("tcp");
    }

    [Fact]
    public void EnvironmentVariable_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var environmentVariable = new EnvironmentVariable
        {
            Name = "TEST_VAR",
            Value = "test-value"
        };

        // Assert
        environmentVariable.ShouldNotBeNull();
        environmentVariable.Name.ShouldBe("TEST_VAR");
        environmentVariable.Value.ShouldBe("test-value");
    }

    [Fact]
    public void HealthCheckConfig_WithValidData_ShouldCreateInstance()
    {
        // Arrange & Act
        var healthCheckConfig = new HealthCheckConfig
        {
            Command = new List<string> { "CMD-SHELL", "curl -f http://localhost/health || exit 1" },
            Interval = 30,
            Timeout = 5,
            Retries = 3,
            StartPeriod = 60
        };

        // Assert
        healthCheckConfig.ShouldNotBeNull();
        healthCheckConfig.Command.ShouldNotBeNull();
        healthCheckConfig.Command.Count.ShouldBe(2);
        healthCheckConfig.Interval.ShouldBe(30);
        healthCheckConfig.Timeout.ShouldBe(5);
        healthCheckConfig.Retries.ShouldBe(3);
        healthCheckConfig.StartPeriod.ShouldBe(60);
    }

    [Fact]
    public void HealthCheckConfig_WithDefaultValues_ShouldUseDefaults()
    {
        // Arrange & Act
        var healthCheckConfig = new HealthCheckConfig
        {
            Command = new List<string> { "CMD-SHELL", "echo 'healthy'" }
        };

        // Assert
        healthCheckConfig.ShouldNotBeNull();
        healthCheckConfig.Command.ShouldNotBeNull();
        healthCheckConfig.Command.Count.ShouldBe(2);
        // Default values should be applied
        healthCheckConfig.Interval.ShouldBe(30);
        healthCheckConfig.Timeout.ShouldBe(5);
        healthCheckConfig.Retries.ShouldBe(3);
        healthCheckConfig.StartPeriod.ShouldBe(60);
    }

    [Fact]
    public void ContainerDefinitionConfig_WithPortMappings_ShouldIncludePortMappings()
    {
        // Arrange
        var portMappings = new List<PortMapping>
        {
            new PortMapping { ContainerPort = 80, Protocol = "tcp" },
            new PortMapping { ContainerPort = 443, Protocol = "tcp" }
        };

        // Act
        var containerDefinitionConfig = new ContainerDefinitionConfig
        {
            Name = "test-container",
            Image = "nginx:latest",
            Essential = true,
            PortMappings = portMappings
        };

        // Assert
        containerDefinitionConfig.PortMappings.ShouldNotBeNull();
        containerDefinitionConfig.PortMappings.Count.ShouldBe(2);
        containerDefinitionConfig.PortMappings[0].ContainerPort.ShouldBe(80);
        containerDefinitionConfig.PortMappings[1].ContainerPort.ShouldBe(443);
    }

    [Fact]
    public void ContainerDefinitionConfig_WithEnvironmentVariables_ShouldIncludeEnvironmentVariables()
    {
        // Arrange
        var environmentVariables = new List<EnvironmentVariable>
        {
            new EnvironmentVariable { Name = "API_URL", Value = "http://api.example.com" },
            new EnvironmentVariable { Name = "DEBUG", Value = "true" }
        };

        // Act
        var containerDefinitionConfig = new ContainerDefinitionConfig
        {
            Name = "test-container",
            Image = "nginx:latest",
            Essential = true,
            Environment = environmentVariables
        };

        // Assert
        containerDefinitionConfig.Environment.ShouldNotBeNull();
        containerDefinitionConfig.Environment.Count.ShouldBe(2);
        containerDefinitionConfig.Environment[0].Name.ShouldBe("API_URL");
        containerDefinitionConfig.Environment[1].Name.ShouldBe("DEBUG");
    }

    [Fact]
    public void ContainerDefinitionConfig_WithSecrets_ShouldIncludeSecrets()
    {
        // Arrange
        var secrets = new List<string> { "API_KEY", "DB_PASSWORD", "JWT_SECRET" };

        // Act
        var containerDefinitionConfig = new ContainerDefinitionConfig
        {
            Name = "test-container",
            Image = "nginx:latest",
            Essential = true,
            Secrets = secrets
        };

        // Assert
        containerDefinitionConfig.Secrets.ShouldNotBeNull();
        containerDefinitionConfig.Secrets.Count.ShouldBe(3);
        containerDefinitionConfig.Secrets.ShouldContain("API_KEY");
        containerDefinitionConfig.Secrets.ShouldContain("DB_PASSWORD");
        containerDefinitionConfig.Secrets.ShouldContain("JWT_SECRET");
    }

    [Fact]
    public void EcsTaskConfiguration_WithMultipleServices_ShouldHandleMultipleServices()
    {
        // Arrange & Act
        var config = new EcsTaskConfiguration
        {
            Services = new List<ServiceConfig>
            {
                new ServiceConfig
                {
                    ServiceName = "service1",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TaskDefinition1",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>()
                        }
                    }
                },
                new ServiceConfig
                {
                    ServiceName = "service2",
                    TaskDefinition = new List<TaskDefinitionConfig>
                    {
                        new TaskDefinitionConfig
                        {
                            TaskDefinitionName = "TaskDefinition2",
                            ContainerDefinitions = new List<ContainerDefinitionConfig>()
                        }
                    }
                }
            }
        };

        // Assert
        config.Services.Count.ShouldBe(2);
        config.Services[0].ServiceName.ShouldBe("service1");
        config.Services[1].ServiceName.ShouldBe("service2");
    }

    [Fact]
    public void ServiceConfig_WithMultipleTaskDefinitions_ShouldHandleMultipleTaskDefinitions()
    {
        // Arrange & Act
        var serviceConfig = new ServiceConfig
        {
            ServiceName = "test-service",
            TaskDefinition = new List<TaskDefinitionConfig>
            {
                new TaskDefinitionConfig
                {
                    TaskDefinitionName = "TaskDefinition1",
                    ContainerDefinitions = new List<ContainerDefinitionConfig>()
                },
                new TaskDefinitionConfig
                {
                    TaskDefinitionName = "TaskDefinition2",
                    ContainerDefinitions = new List<ContainerDefinitionConfig>()
                }
            }
        };

        // Assert
        serviceConfig.TaskDefinition.Count.ShouldBe(2);
        serviceConfig.TaskDefinition[0].TaskDefinitionName.ShouldBe("TaskDefinition1");
        serviceConfig.TaskDefinition[1].TaskDefinitionName.ShouldBe("TaskDefinition2");
    }

    [Fact]
    public void TaskDefinitionConfig_WithMultipleContainers_ShouldHandleMultipleContainers()
    {
        // Arrange & Act
        var taskDefinitionConfig = new TaskDefinitionConfig
        {
            TaskDefinitionName = "TestTaskDefinition",
            ContainerDefinitions = new List<ContainerDefinitionConfig>
            {
                new ContainerDefinitionConfig
                {
                    Name = "container1",
                    Image = "nginx:latest",
                    Essential = true
                },
                new ContainerDefinitionConfig
                {
                    Name = "container2",
                    Image = "redis:latest",
                    Essential = false
                }
            }
        };

        // Assert
        taskDefinitionConfig.ContainerDefinitions.Count.ShouldBe(2);
        taskDefinitionConfig.ContainerDefinitions[0].Name.ShouldBe("container1");
        taskDefinitionConfig.ContainerDefinitions[1].Name.ShouldBe("container2");
    }

    [Fact]
    public void EcsTaskConfiguration_WithValidData_ShouldValidateCorrectly()
    {
        // Arrange
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
                            ContainerDefinitions = new List<ContainerDefinitionConfig>
                            {
                                new ContainerDefinitionConfig
                                {
                                    Name = "test-container",
                                    Image = "test-image:latest",
                                    PortMappings = new List<PortMapping>
                                    {
                                        new PortMapping { ContainerPort = 80, HostPort = 80 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act & Assert
        config.Services.ShouldNotBeNull();
        config.Services.Count.ShouldBe(1);
        config.Services[0].ServiceName.ShouldBe("test-service");
        config.Services[0].TaskDefinition.ShouldNotBeNull();
        config.Services[0].TaskDefinition.Count.ShouldBe(1);
        config.Services[0].TaskDefinition[0].ContainerDefinitions.ShouldNotBeNull();
        config.Services[0].TaskDefinition[0].ContainerDefinitions.Count.ShouldBe(1);
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].Name.ShouldBe("test-container");
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].Image.ShouldBe("test-image:latest");
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].PortMappings.ShouldNotBeNull();
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].PortMappings.Count.ShouldBe(1);
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].PortMappings[0].ContainerPort.ShouldBe(80);
        config.Services[0].TaskDefinition[0].ContainerDefinitions[0].PortMappings[0].HostPort.ShouldBe(80);
        // Note: EnvironmentVariables property doesn't exist in ContainerDefinitionConfig
        // Note: HealthCheck property doesn't exist in ContainerDefinitionConfig
    }
}
