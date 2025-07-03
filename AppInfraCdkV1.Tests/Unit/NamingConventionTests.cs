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
    [InlineData("Integration", "int")]
    [InlineData("Staging", "stg")]
    [InlineData("Production", "prod")]
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

    #region Enum-based Method Tests

    [Theory]
    [InlineData(EnvironmentType.Development, "dev")]
    [InlineData(EnvironmentType.QA, "qa")]
    [InlineData(EnvironmentType.Production, "prod")]
    public void GetEnvironmentPrefix_WithValidEnum_ReturnsCorrectPrefix(EnvironmentType environment, string expectedPrefix)
    {
        // Act
        var result = NamingConvention.GetEnvironmentPrefix(environment);

        // Assert
        result.Should().Be(expectedPrefix);
    }

    [Theory]
    [InlineData(ApplicationType.TrialFinderV2, "tfv2")]
    public void GetApplicationCode_WithValidEnum_ReturnsCorrectCode(ApplicationType application, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetApplicationCode(application);

        // Assert
        result.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(AwsRegion.UsEast1, "ue1")]
    [InlineData(AwsRegion.UsWest2, "uw2")]
    [InlineData(AwsRegion.EuWest1, "ew1")]
    public void GetRegionCode_WithValidEnum_ReturnsCorrectCode(AwsRegion region, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetRegionCode(region);

        // Assert
        result.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(EnvironmentType.Development, AccountType.NonProduction)]
    [InlineData(EnvironmentType.Production, AccountType.Production)]
    public void GetAccountType_WithValidEnum_ReturnsCorrectType(EnvironmentType environment, AccountType expectedType)
    {
        // Act
        var result = NamingConvention.GetAccountType(environment);

        // Assert
        result.Should().Be(expectedType);
    }

    #endregion

    #region Registration Tests

    [Fact]
    public void RegisterEnvironment_WithDuplicateEnvironment_ThrowsInvalidOperationException()
    {
        // Arrange - Use existing environment
        var existingEnv = EnvironmentType.Development;
        
        // Act & Assert - Should throw because Development already exists
        var action = () => NamingConvention.RegisterEnvironment(existingEnv, "test", AccountType.NonProduction);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void RegisterApplication_WithDuplicateApplication_ThrowsInvalidOperationException()
    {
        // Arrange - Use existing application
        var existingApp = ApplicationType.TrialFinderV2;
        
        // Act & Assert - Should throw because TrialFinderV2 already exists
        var action = () => NamingConvention.RegisterApplication(existingApp, "duplicate");
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    #endregion

    #region Environment Grouping Tests

    [Fact]
    public void GetEnvironmentsInSameAccount_WithDevelopment_ReturnsNonProductionEnvironments()
    {
        // Act
        var result = NamingConvention.GetEnvironmentsInSameAccount("Development");

        // Assert
        result.Should().Contain(new[] { "Development", "QA", "Integration" });
        result.Should().NotContain(new[] { "Staging", "Production" });
    }

    [Fact]
    public void GetEnvironmentsInSameAccount_WithProduction_ReturnsProductionEnvironments()
    {
        // Act
        var result = NamingConvention.GetEnvironmentsInSameAccount("Production");

        // Assert
        result.Should().Contain(new[] { "Staging", "Production" });
        result.Should().NotContain(new[] { "Development", "QA", "Integration" });
    }

    #endregion

    #region Security Group Naming Tests

    [Fact]
    public void GenerateSecurityGroupName_WithValidContext_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Development", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateSecurityGroupName(context, "alb", "web");

        // Assert
        result.Should().Be("dev-tfv2-sg-alb-web-ue1");
    }

    #endregion

    #region Log Group Naming Tests

    [Fact]
    public void GenerateLogGroupName_WithValidContext_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Production", "TrialFinderV2", "us-west-2");

        // Act
        var result = NamingConvention.GenerateLogGroupName(context, "ecs", "web-app");

        // Assert
        result.Should().Be("/aws/ecs/prod-tfv2-web-app");
    }

    #endregion

    #region VPC Naming Tests

    [Fact]
    public void GenerateVpcName_WithDefaultPurpose_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("QA", "TrialFinderV2", "eu-west-1");

        // Act
        var result = NamingConvention.GenerateVpcName(context);

        // Assert
        result.Should().Be("qa-tfv2-vpc-ew1-main");
    }

    [Fact]
    public void GenerateVpcName_WithCustomPurpose_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Integration", "TrialFinderV2", "us-east-2");

        // Act
        var result = NamingConvention.GenerateVpcName(context, "isolated");

        // Assert
        result.Should().Be("int-tfv2-vpc-ue2-isolated");
    }

    #endregion

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