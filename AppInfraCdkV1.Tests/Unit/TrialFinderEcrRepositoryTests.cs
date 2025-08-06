using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.Core.Models;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Unit
{
    public class TrialFinderEcrRepositoryTests
    {
        [Fact]
        public void EcrRepository_ShouldFollowNamingConvention_ForTrialFinder()
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
        public void EcrRepository_ShouldBeConsistent_AcrossEnvironments()
        {
            // Arrange
            var devContext = CreateTestDeploymentContext("dev");
            var prodContext = CreateTestDeploymentContext("prod");
            var devNamer = new ResourceNamer(devContext);
            var prodNamer = new ResourceNamer(prodContext);
            
            // Act
            var devRepositoryName = devNamer.EcrRepository("webapp");
            var prodRepositoryName = prodNamer.EcrRepository("webapp");
            
            // Assert
            devRepositoryName.ShouldBe("thirdopinion/trialfinderv2/webapp");
            prodRepositoryName.ShouldBe("thirdopinion/trialfinderv2/webapp");
            devRepositoryName.ShouldBe(prodRepositoryName);
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