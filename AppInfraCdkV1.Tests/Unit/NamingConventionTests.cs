using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
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
    public void GetEnvironmentPrefixWithValidEnvironmentReturnsCorrectPrefix(string environment,
        string expectedPrefix)
    {
        // Act
        var result = NamingConvention.GetEnvironmentPrefix(environment);

        // Assert
        result.ShouldBe(expectedPrefix);
    }

    [Fact]
    public void GetEnvironmentPrefixWithInvalidEnvironmentThrowsArgumentException()
    {
        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => NamingConvention.GetEnvironmentPrefix("InvalidEnvironment"));
        ex.Message.ShouldStartWith("Unknown environment: InvalidEnvironment");
    }

    [Theory]
    [InlineData("TrialFinderV2", "tfv2")]
    public void GetApplicationCodeWithValidApplicationReturnsCorrectCode(string application,
        string expectedCode)
    {
        // Act
        var result = NamingConvention.GetApplicationCode(application);

        // Assert
        result.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("us-east-1", "ue1")]
    [InlineData("us-west-2", "uw2")]
    [InlineData("eu-west-1", "ew1")]
    public void GetRegionCodeWithValidRegionReturnsCorrectCode(string region, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetRegionCode(region);

        // Assert
        result.ShouldBe(expectedCode);
    }

    [Fact]
    public void GenerateResourceNameWithDevelopmentEnvironmentReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Development", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateResourceName(context, "ecs", "main");

        // Assert
        result.ShouldBe("dev-tfv2-ecs-ue1-main");
    }

    [Fact]
    public void GenerateResourceNameWithProductionEnvironmentReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Production", "TrialFinderV2", "us-west-2");

        // Act
        var result = NamingConvention.GenerateResourceName(context, "ecs", "main");

        // Assert
        result.ShouldBe("prod-tfv2-ecs-uw2-main");
    }

    [Fact]
    public void GenerateS3BucketNameWithValidContextReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("QA", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateS3BucketName(context, "app");

        // Assert
        result.ShouldBe("thirdopinion.io-qa-tfv2-app-ue1");
    }

    #region Enum-based Method Tests

    [Theory]
    [InlineData(EnvironmentType.Development, "dev")]
    [InlineData(EnvironmentType.QA, "qa")]
    [InlineData(EnvironmentType.Production, "prod")]
    public void GetEnvironmentPrefixWithValidEnumReturnsCorrectPrefix(EnvironmentType environment, string expectedPrefix)
    {
        // Act
        var result = NamingConvention.GetEnvironmentPrefix(environment);

        // Assert
        result.ShouldBe(expectedPrefix);
    }

    [Theory]
    [InlineData(ApplicationType.TrialFinderV2, "tfv2")]
    public void GetApplicationCodeWithValidEnumReturnsCorrectCode(ApplicationType application, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetApplicationCode(application);

        // Assert
        result.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(AwsRegion.UsEast1, "ue1")]
    [InlineData(AwsRegion.UsWest2, "uw2")]
    [InlineData(AwsRegion.EuWest1, "ew1")]
    public void GetRegionCodeWithValidEnumReturnsCorrectCode(AwsRegion region, string expectedCode)
    {
        // Act
        var result = NamingConvention.GetRegionCode(region);

        // Assert
        result.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(EnvironmentType.Development, AccountType.NonProduction)]
    [InlineData(EnvironmentType.Production, AccountType.Production)]
    public void GetAccountTypeWithValidEnumReturnsCorrectType(EnvironmentType environment, AccountType expectedType)
    {
        // Act
        var result = NamingConvention.GetAccountType(environment);

        // Assert
        result.ShouldBe(expectedType);
    }

    #endregion

    #region Registration Tests

    [Fact]
    public void RegisterEnvironmentWithDuplicateEnvironmentThrowsInvalidOperationException()
    {
        // Arrange - Use existing environment
        var existingEnv = EnvironmentType.Development;
        
        // Act & Assert - Should throw because Development already exists
        var ex = Should.Throw<InvalidOperationException>(() => NamingConvention.RegisterEnvironment(existingEnv, "test", AccountType.NonProduction));
        ex.Message.ShouldContain("already registered");
    }

    [Fact]
    public void RegisterApplicationWithDuplicateApplicationThrowsInvalidOperationException()
    {
        // Arrange - Use existing application
        var existingApp = ApplicationType.TrialFinderV2;
        
        // Act & Assert - Should throw because TrialFinderV2 already exists
        var ex = Should.Throw<InvalidOperationException>(() => NamingConvention.RegisterApplication(existingApp, "duplicate"));
        ex.Message.ShouldContain("already registered");
    }

    #endregion

    #region Environment Grouping Tests

    [Fact]
    public void GetEnvironmentsInSameAccountWithDevelopmentReturnsNonProductionEnvironments()
    {
        // Act
        var result = NamingConvention.GetEnvironmentsInSameAccount("Development");

        // Assert
        result.ShouldContain("Development");
        result.ShouldContain("QA");
        result.ShouldContain("Integration");
        result.ShouldNotContain("Staging");
        result.ShouldNotContain("Production");
    }

    [Fact]
    public void GetEnvironmentsInSameAccountWithProductionReturnsProductionEnvironments()
    {
        // Act
        var result = NamingConvention.GetEnvironmentsInSameAccount("Production");

        // Assert
        result.ShouldContain("Staging");
        result.ShouldContain("Production");
        result.ShouldNotContain("Development");
        result.ShouldNotContain("QA");
        result.ShouldNotContain("Integration");
    }

    #endregion

    #region Security Group Naming Tests

    [Fact]
    public void GenerateSecurityGroupNameWithValidContextReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Development", "TrialFinderV2", "us-east-1");

        // Act
        var result = NamingConvention.GenerateSecurityGroupName(context, "alb", "web");

        // Assert
        result.ShouldBe("dev-tfv2-sg-alb-web-ue1");
    }

    #endregion

    #region Log Group Naming Tests

    [Fact]
    public void GenerateLogGroupNameWithValidContextReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Production", "TrialFinderV2", "us-west-2");

        // Act
        var result = NamingConvention.GenerateLogGroupName(context, "ecs", "web-app");

        // Assert
        result.ShouldBe("/aws/ecs/prod-tfv2-web-app");
    }

    #endregion

    #region VPC Naming Tests

    [Fact]
    public void GenerateVpcNameWithDefaultPurposeReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("QA", "TrialFinderV2", "eu-west-1");

        // Act
        var result = NamingConvention.GenerateVpcName(context);

        // Assert
        result.ShouldBe("qa-tfv2-vpc-ew1-main");
    }

    [Fact]
    public void GenerateVpcNameWithCustomPurposeReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateTestContext("Integration", "TrialFinderV2", "us-east-2");

        // Act
        var result = NamingConvention.GenerateVpcName(context, "isolated");

        // Assert
        result.ShouldBe("int-tfv2-vpc-ue2-isolated");
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