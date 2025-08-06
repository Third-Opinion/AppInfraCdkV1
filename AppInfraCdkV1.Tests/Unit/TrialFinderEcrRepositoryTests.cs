using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class TrialFinderEcrRepositoryTests
{
    [Fact]
    public void ContainerRepositoryConfig_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
            ""name"": ""trial-finder-v2"",
            ""image"": ""placeholder"",
            ""repository"": {
                ""type"": ""webapp"",
                ""description"": ""Main TrialFinder web application"",
                ""imageScanOnPush"": true,
                ""removalPolicy"": ""RETAIN""
            }
        }";

        // Act
        var container = System.Text.Json.JsonSerializer.Deserialize<ContainerDefinitionConfig>(
            json, 
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        // Assert
        container.ShouldNotBeNull();
        container.Name.ShouldBe("trial-finder-v2");
        container.Image.ShouldBe("placeholder");
        container.Repository.ShouldNotBeNull();
        container.Repository.Type.ShouldBe("webapp");
        container.Repository.Description.ShouldBe("Main TrialFinder web application");
        container.Repository.ImageScanOnPush.ShouldBe(true);
        container.Repository.RemovalPolicy.ShouldBe("RETAIN");
    }

    [Fact]
    public void ConfigurationLoader_ShouldLoadRepositoryConfiguration()
    {
        // Arrange
        var configLoader = new ConfigurationLoader();

        // Act & Assert
        Should.NotThrow(() => configLoader.LoadFullConfig("development"));
    }

    [Fact]
    public void ResourceNamer_ShouldGenerateCorrectEcrRepositoryNames()
    {
        // Arrange
        var context = CreateTestDeploymentContext();
        var namer = new AppInfraCdkV1.Core.Naming.ResourceNamer(context);

        // Act
        var webappRepositoryName = namer.EcrRepository("webapp");
        var loaderRepositoryName = namer.EcrRepository("loader");

        // Assert
        webappRepositoryName.ShouldBe("thirdopinion/trialfinderv2/webapp");
        loaderRepositoryName.ShouldBe("thirdopinion/trialfinderv2/loader");
    }

    private static DeploymentContext CreateTestDeploymentContext()
    {
        return new DeploymentContext
        {
            Environment = new EnvironmentConfig
            {
                Name = "development",
                AccountId = "123456789012",
                Region = "us-east-2",
                AccountType = AccountType.NonProduction
            },
            Application = new ApplicationConfig
            {
                Name = "TrialFinderV2",
                Version = "1.0.0"
            }
        };
    }
} 