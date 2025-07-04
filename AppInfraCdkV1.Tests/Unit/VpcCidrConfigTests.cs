using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class VpcCidrConfigTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithEmptyValues()
    {
        // Act
        var config = new VpcCidrConfig();

        // Assert
        config.PrimaryCidr.ShouldBe(string.Empty);
        config.SecondaryCidrs.ShouldNotBeNull();
        config.SecondaryCidrs.ShouldBeEmpty();
    }

    [Fact]
    public void PrimaryCidr_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new VpcCidrConfig();
        const string expectedCidr = "10.0.0.0/16";

        // Act
        config.PrimaryCidr = expectedCidr;

        // Assert
        config.PrimaryCidr.ShouldBe(expectedCidr);
    }

    [Fact]
    public void SecondaryCidrs_ShouldAllowSetAndGet()
    {
        // Arrange
        var config = new VpcCidrConfig();
        var expectedCidrs = new List<string> { "10.1.0.0/16", "10.2.0.0/16" };

        // Act
        config.SecondaryCidrs = expectedCidrs;

        // Assert
        config.SecondaryCidrs.ShouldBe(expectedCidrs);
        config.SecondaryCidrs.Count.ShouldBe(2);
        config.SecondaryCidrs.ShouldContain("10.1.0.0/16");
        config.SecondaryCidrs.ShouldContain("10.2.0.0/16");
    }

    [Theory]
    [InlineData("Development", "10.0.0.0/16")]
    [InlineData("Staging", "10.10.0.0/16")]
    [InlineData("Production", "10.20.0.0/16")]
    public void GetDefaultForEnvironment_WithKnownEnvironment_ShouldReturnCorrectCidr(string environment, string expectedCidr)
    {
        // Act
        var result = VpcCidrConfig.GetDefaultForEnvironment(environment);

        // Assert
        result.ShouldNotBeNull();
        result.PrimaryCidr.ShouldBe(expectedCidr);
        result.SecondaryCidrs.ShouldNotBeNull();
        result.SecondaryCidrs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("")]
    public void GetDefaultForEnvironment_WithUnknownEnvironment_ShouldReturnFallbackCidr(string environment)
    {
        // Act
        var result = VpcCidrConfig.GetDefaultForEnvironment(environment);

        // Assert
        result.ShouldNotBeNull();
        result.PrimaryCidr.ShouldBe("10.99.0.0/16");
        result.SecondaryCidrs.ShouldNotBeNull();
        result.SecondaryCidrs.ShouldBeEmpty();
    }

    [Fact]
    public void GetDefaultForEnvironment_ShouldReturnNewInstanceEachTime()
    {
        // Act
        var result1 = VpcCidrConfig.GetDefaultForEnvironment("Development");
        var result2 = VpcCidrConfig.GetDefaultForEnvironment("Development");

        // Assert
        result1.ShouldNotBeSameAs(result2);
        result1.PrimaryCidr.ShouldBe(result2.PrimaryCidr);
    }

    [Fact]
    public void SecondaryCidrs_ShouldAllowModification()
    {
        // Arrange
        var config = new VpcCidrConfig();

        // Act
        config.SecondaryCidrs.Add("10.1.0.0/16");
        config.SecondaryCidrs.Add("10.2.0.0/16");

        // Assert
        config.SecondaryCidrs.Count.ShouldBe(2);
        config.SecondaryCidrs[0].ShouldBe("10.1.0.0/16");
        config.SecondaryCidrs[1].ShouldBe("10.2.0.0/16");
    }

    [Fact]
    public void VpcCidrConfig_ShouldHandleNullSecondaryCidrsAssignment()
    {
        // Arrange
        var config = new VpcCidrConfig();

        // Act
        config.SecondaryCidrs = null!;

        // Assert
        config.SecondaryCidrs.ShouldBeNull();
    }

    [Theory]
    [InlineData("10.0.0.0/16")]
    [InlineData("192.168.1.0/24")]
    [InlineData("172.16.0.0/12")]
    public void PrimaryCidr_ShouldAcceptValidCidrFormats(string cidr)
    {
        // Arrange
        var config = new VpcCidrConfig();

        // Act
        config.PrimaryCidr = cidr;

        // Assert
        config.PrimaryCidr.ShouldBe(cidr);
    }
}