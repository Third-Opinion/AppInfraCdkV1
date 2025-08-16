using System.Diagnostics;
using Shouldly;
using Xunit;

namespace AppInfraCdkV1.Tests.Integration;

/// <summary>
/// Simple integration test for the AppInfraCdkV1.Deploy --show-names-only command.
/// This is a basic sanity check to ensure the command executes successfully.
/// </summary>
public class ShowNamesOnlyBasicTest
{
    [Theory]
    [InlineData("TrialFinderV2")]
    [InlineData("TrialMatch")]
    public async Task ShowNamesOnly_BasicExecution_ReturnsExpectedOutput(string application)
    {
        // Arrange - Use built executable for reliable execution
        var solutionRoot = GetSolutionRoot();
        var deployProjectPath = Path.Combine(solutionRoot, "AppInfraCdkV1.Deploy");
        
        // Check for Release first (used by CI), then Debug as fallback
        var releasePath = Path.Combine(deployProjectPath, "bin", "Release", "net8.0", "AppInfraCdkV1.Deploy.dll");
        var debugPath = Path.Combine(deployProjectPath, "bin", "Debug", "net8.0", "AppInfraCdkV1.Deploy.dll");
        var executablePath = File.Exists(releasePath) ? releasePath : debugPath;
        
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{executablePath}\" --environment=Development --app={application} --show-names-only",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = deployProjectPath
        };

        // Act
        using var process = Process.Start(processStartInfo);
        process.ShouldNotBeNull("Process should start successfully");
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Assert
        process.ExitCode.ShouldBe(0, $"Command should succeed.\nOutput: {output}\nError: {error}");
        output.ShouldContain($"Starting CDK deployment for {application} in Development");
        output.ShouldContain("Resource names that will be created:");
        output.ShouldContain("VPC:");
        output.ShouldContain("ECS Cluster:");
    }

    private static string GetSolutionRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var solutionRoot = FindSolutionRoot(currentDir);
        return solutionRoot ?? throw new InvalidOperationException($"Could not find solution root starting from {currentDir}");
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
}