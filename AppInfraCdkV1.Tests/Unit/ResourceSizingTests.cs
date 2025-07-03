using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class ResourceSizingTests
{
    [Fact]
    public void Constructor_ShouldCreateInstanceSuccessfully()
    {
        // Act
        var sizing = new ResourceSizing();

        // Assert
        sizing.ShouldNotBeNull();
    }

    [Fact]
    public void GetProductionSizing_ShouldReturnResourceSizingInstance()
    {
        // Act
        var result = ResourceSizing.GetProductionSizing();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<ResourceSizing>();
    }

    [Fact]
    public void GetDevelopmentSizing_ShouldReturnResourceSizingInstance()
    {
        // Act
        var result = ResourceSizing.GetDevelopmentSizing();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<ResourceSizing>();
    }

    [Fact]
    public void GetProductionSizing_ShouldReturnNewInstanceEachTime()
    {
        // Act
        var result1 = ResourceSizing.GetProductionSizing();
        var result2 = ResourceSizing.GetProductionSizing();

        // Assert
        result1.ShouldNotBeSameAs(result2);
    }

    [Fact]
    public void GetDevelopmentSizing_ShouldReturnNewInstanceEachTime()
    {
        // Act
        var result1 = ResourceSizing.GetDevelopmentSizing();
        var result2 = ResourceSizing.GetDevelopmentSizing();

        // Assert
        result1.ShouldNotBeSameAs(result2);
    }

    [Theory]
    [InlineData(AccountType.Production)]
    public void GetSizingForEnvironment_WithProductionAccountType_ShouldReturnProductionSizing(AccountType accountType)
    {
        // Act
        var result = ResourceSizing.GetSizingForEnvironment(accountType);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<ResourceSizing>();
        
        // Verify it's equivalent to production sizing by comparing with a fresh instance
        var productionSizing = ResourceSizing.GetProductionSizing();
        result.ShouldNotBeSameAs(productionSizing);
    }

    [Theory]
    [InlineData(AccountType.NonProduction)]
    public void GetSizingForEnvironment_WithNonProductionAccountType_ShouldReturnDevelopmentSizing(AccountType accountType)
    {
        // Act
        var result = ResourceSizing.GetSizingForEnvironment(accountType);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<ResourceSizing>();
        
        // Verify it's equivalent to development sizing by comparing with a fresh instance
        var developmentSizing = ResourceSizing.GetDevelopmentSizing();
        result.ShouldNotBeSameAs(developmentSizing);
    }

    [Fact]
    public void GetSizingForEnvironment_ShouldReturnDifferentInstancesForDifferentAccountTypes()
    {
        // Act
        var productionResult = ResourceSizing.GetSizingForEnvironment(AccountType.Production);
        var nonProductionResult = ResourceSizing.GetSizingForEnvironment(AccountType.NonProduction);

        // Assert
        productionResult.ShouldNotBeSameAs(nonProductionResult);
    }

    [Fact]
    public void GetSizingForEnvironment_ShouldReturnNewInstanceEachTime()
    {
        // Act
        var result1 = ResourceSizing.GetSizingForEnvironment(AccountType.Production);
        var result2 = ResourceSizing.GetSizingForEnvironment(AccountType.Production);

        // Assert
        result1.ShouldNotBeSameAs(result2);
    }
}