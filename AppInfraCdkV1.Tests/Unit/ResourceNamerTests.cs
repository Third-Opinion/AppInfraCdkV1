using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit;

public class ResourceNamerTests
{
    private readonly DeploymentContext _testContext;
    private readonly ResourceNamer _resourceNamer;

    public ResourceNamerTests()
    {
        _testContext = CreateTestDeploymentContext();
        _resourceNamer = new ResourceNamer(_testContext);
    }

    [Fact]
    public void Constructor_WithNullContext_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new ResourceNamer(null!));
    }

    [Fact]
    public void EcsCluster_WithDefaultPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsCluster();

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-ecs");
        result.ShouldContain("main");
    }

    [Fact]
    public void EcsCluster_WithCustomPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsCluster(ResourcePurpose.Api);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-ecs");
        result.ShouldContain("api");
    }

    [Fact]
    public void EcsTaskDefinition_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsTaskDefinition(ResourcePurpose.Web);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-task");
        result.ShouldContain("web");
    }

    [Fact]
    public void EcsService_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsService(ResourcePurpose.Api);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-svc");
        result.ShouldContain("api");
    }

    [Fact]
    public void ApplicationLoadBalancer_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.ApplicationLoadBalancer(ResourcePurpose.Web);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-alb");
        result.ShouldContain("web");
    }

    [Fact]
    public void NetworkLoadBalancer_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.NetworkLoadBalancer(ResourcePurpose.Internal);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-nlb");
        result.ShouldContain("internal");
    }

    [Fact]
    public void Vpc_WithDefaultPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.Vpc();

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-vpc");
        result.ShouldContain("main");
    }

    [Fact]
    public void RdsInstance_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.RdsInstance(ResourcePurpose.Primary);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-rds");
        result.ShouldContain("primary");
    }

    [Theory]
    [InlineData(StoragePurpose.Cache, "cache")]
    [InlineData(StoragePurpose.Session, "session")]
    public void ElastiCache_WithPurpose_ShouldGenerateCorrectName(StoragePurpose purpose, string expectedString)
    {
        // Act
        var result = _resourceNamer.ElastiCache(purpose);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-cache");
        result.ShouldContain(expectedString);
    }

    [Fact]
    public void Lambda_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.Lambda(ResourcePurpose.Api); // Changed from processor to Api since processor isn't in enum

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-lambda");
        result.ShouldContain("api");
    }

    [Fact]
    public void IamRole_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamRole(IamPurpose.EcsExecution);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-role");
        result.ShouldContain("ecs-exec");
    }

    [Fact]
    public void IamUser_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamUser(IamPurpose.Service);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-user");
        result.ShouldContain("service");
    }

    [Fact]
    public void IamPolicy_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamPolicy(IamPurpose.S3Access);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-policy");
        result.ShouldContain("s3-access");
    }

    [Fact]
    public void SecurityGroupForAlb_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForAlb(ResourcePurpose.Web);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-alb");
        result.ShouldContain("web");
    }

    [Fact]
    public void SecurityGroupForEcs_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForEcs(ResourcePurpose.Api);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-ecs");
        result.ShouldContain("api");
    }

    [Fact]
    public void SecurityGroupForRds_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForRds(ResourcePurpose.Primary);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-rds");
        result.ShouldContain("primary");
    }

    [Fact]
    public void SecurityGroupForBastion_WithDefaultPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForBastion();

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-bastion");
        result.ShouldContain("admin");
    }

    [Fact]
    public void S3Bucket_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.S3Bucket(StoragePurpose.Documents);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("thirdopinion.io");
        result.ShouldContain("dev");
        result.ShouldContain("tfv2");
        result.ShouldContain("docs");
    }

    [Fact]
    public void LogGroup_WithServiceTypeAndPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.LogGroup("ecs", ResourcePurpose.Web);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("/aws/ecs");
        result.ShouldContain("dev");
        result.ShouldContain("tfv2");
        result.ShouldContain("web");
    }

    [Fact]
    public void SnsTopics_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SnsTopics(NotificationPurpose.General);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sns");
        result.ShouldContain("notifications");
    }

    [Fact]
    public void SqsQueue_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SqsQueue(QueuePurpose.Processing);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sqs");
        result.ShouldContain("processing");
    }

    [Fact]
    public void SecretsManager_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecretsManager(ResourcePurpose.Primary);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-secret");
        result.ShouldContain("primary");
    }

    [Fact]
    public void ParameterStore_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.ParameterStore(ResourcePurpose.Main);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-param");
        result.ShouldContain("main");
    }

    [Fact]
    public void Custom_WithResourceTypeAndPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.Custom("cloudfront", ResourcePurpose.Web);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-cloudfront");
        result.ShouldContain("web");
    }

    [Fact]
    public void IamRole_WithPurpose_ShouldGenerateCorrectNameAndPrintToConsole()
    {
        // Arrange
        var purpose = IamPurpose.EcsExecution;
        var purposeString = "ecs-exec"; // Expected string representation
        Console.WriteLine($"🧪 Testing IAM Role creation with purpose: {purpose}");
        
        // Act
        var result = _resourceNamer.IamRole(purpose);
        
        // Log the generated name to console
        Console.WriteLine($"✅ Generated IAM Role name: {result}");
        Console.WriteLine($"🔍 Environment: {_testContext.Environment.Name}");
        Console.WriteLine($"🏷️  Application: {_testContext.Application.Name}");
        Console.WriteLine($"🌍 Region: {_testContext.Environment.Region}");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-role");
        result.ShouldContain(purposeString);
        result.ShouldEndWith(purposeString); // Should end with the purpose
        
        // Validate the full expected format: {env}-{app}-role-{region}-{purpose}
        var expectedPattern = $"dev-tfv2-role-ue2-{purposeString}";
        result.ShouldBe(expectedPattern);
        
        Console.WriteLine($"✅ IAM Role name validation passed: {result}");
    }

    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("Staging", "stg")]
    [InlineData("Production", "prod")]
    public void ResourceNamer_WithDifferentEnvironments_ShouldUseCorrectPrefix(string environment, string expectedPrefix)
    {
        // Arrange
        var context = CreateTestDeploymentContext(environment);
        var namer = new ResourceNamer(context);

        // Act
        var result = namer.EcsCluster();

        // Assert
        result.ShouldStartWith(expectedPrefix);
    }

    private static DeploymentContext CreateTestDeploymentContext(string environmentName = "Development")
    {
        return new DeploymentContext
        {
            Environment = new EnvironmentConfig
            {
                Name = environmentName,
                AccountId = "123456789012",
                Region = "us-east-2",
                AccountType = environmentName == "Production" ? AccountType.Production : AccountType.NonProduction
            },
            Application = new ApplicationConfig
            {
                Name = "TrialFinderV2",
                Version = "1.0.0",
                Sizing = new ResourceSizing(),
                Security = new SecurityConfig(),
                Settings = new Dictionary<string, object>(),
                MultiEnvironment = new MultiEnvironmentConfig()
            },
            DeployedBy = "TestRunner"
        };
    }
}