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
        var result = _resourceNamer.EcsCluster("api");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-ecs");
        result.ShouldContain("api");
    }

    [Fact]
    public void EcsTaskDefinition_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsTaskDefinition("web");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-task");
        result.ShouldContain("web");
    }

    [Fact]
    public void EcsService_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.EcsService("api");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-svc");
        result.ShouldContain("api");
    }

    [Fact]
    public void ApplicationLoadBalancer_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.ApplicationLoadBalancer("web");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-alb");
        result.ShouldContain("web");
    }

    [Fact]
    public void NetworkLoadBalancer_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.NetworkLoadBalancer("internal");

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
        var result = _resourceNamer.RdsInstance("primary");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-rds");
        result.ShouldContain("primary");
    }

    [Theory]
    [InlineData("cache")]
    [InlineData("session")]
    public void ElastiCache_WithPurpose_ShouldGenerateCorrectName(string purpose)
    {
        // Act
        var result = _resourceNamer.ElastiCache(purpose);

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-cache");
        result.ShouldContain(purpose);
    }

    [Fact]
    public void Lambda_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.Lambda("processor");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-lambda");
        result.ShouldContain("processor");
    }

    [Fact]
    public void IamRole_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamRole("ecs-task");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-role");
        result.ShouldContain("ecs-task");
    }

    [Fact]
    public void IamUser_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamUser("service");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-user");
        result.ShouldContain("service");
    }

    [Fact]
    public void IamPolicy_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.IamPolicy("s3-access");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-policy");
        result.ShouldContain("s3-access");
    }

    [Fact]
    public void SecurityGroupForAlb_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForAlb("web");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-alb");
        result.ShouldContain("web");
    }

    [Fact]
    public void SecurityGroupForEcs_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForEcs("api");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sg-ecs");
        result.ShouldContain("api");
    }

    [Fact]
    public void SecurityGroupForRds_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecurityGroupForRds("primary");

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
        var result = _resourceNamer.S3Bucket("documents");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("thirdopinion.io");
        result.ShouldContain("dev");
        result.ShouldContain("tfv2");
        result.ShouldContain("documents");
    }

    [Fact]
    public void LogGroup_WithServiceTypeAndPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.LogGroup("ecs", "web");

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
        var result = _resourceNamer.SnsTopics("notifications");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sns");
        result.ShouldContain("notifications");
    }

    [Fact]
    public void SqsQueue_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SqsQueue("processing");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-sqs");
        result.ShouldContain("processing");
    }

    [Fact]
    public void SecretsManager_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.SecretsManager("database");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-secret");
        result.ShouldContain("database");
    }

    [Fact]
    public void ParameterStore_WithPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.ParameterStore("config");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-param");
        result.ShouldContain("config");
    }

    [Fact]
    public void Custom_WithResourceTypeAndPurpose_ShouldGenerateCorrectName()
    {
        // Act
        var result = _resourceNamer.Custom("cloudfront", "cdn");

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldStartWith("dev-tfv2-cloudfront");
        result.ShouldContain("cdn");
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
                AccountType = environmentName == "Production" ? AccountType.Production : AccountType.NonProduction,
                IsolationStrategy = new EnvironmentIsolationStrategy
                {
                    VpcCidr = new VpcCidrConfig
                    {
                        PrimaryCidr = "10.0.0.0/16"
                    }
                }
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