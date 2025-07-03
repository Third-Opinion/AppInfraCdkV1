using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using FluentAssertions;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class DeploymentContextTests
{
    [Fact]
    public void GetCommonTags_WithValidContext_ReturnsAllRequiredTags()
    {
        // Arrange
        var context = CreateTestContext();
        var expectedDeploymentDate = context.DeployedAt.ToString("yyyy-MM-dd");

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.Should().ContainKey("Environment").WhoseValue.Should().Be("Development");
        tags.Should().ContainKey("Application").WhoseValue.Should().Be("TrialFinderV2");
        tags.Should().ContainKey("Version").WhoseValue.Should().Be("1.0.0");
        tags.Should().ContainKey("DeployedBy").WhoseValue.Should().Be("CDK");
        tags.Should().ContainKey("DeployedAt").WhoseValue.Should().Be(expectedDeploymentDate);
        tags.Should().ContainKey("DeploymentId").WhoseValue.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetCommonTags_IncludesEnvironmentTags()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags["CustomTag"] = "CustomValue";
        context.Environment.Tags["Team"] = "Infrastructure";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.Should().ContainKey("CustomTag").WhoseValue.Should().Be("CustomValue");
        tags.Should().ContainKey("Team").WhoseValue.Should().Be("Infrastructure");
    }

    [Fact]
    public void ValidateNamingContext_WithValidContext_DoesNotThrow()
    {
        // Arrange
        var context = CreateTestContext();

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateNamingContext_WithInvalidEnvironment_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Name = "InvalidEnvironment";

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContext_WithInvalidApplication_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Application.Name = "InvalidApplication";

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContext_WithInvalidRegion_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Region = "invalid-region";

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void Namer_PropertyInitialization_CreatesResourceNamerInstance()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var namer = context.Namer;

        // Assert
        namer.Should().NotBeNull();
        namer.Should().BeOfType<ResourceNamer>();
    }

    [Fact]
    public void Namer_PropertyAccess_ReturnsSameInstance()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var namer1 = context.Namer;
        var namer2 = context.Namer;

        // Assert
        namer1.Should().BeSameAs(namer2);
    }

    [Fact]
    public void DeploymentId_DefaultValue_IsValidFormat()
    {
        // Arrange & Act
        var context = new DeploymentContext();

        // Assert
        context.DeploymentId.Should().NotBeNullOrEmpty();
        context.DeploymentId.Should().HaveLength(8); // GUID first 8 characters
        context.DeploymentId.Should().MatchRegex("^[a-f0-9]{8}$"); // Hex digits only
    }

    [Fact]
    public void DeployedAt_DefaultValue_IsRecentUtcTime()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;
        
        // Act
        var context = new DeploymentContext();
        var afterCreation = DateTime.UtcNow;

        // Assert
        context.DeployedAt.Should().BeOnOrAfter(beforeCreation);
        context.DeployedAt.Should().BeOnOrBefore(afterCreation);
        context.DeployedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void DeployedBy_DefaultValue_IsCDK()
    {
        // Arrange & Act
        var context = new DeploymentContext();

        // Assert
        context.DeployedBy.Should().Be("CDK");
    }

    [Theory]
    [InlineData("Production", "TrialFinderV2", "us-east-1")]
    [InlineData("Development", "TrialFinderV2", "eu-west-1")]
    [InlineData("Staging", "TrialFinderV2", "us-west-2")]
    public void ValidateNamingContext_WithValidCombinations_DoesNotThrow(string environment, string application, string region)
    {
        // Arrange
        var context = CreateTestContext(environment, application, region);

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().NotThrow();
    }

    [Fact]
    public void Environment_PropertySetter_AssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var environment = new EnvironmentConfig { Name = "TestEnv", Region = "us-west-1" };

        // Act
        context.Environment = environment;

        // Assert
        context.Environment.Should().BeSameAs(environment);
    }

    [Fact]
    public void Application_PropertySetter_AssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var application = new ApplicationConfig { Name = "TestApp", Version = "2.0.0" };

        // Act
        context.Application = application;

        // Assert
        context.Application.Should().BeSameAs(application);
    }

    [Fact]
    public void DeploymentId_PropertySetter_AssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deploymentId = "test1234";

        // Act
        context.DeploymentId = deploymentId;

        // Assert
        context.DeploymentId.Should().Be(deploymentId);
    }

    [Fact]
    public void DeployedBy_PropertySetter_AssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deployedBy = "GitHub Actions";

        // Act
        context.DeployedBy = deployedBy;

        // Assert
        context.DeployedBy.Should().Be(deployedBy);
    }

    [Fact]
    public void DeployedAt_PropertySetter_AssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deployedAt = new DateTime(2023, 5, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        context.DeployedAt = deployedAt;

        // Assert
        context.DeployedAt.Should().Be(deployedAt);
    }

    [Fact]
    public void GetCommonTags_WithEmptyEnvironmentTags_ReturnsSystemTagsOnly()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags.Clear();

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.Should().HaveCount(6);
        tags.Should().ContainKey("Environment");
        tags.Should().ContainKey("Application");
        tags.Should().ContainKey("Version");
        tags.Should().ContainKey("DeploymentId");
        tags.Should().ContainKey("DeployedBy");
        tags.Should().ContainKey("DeployedAt");
    }

    [Fact]
    public void GetCommonTags_WithTagKeyCollision_SystemTagsOverrideEnvironmentTags()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags["Environment"] = "OverrideValue";
        context.Environment.Tags["Application"] = "OverrideApp";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["Environment"].Should().Be("Development");
        tags["Application"].Should().Be("TrialFinderV2");
    }

    [Fact]
    public void GetCommonTags_WithNullEnvironmentTags_ThrowsException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags = null!;

        // Act & Assert
        var action = () => context.GetCommonTags();
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetCommonTags_WithCustomDeployedAtFormat_UsesCorrectDateFormat()
    {
        // Arrange
        var context = CreateTestContext();
        context.DeployedAt = new DateTime(2023, 12, 25, 14, 30, 45, DateTimeKind.Utc);

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["DeployedAt"].Should().Be("2023-12-25");
    }

    [Fact]
    public void GetCommonTags_WithEmptyStrings_HandlesEmptyValues()
    {
        // Arrange
        var context = CreateTestContext();
        context.DeploymentId = "";
        context.DeployedBy = "";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["DeploymentId"].Should().Be("");
        tags["DeployedBy"].Should().Be("");
    }

    [Fact]
    public void ValidateNamingContext_WithNullEnvironmentName_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Name = null!;

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContext_WithNullApplicationName_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Application.Name = null!;

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContext_WithNullRegion_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Region = null!;

        // Act & Assert
        var action = () => context.ValidateNamingContext();
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Naming convention validation failed:*")
            .WithInnerException<ArgumentException>();
    }

    [Fact]
    public void Namer_ConcurrentAccess_ReturnsSameInstance()
    {
        // Arrange
        var context = CreateTestContext();
        var task1 = Task.Run(() => context.Namer);
        var task2 = Task.Run(() => context.Namer);

        // Act
        Task.WaitAll(task1, task2);

        // Assert
        task1.Result.Should().BeSameAs(task2.Result);
    }

    private static DeploymentContext CreateTestContext(
        string environment = "Development",
        string application = "TrialFinderV2", 
        string region = "us-east-1")
    {
        return new DeploymentContext
        {
            Environment = new EnvironmentConfig
            {
                Name = environment,
                Region = region,
                AccountId = "123456789012",
                AccountType = NamingConvention.GetAccountType(environment),
                Tags = new Dictionary<string, string>()
            },
            Application = new ApplicationConfig
            {
                Name = application,
                Version = "1.0.0"
            }
        };
    }
}