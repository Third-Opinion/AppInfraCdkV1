using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class DeploymentContextTests
{
    [Fact]
    public void GetCommonTagsWithValidContextReturnsAllRequiredTags()
    {
        // Arrange
        var context = CreateTestContext();
        var expectedDeploymentDate = context.DeployedAt.ToString("yyyy-MM-dd");

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.ShouldContainKey("Environment");
        tags["Environment"].ShouldBe("Development");
        tags.ShouldContainKey("Application");
        tags["Application"].ShouldBe("TrialFinderV2");
        tags.ShouldContainKey("Version");
        tags["Version"].ShouldBe("1.0.0");
        tags.ShouldContainKey("DeployedBy");
        tags["DeployedBy"].ShouldBe("CDK");
        tags.ShouldContainKey("DeployedAt");
        tags["DeployedAt"].ShouldBe(expectedDeploymentDate);
        tags.ShouldContainKey("DeploymentId");
        tags["DeploymentId"].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GetCommonTagsIncludesEnvironmentTags()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags["CustomTag"] = "CustomValue";
        context.Environment.Tags["Team"] = "Infrastructure";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.ShouldContainKey("CustomTag");
        tags["CustomTag"].ShouldBe("CustomValue");
        tags.ShouldContainKey("Team");
        tags["Team"].ShouldBe("Infrastructure");
    }

    [Fact]
    public void ValidateNamingContextWithValidContextDoesNotThrow()
    {
        // Arrange
        var context = CreateTestContext();

        // Act & Assert
        Should.NotThrow(() => context.ValidateNamingContext());
    }

    [Fact]
    public void ValidateNamingContextWithInvalidEnvironmentThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Name = "InvalidEnvironment";

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContextWithInvalidApplicationThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Application.Name = "InvalidApplication";

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContextWithInvalidRegionThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Region = "invalid-region";

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void NamerPropertyInitializationCreatesResourceNamerInstance()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var namer = context.Namer;

        // Assert
        namer.ShouldNotBeNull();
        namer.ShouldBeOfType<ResourceNamer>();
    }

    [Fact]
    public void NamerPropertyAccessReturnsSameInstance()
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var namer1 = context.Namer;
        var namer2 = context.Namer;

        // Assert
        namer1.ShouldBeSameAs(namer2);
    }

    [Fact]
    public void DeploymentIdDefaultValueIsValidFormat()
    {
        // Arrange & Act
        var context = new DeploymentContext();

        // Assert
        context.DeploymentId.ShouldNotBeNullOrEmpty();
        context.DeploymentId.Length.ShouldBe(8); // GUID first 8 characters
        context.DeploymentId.ShouldMatch("^[a-f0-9]{8}$"); // Hex digits only
    }

    [Fact]
    public void DeployedAtDefaultValueIsRecentUtcTime()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;
        
        // Act
        var context = new DeploymentContext();
        var afterCreation = DateTime.UtcNow;

        // Assert
        context.DeployedAt.ShouldBeGreaterThanOrEqualTo(beforeCreation);
        context.DeployedAt.ShouldBeLessThanOrEqualTo(afterCreation);
        context.DeployedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void DeployedByDefaultValueIsCDK()
    {
        // Arrange & Act
        var context = new DeploymentContext();

        // Assert
        context.DeployedBy.ShouldBe("CDK");
    }

    [Theory]
    [InlineData("Production", "TrialFinderV2", "us-east-1")]
    [InlineData("Development", "TrialFinderV2", "eu-west-1")]
    [InlineData("Staging", "TrialFinderV2", "us-west-2")]
    public void ValidateNamingContextWithValidCombinationsDoesNotThrow(string environment, string application, string region)
    {
        // Arrange
        var context = CreateTestContext(environment, application, region);

        // Act & Assert
        Should.NotThrow(() => context.ValidateNamingContext());
    }

    [Fact]
    public void EnvironmentPropertySetterAssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var environment = new EnvironmentConfig { Name = "TestEnv", Region = "us-west-1" };

        // Act
        context.Environment = environment;

        // Assert
        context.Environment.ShouldBeSameAs(environment);
    }

    [Fact]
    public void ApplicationPropertySetterAssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var application = new ApplicationConfig { Name = "TestApp", Version = "2.0.0" };

        // Act
        context.Application = application;

        // Assert
        context.Application.ShouldBeSameAs(application);
    }

    [Fact]
    public void DeploymentIdPropertySetterAssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deploymentId = "test1234";

        // Act
        context.DeploymentId = deploymentId;

        // Assert
        context.DeploymentId.ShouldBe(deploymentId);
    }

    [Fact]
    public void DeployedByPropertySetterAssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deployedBy = "GitHub Actions";

        // Act
        context.DeployedBy = deployedBy;

        // Assert
        context.DeployedBy.ShouldBe(deployedBy);
    }

    [Fact]
    public void DeployedAtPropertySetterAssignsValue()
    {
        // Arrange
        var context = new DeploymentContext();
        var deployedAt = new DateTime(2023, 5, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        context.DeployedAt = deployedAt;

        // Assert
        context.DeployedAt.ShouldBe(deployedAt);
    }

    [Fact]
    public void GetCommonTagsWithEmptyEnvironmentTagsReturnsSystemTagsOnly()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags.Clear();

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags.Count.ShouldBe(6);
        tags.ShouldContainKey("Environment");
        tags.ShouldContainKey("Application");
        tags.ShouldContainKey("Version");
        tags.ShouldContainKey("DeploymentId");
        tags.ShouldContainKey("DeployedBy");
        tags.ShouldContainKey("DeployedAt");
    }

    [Fact]
    public void GetCommonTagsWithTagKeyCollisionSystemTagsOverrideEnvironmentTags()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags["Environment"] = "OverrideValue";
        context.Environment.Tags["Application"] = "OverrideApp";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["Environment"].ShouldBe("Development");
        tags["Application"].ShouldBe("TrialFinderV2");
    }

    [Fact]
    public void GetCommonTagsWithNullEnvironmentTagsThrowsException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Tags = null!;

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => context.GetCommonTags());
    }

    [Fact]
    public void GetCommonTagsWithCustomDeployedAtFormatUsesCorrectDateFormat()
    {
        // Arrange
        var context = CreateTestContext();
        context.DeployedAt = new DateTime(2023, 12, 25, 14, 30, 45, DateTimeKind.Utc);

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["DeployedAt"].ShouldBe("2023-12-25");
    }

    [Fact]
    public void GetCommonTagsWithEmptyStringsHandlesEmptyValues()
    {
        // Arrange
        var context = CreateTestContext();
        context.DeploymentId = "";
        context.DeployedBy = "";

        // Act
        var tags = context.GetCommonTags();

        // Assert
        tags["DeploymentId"].ShouldBe("");
        tags["DeployedBy"].ShouldBe("");
    }

    [Fact]
    public void ValidateNamingContextWithNullEnvironmentNameThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Name = null!;

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContextWithNullApplicationNameThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Application.Name = null!;

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void ValidateNamingContextWithNullRegionThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateTestContext();
        context.Environment.Region = null!;

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => context.ValidateNamingContext());
        ex.Message.ShouldStartWith("Naming convention validation failed:");
        ex.InnerException.ShouldBeOfType<ArgumentNullException>();
    }

    [Fact]
    public void NamerConcurrentAccessReturnsSameInstance()
    {
        // Arrange
        var context = CreateTestContext();
        var task1 = Task.Run(() => context.Namer);
        var task2 = Task.Run(() => context.Namer);

        // Act
        Task.WaitAll(task1, task2);

        // Assert
        task1.Result.ShouldBeSameAs(task2.Result);
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