using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class SecurityConfigTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Act
        var config = new SecurityConfig();

        // Assert
        config.AllowedCidrBlocks.ShouldNotBeNull();
        config.AllowedCidrBlocks.ShouldBeEmpty();
        config.EnableWaf.ShouldBeTrue();
        config.CertificateArn.ShouldBe(string.Empty);
    }

    [Fact]
    public void AllowedCidrBlocks_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new SecurityConfig();
        var expectedCidrs = new List<string> { "10.0.0.0/16", "192.168.1.0/24" };

        // Act
        config.AllowedCidrBlocks = expectedCidrs;

        // Assert
        config.AllowedCidrBlocks.ShouldBe(expectedCidrs);
        config.AllowedCidrBlocks.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnableWaf_ShouldAllowSetAndGet(bool enableWaf)
    {
        // Arrange
        var config = new SecurityConfig();

        // Act
        config.EnableWaf = enableWaf;

        // Assert
        config.EnableWaf.ShouldBe(enableWaf);
    }

    [Fact]
    public void CertificateArn_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new SecurityConfig();
        const string expectedArn = "arn:aws:acm:us-east-1:123456789012:certificate/12345678-1234-1234-1234-123456789012";

        // Act
        config.CertificateArn = expectedArn;

        // Assert
        config.CertificateArn.ShouldBe(expectedArn);
    }


    [Fact]
    public void GetSecurityConfigForAccountType_WithProductionAccountType_ShouldReturnProductionConfig()
    {
        // Act
        var result = SecurityConfig.GetSecurityConfigForAccountType(AccountType.Production);

        // Assert
        result.ShouldNotBeNull();
        result.EnableWaf.ShouldBeTrue();
        result.AllowedCidrBlocks.ShouldNotBeNull();
        result.AllowedCidrBlocks.ShouldBeEmpty();
    }

    [Fact]
    public void GetSecurityConfigForAccountType_WithNonProductionAccountType_ShouldReturnDevelopmentConfig()
    {
        // Act
        var result = SecurityConfig.GetSecurityConfigForAccountType(AccountType.NonProduction);

        // Assert
        result.ShouldNotBeNull();
        result.EnableWaf.ShouldBeFalse();
        result.AllowedCidrBlocks.ShouldNotBeNull();
        result.AllowedCidrBlocks.ShouldNotBeEmpty();
        result.AllowedCidrBlocks.ShouldContain("10.0.0.0/8");
    }

    [Fact]
    public void GetSecurityConfigForAccountType_ShouldReturnNewInstanceEachTime()
    {
        // Act
        var result1 = SecurityConfig.GetSecurityConfigForAccountType(AccountType.Production);
        var result2 = SecurityConfig.GetSecurityConfigForAccountType(AccountType.Production);

        // Assert
        result1.ShouldNotBeSameAs(result2);
        result1.EnableWaf.ShouldBe(result2.EnableWaf);
    }

    [Fact]
    public void GetSecurityConfigForAccountType_ProductionVsDevelopment_ShouldHaveDifferentSettings()
    {
        // Act
        var productionConfig = SecurityConfig.GetSecurityConfigForAccountType(AccountType.Production);
        var developmentConfig = SecurityConfig.GetSecurityConfigForAccountType(AccountType.NonProduction);

        // Assert
        productionConfig.EnableWaf.ShouldNotBe(developmentConfig.EnableWaf);
        productionConfig.AllowedCidrBlocks.Count.ShouldNotBe(developmentConfig.AllowedCidrBlocks.Count);
    }

    [Fact]
    public void AllowedCidrBlocks_ShouldAllowModification()
    {
        // Arrange
        var config = new SecurityConfig();

        // Act
        config.AllowedCidrBlocks.Add("10.0.0.0/16");
        config.AllowedCidrBlocks.Add("192.168.1.0/24");

        // Assert
        config.AllowedCidrBlocks.Count.ShouldBe(2);
        config.AllowedCidrBlocks[0].ShouldBe("10.0.0.0/16");
        config.AllowedCidrBlocks[1].ShouldBe("192.168.1.0/24");
    }

    [Fact]
    public void SecurityConfig_ShouldHandleNullAllowedCidrBlocksAssignment()
    {
        // Arrange
        var config = new SecurityConfig();

        // Act
        config.AllowedCidrBlocks = null!;

        // Assert
        config.AllowedCidrBlocks.ShouldBeNull();
    }

}