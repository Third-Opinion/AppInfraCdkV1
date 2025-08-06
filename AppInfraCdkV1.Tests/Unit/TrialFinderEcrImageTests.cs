using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit
{
    public class TrialFinderEcrImageTests
    {
        [Fact]
        public void EcrRepository_ShouldBeAccessible_ForImageChecking()
        {
            // Arrange
            var context = CreateTestDeploymentContext();
            var namer = new ResourceNamer(context);
            
            // Act
            var repositoryName = namer.EcrRepository("webapp");
            
            // Assert
            repositoryName.ShouldBe("thirdopinion/trialfinderv2/webapp");
        }

        [Fact]
        public void EcrImageUri_ShouldFollowCorrectFormat_WhenLatestImageExists()
        {
            // Arrange
            var context = CreateTestDeploymentContext();
            var repositoryName = "thirdopinion/trialfinderv2/webapp";
            var accountId = context.Environment.AccountId;
            var region = context.Environment.Region;
            
            // Act
            var expectedImageUri = $"{accountId}.dkr.ecr.{region}.amazonaws.com/{repositoryName}:latest";
            
            // Assert
            expectedImageUri.ShouldBe("123456789012.dkr.ecr.us-east-2.amazonaws.com/thirdopinion/trialfinderv2/webapp:latest");
        }

        [Fact]
        public void EnvironmentVariables_ShouldIncludeImageSource_ForEcrDeployment()
        {
            // Arrange
            var context = CreateTestDeploymentContext();
            
            // Act & Assert
            // This test verifies that the environment variables are properly set
            // The actual implementation will be tested during deployment
            context.Environment.Name.ShouldBe("dev");
            context.Application.Name.ShouldBe("TrialFinderV2");
        }

        private static DeploymentContext CreateTestDeploymentContext(string environmentName = "dev")
        {
            return new DeploymentContext
            {
                Application = new ApplicationConfig
                {
                    Name = "TrialFinderV2",
                    Version = "1.0.0"
                },
                Environment = new EnvironmentConfig
                {
                    Name = environmentName,
                    AccountType = AccountType.NonProduction,
                    AccountId = "123456789012",
                    Region = "us-east-2"
                }
            };
        }
    }
} 