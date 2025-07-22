using System.Diagnostics;
using System.Text;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Integration;

/// <summary>
/// Integration tests for the AppInfraCdkV1.Deploy --show-names-only command.
/// These tests validate that the command executes successfully and produces expected output
/// for different environments and applications.
/// </summary>
public class ShowNamesOnlyIntegrationTests
{
    private const int TimeoutMilliseconds = 60000; // 60 seconds
    private readonly string _solutionRoot;
    private readonly string _deployProjectPath;

    public ShowNamesOnlyIntegrationTests()
    {
        // Find solution root by looking for the .sln file
        var currentDirectory = Directory.GetCurrentDirectory();
        var solutionRoot = FindSolutionRoot(currentDirectory);
        _solutionRoot = solutionRoot ?? throw new InvalidOperationException("Could not find solution root");
        _deployProjectPath = Path.Combine(_solutionRoot, "AppInfraCdkV1.Deploy");
    }

    private static string? FindSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any())
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    [Fact]
    public void VerifyTestSetup_SolutionAndProjectPathsExist()
    {
        // Assert
        Directory.Exists(_solutionRoot).ShouldBeTrue($"Solution root should exist: {_solutionRoot}");
        Directory.Exists(_deployProjectPath).ShouldBeTrue($"Deploy project path should exist: {_deployProjectPath}");
        File.Exists(Path.Combine(_deployProjectPath, "AppInfraCdkV1.Deploy.csproj")).ShouldBeTrue("Deploy project file should exist");
        File.Exists(Path.Combine(_solutionRoot, "AppInfraCdkV1.sln")).ShouldBeTrue("Solution file should exist");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public async Task ShowNamesOnly_WithValidEnvironmentAndApp_ExecutesSuccessfully(string environment, string application)
    {
        // Arrange
        var arguments = $"--environment={environment} --app={application} --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0, $"Command should succeed. Error output: {result.ErrorOutput}\nStandard output: {result.StandardOutput}\nWorking directory: {_solutionRoot}\nDeploy project path: {_deployProjectPath}");
        result.StandardOutput.ShouldNotBeEmpty("Command should produce output");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public async Task ShowNamesOnly_WithValidEnvironmentAndApp_ContainsExpectedResourceNames(string environment, string application)
    {
        // Arrange
        var arguments = $"--environment={environment} --app={application} --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);

        // Validate that key resource names are displayed
        result.StandardOutput.ShouldContain("Resource names that will be created:");
        result.StandardOutput.ShouldContain("Stack:");
        result.StandardOutput.ShouldContain("VPC:");
        result.StandardOutput.ShouldContain("ECS Cluster:");
        result.StandardOutput.ShouldContain("Web Service:");
        result.StandardOutput.ShouldContain("Database:");
        result.StandardOutput.ShouldContain("Security Groups:");
        result.StandardOutput.ShouldContain("IAM Roles:");
        result.StandardOutput.ShouldContain("CloudWatch:");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ShowNamesOnly_ForTrialFinderV2_ContainsTrialFinderSpecificResources(string environment)
    {
        // Arrange
        var arguments = $"--environment={environment} --app=TrialFinderV2 --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("TrialFinderV2-Specific Resources:");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public async Task ShowNamesOnly_WithValidEnvironmentAndApp_ContainsAccountContext(string environment, string application)
    {
        // Arrange
        var arguments = $"--environment={environment} --app={application} --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);

        // Validate account context information is displayed
        result.StandardOutput.ShouldContain("Account Context:");
        result.StandardOutput.ShouldContain($"Environment: {environment}");
        result.StandardOutput.ShouldContain("Account ID:");
        result.StandardOutput.ShouldContain("Account Type:");
        result.StandardOutput.ShouldContain("Region:");
    }

    [Theory]
    [InlineData("Development", "TrialFinderV2")]
    [InlineData("Production", "TrialFinderV2")]
    public async Task ShowNamesOnly_WithValidEnvironmentAndApp_PassesValidation(string environment, string application)
    {
        // Arrange
        var arguments = $"--environment={environment} --app={application} --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);

        // Validate that all validations pass
        result.StandardOutput.ShouldContain("Validating naming conventions...");
        result.StandardOutput.ShouldContain("Naming conventions validated successfully");
        result.StandardOutput.ShouldContain("Validating multi-environment setup...");
        result.StandardOutput.ShouldContain("Multi-environment setup validated successfully");
        result.StandardOutput.ShouldContain("All resource names within AWS limits");
        result.StandardOutput.ShouldContain("Account-level uniqueness validated");
    }

    [Fact]
    public async Task ShowNamesOnly_WithInvalidEnvironment_FailsWithError()
    {
        // Arrange
        var arguments = "--environment=InvalidEnvironment --app=TrialFinderV2 --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldNotBe(0, "Command should fail with invalid environment");
        result.ErrorOutput.ShouldContain("Environment configuration not found");
    }

    [Fact]
    public async Task ShowNamesOnly_WithInvalidApplication_FailsWithError()
    {
        // Arrange
        var arguments = "--environment=Development --app=InvalidApp --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldNotBe(0, "Command should fail with invalid application");
        result.ErrorOutput.ShouldContain("Unsupported application");
    }

    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("Production", "prod")]
    public async Task ShowNamesOnly_GeneratesCorrectNamingPrefix(string environment, string expectedPrefix)
    {
        // Arrange
        var arguments = $"--environment={environment} --app=TrialFinderV2 --show-names-only";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);

        // Validate that resource names use the correct environment prefix
        result.StandardOutput.ShouldContain($"{expectedPrefix}-tfv2-");
    }

    [Fact]
    public async Task ShowNamesOnly_WithoutArguments_UsesDefaultEnvironmentAndApp()
    {
        // Arrange - No arguments, should use defaults from environment variables or fallbacks
        var arguments = "--show-names-only";
        
        // Get the expected environment from CDK_ENVIRONMENT or default to Development
        var expectedEnvironment = Environment.GetEnvironmentVariable("CDK_ENVIRONMENT") ?? "Development";
        var expectedPrefix = expectedEnvironment == "Production" ? "prod" : "dev";

        // Act
        var result = await ExecuteDeployCommand(arguments);

        // Assert
        result.ExitCode.ShouldBe(0);
        
        // Should use environment from CDK_ENVIRONMENT or default (Development) and application (TrialFinderV2)
        result.StandardOutput.ShouldContain($"Starting CDK deployment for TrialFinderV2 in {expectedEnvironment}");
        result.StandardOutput.ShouldContain($"{expectedPrefix}-tfv2-");
    }

    [Fact]
    public async Task ShowNamesOnly_CompletesWithinReasonableTime()
    {
        // Arrange
        var arguments = "--environment=Development --app=TrialFinderV2 --show-names-only";
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await ExecuteDeployCommand(arguments);
        stopwatch.Stop();

        // Assert
        result.ExitCode.ShouldBe(0);
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(TimeoutMilliseconds, 
            "Command should complete within reasonable time");
    }

    private async Task<ProcessResult> ExecuteDeployCommand(string arguments)
    {
        // Use the built executable instead of dotnet run for more reliable execution
        // Check for Release first (used by CI), then Debug as fallback
        var releasePath = Path.Combine(_deployProjectPath, "bin", "Release", "net8.0", "AppInfraCdkV1.Deploy.dll");
        var debugPath = Path.Combine(_deployProjectPath, "bin", "Debug", "net8.0", "AppInfraCdkV1.Deploy.dll");
        
        var executablePath = File.Exists(releasePath) ? releasePath : debugPath;
        
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{executablePath}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _deployProjectPath
        };

        using var process = new Process { StartInfo = processStartInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellationTokenSource = new CancellationTokenSource(TimeoutMilliseconds);
        
        try
        {
            await process.WaitForExitAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Ignore errors when killing process
            }
            throw new TimeoutException($"Process did not complete within {TimeoutMilliseconds}ms timeout");
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputBuilder.ToString(),
            ErrorOutput = errorBuilder.ToString()
        };
    }

    private record ProcessResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string ErrorOutput { get; init; } = string.Empty;
    }
}