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
    [Fact]
    public async Task ShowNamesOnly_BasicExecution_ReturnsExpectedOutput()
    {
        // Arrange - Use simple command execution from the solution root
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project AppInfraCdkV1.Deploy -- --environment=Development --app=TrialFinderV2 --show-names-only",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = GetSolutionRoot()
        };

        // Act
        using var process = Process.Start(processStartInfo);
        process.ShouldNotBeNull("Process should start successfully");
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Assert
        process.ExitCode.ShouldBe(0, $"Command should succeed.\nOutput: {output}\nError: {error}");
        output.ShouldContain("Starting CDK deployment for TrialFinderV2 in Development");
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