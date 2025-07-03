using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using FluentAssertions;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class NamingConventionTests
{
    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("QA", "qa")]
    [InlineData("Test", "test")]
    [InlineData("Integration", "int")]
    [InlineData("Staging", "stg")]
    [InlineData("Production", "prod")]
    [InlineData("PreProduction", "preprod")]
    [InlineData("UAT", "uat")]
    public void GetEnvironmentPrefix_WithValidEnvironment_ReturnsCorrectPrefix(string environment,
        string expectedPrefix)
    {
        // Act
        var result = NamingConvention.GetEnvironmentPrefix(environment);

        // Assert
        result.Should().Be(expectedPrefix);
    }

    [Fact]
    public void GetEnvironmentPrefix_WithInvalidEnvironment_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => NamingConvention.GetEnvironmentPrefix("InvalidEnvironment");
        action.Should().Throw<ArgumentException>()
            .WithMessage("Unknown environment: InvalidEnvironment*");
    }

    [Theory]
    [InlineData("TrialFinderV2", "tfv2")]
    [InlineData("PatientPortal", "pp")]
    [InlineData("AdminDashboard", "ad")]
    public void GetApplicationCode_WithValidApplication_ReturnsCorrectCode(string application,
        string expectedCode)
    {
        // Act
        var result = NamingConvention.GetApplicationCode(application);

        // Assert
        result.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("us-east-1", "ue1")]
    [InlineData("us-west-2", "uw2")]
    [InlineData("eu-west-1", "ew1")]
    public void GetRegionCode_WithValidRegion_ReturnsCorrectCode(string region, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetRegionCode(region);

        // Assert
        result.Should().Be(expectedCode);
    }

    [Fact]
    public void GenerateResourceName_WithDevelopmentEnvironment_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Development", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateResourceName(context, "ecs", "main");

        // Assert
        result.Should().Be("dev-tfv2-ecs-ue1-main");
    }

    [Fact]
    public void GenerateResourceName_WithProductionEnvironment_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Production", "TrialFinderV2", "us-west-2");

        // Act
        var result = NamingConvention.GenerateResourceName(context, "ecs", "main");

        // Assert
        result.Should().Be("prod-tfv2-ecs-uw2-main");
    }

    [Fact]
    public void GenerateS3BucketName_WithValidContext_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("QA", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateS3BucketName(context, "app");

        // Assert
        result.Should().Be("thirdopinion.io-qa-tfv2-app-ue1");
    }

    private static DeploymentContext CreateTestContext(string environment,
        string application,
        string region)
    {
        return new DeploymentContext
        {
            Environment = new EnvironmentConfig
            {
                Name = environment,
                Region = region,
                AccountId = "123456789012",
                AccountType = NamingConvention.GetAccountType(environment)
            },
            Application = new ApplicationConfig
            {
                Name = application,
                Version = "1.0.0"
            }
        };
    }
}