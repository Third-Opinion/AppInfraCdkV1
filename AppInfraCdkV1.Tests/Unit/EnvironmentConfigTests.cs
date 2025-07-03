using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class EnvironmentConfigTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Act
        var config = new EnvironmentConfig();

        // Assert
        config.Name.ShouldBe(string.Empty);
        config.AccountId.ShouldBe(string.Empty);
        config.Region.ShouldBe(string.Empty);
        config.AccountType.ShouldBe(AccountType.NonProduction);
        config.Tags.ShouldNotBeNull();
        config.Tags.ShouldBeEmpty();
        config.IsolationStrategy.ShouldNotBeNull();
    }

    [Fact]
    public void Name_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new EnvironmentConfig();
        const string expectedName = "Development";

        // Act
        config.Name = expectedName;

        // Assert
        config.Name.ShouldBe(expectedName);
    }

    [Fact]
    public void AccountId_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new EnvironmentConfig();
        const string expectedAccountId = "123456789012";

        // Act
        config.AccountId = expectedAccountId;

        // Assert
        config.AccountId.ShouldBe(expectedAccountId);
    }

    [Fact]
    public void Region_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new EnvironmentConfig();
        const string expectedRegion = "us-east-1";

        // Act
        config.Region = expectedRegion;

        // Assert
        config.Region.ShouldBe(expectedRegion);
    }

    [Theory]
    [InlineData(AccountType.Production)]
    [InlineData(AccountType.NonProduction)]
    public void AccountType_ShouldAllowSetAndGet(AccountType expectedAccountType)
    {
        // Arrange
        var config = new EnvironmentConfig();

        // Act
        config.AccountType = expectedAccountType;

        // Assert
        config.AccountType.ShouldBe(expectedAccountType);
    }

    [Fact]
    public void Tags_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new EnvironmentConfig();
        var expectedTags = new Dictionary<string, string>
        {
            ["Environment"] = "Development",
            ["Team"] = "Platform"
        };

        // Act
        config.Tags = expectedTags;

        // Assert
        config.Tags.ShouldBe(expectedTags);
        config.Tags["Environment"].ShouldBe("Development");
        config.Tags["Team"].ShouldBe("Platform");
    }

    [Fact]
    public void IsolationStrategy_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new EnvironmentConfig();
        var expectedStrategy = new EnvironmentIsolationStrategy
        {
            UseVpcPerEnvironment = true,
            VpcCidr = new VpcCidrConfig { PrimaryCidr = "10.0.0.0/16" }
        };

        // Act
        config.IsolationStrategy = expectedStrategy;

        // Assert
        config.IsolationStrategy.ShouldBe(expectedStrategy);
        config.IsolationStrategy.UseVpcPerEnvironment.ShouldBeTrue();
    }

    [Fact]
    public void IsProductionClass_WithProductionAccountType_ShouldReturnTrue()
    {
        // Arrange
        var config = new EnvironmentConfig
        {
            AccountType = AccountType.Production
        };

        // Act & Assert
        config.IsProductionClass.ShouldBeTrue();
    }

    [Fact]
    public void IsProductionClass_WithNonProductionAccountType_ShouldReturnFalse()
    {
        // Arrange
        var config = new EnvironmentConfig
        {
            AccountType = AccountType.NonProduction
        };

        // Act & Assert
        config.IsProductionClass.ShouldBeFalse();
    }

    [Fact]
    public void IsProd_ShouldMatchIsProductionClass()
    {
        // Arrange
        var productionConfig = new EnvironmentConfig { AccountType = AccountType.Production };
        var nonProductionConfig = new EnvironmentConfig { AccountType = AccountType.NonProduction };

        // Act & Assert
        productionConfig.IsProd.ShouldBe(productionConfig.IsProductionClass);
        nonProductionConfig.IsProd.ShouldBe(nonProductionConfig.IsProductionClass);
        
        productionConfig.IsProd.ShouldBeTrue();
        nonProductionConfig.IsProd.ShouldBeFalse();
    }

    [Fact]
    public void ToAwsEnvironment_ShouldReturnCorrectCdkEnvironment()
    {
        // Arrange
        var config = new EnvironmentConfig
        {
            AccountId = "123456789012",
            Region = "us-east-1"
        };

        // Act
        var result = config.ToAwsEnvironment();

        // Assert
        result.ShouldNotBeNull();
        result.Account.ShouldBe("123456789012");
        result.Region.ShouldBe("us-east-1");
    }

    [Fact]
    public void ToAwsEnvironment_WithEmptyValues_ShouldReturnEnvironmentWithEmptyValues()
    {
        // Arrange
        var config = new EnvironmentConfig();

        // Act
        var result = config.ToAwsEnvironment();

        // Assert
        result.ShouldNotBeNull();
        result.Account.ShouldBe(string.Empty);
        result.Region.ShouldBe(string.Empty);
    }

    [Fact]
    public void GetAccountSiblingEnvironments_ShouldCallNamingConvention()
    {
        // Arrange
        var config = new EnvironmentConfig
        {
            Name = "Development"
        };

        // Act
        var result = config.GetAccountSiblingEnvironments();

        // Assert
        result.ShouldNotBeNull();
        // The actual behavior depends on NamingConvention implementation
        // We're just verifying the method doesn't throw and returns a list
    }

    [Fact]
    public void Tags_ShouldAllowModification()
    {
        // Arrange
        var config = new EnvironmentConfig();

        // Act
        config.Tags.Add("Environment", "Development");
        config.Tags.Add("Owner", "Platform Team");

        // Assert
        config.Tags.Count.ShouldBe(2);
        config.Tags["Environment"].ShouldBe("Development");
        config.Tags["Owner"].ShouldBe("Platform Team");
    }

    [Fact]
    public void EnvironmentConfig_ShouldHandleNullTagsAssignment()
    {
        // Arrange
        var config = new EnvironmentConfig();

        // Act
        config.Tags = null!;

        // Assert
        config.Tags.ShouldBeNull();
    }

    [Fact]
    public void EnvironmentConfig_ShouldHandleNullIsolationStrategyAssignment()
    {
        // Arrange
        var config = new EnvironmentConfig();

        // Act
        config.IsolationStrategy = null!;

        // Assert
        config.IsolationStrategy.ShouldBeNull();
    }

    [Theory]
    [InlineData("Development", "123456789012", "us-east-1", AccountType.NonProduction)]
    [InlineData("Production", "987654321098", "us-west-2", AccountType.Production)]
    [InlineData("Staging", "555666777888", "eu-west-1", AccountType.Production)]
    public void EnvironmentConfig_ShouldAcceptVariousValidValues(string name, string accountId, string region, AccountType accountType)
    {
        // Arrange & Act
        var config = new EnvironmentConfig
        {
            Name = name,
            AccountId = accountId,
            Region = region,
            AccountType = accountType
        };

        // Assert
        config.Name.ShouldBe(name);
        config.AccountId.ShouldBe(accountId);
        config.Region.ShouldBe(region);
        config.AccountType.ShouldBe(accountType);
        config.IsProductionClass.ShouldBe(accountType == AccountType.Production);
    }
}