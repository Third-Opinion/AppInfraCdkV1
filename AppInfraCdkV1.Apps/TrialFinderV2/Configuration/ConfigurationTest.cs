using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Configuration;

/// <summary>
/// Simple test class to validate configuration loading
/// </summary>
public static class ConfigurationTest
{
    /// <summary>
    /// Test loading development configuration
    /// </summary>
    public static void TestDevelopmentConfig()
    {
        try
        {
            var loader = new ConfigurationLoader();
            var config = loader.LoadFullConfig("development");
            
            Console.WriteLine("✅ Development configuration loaded successfully");
            Console.WriteLine($"VPC Pattern: {config.VpcNamePattern}");
            Console.WriteLine($"Service Name: {config.EcsConfiguration?.ServiceName}");
            Console.WriteLine($"Task Definitions: {config.EcsConfiguration?.TaskDefinition?.Count ?? 0}");
            
            foreach (var taskDef in config.EcsConfiguration?.TaskDefinition ?? new List<TaskDefinitionConfig>())
            {
                Console.WriteLine($"  Task: {taskDef.TaskDefinitionName}");
                Console.WriteLine($"    Type: {taskDef.TaskType}");
                Console.WriteLine($"    Is Scheduled Job: {taskDef.IsScheduledJob}");
                Console.WriteLine($"    CPU: {taskDef.Cpu}");
                Console.WriteLine($"    Memory: {taskDef.Memory}");
                
                if (taskDef.IsScheduledJob)
                {
                    Console.WriteLine($"    Schedule: {taskDef.ScheduleExpression}");
                    Console.WriteLine($"    Timeout: {taskDef.JobTimeout}s");
                }
                
                Console.WriteLine($"    Containers: {taskDef.ContainerDefinitions?.Count ?? 0}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to load development configuration: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Test loading all environment configurations
    /// </summary>
    public static void TestAllConfigs()
    {
        var environments = new[] { "development", "staging", "production", "integration" };
        
        foreach (var env in environments)
        {
            try
            {
                var loader = new ConfigurationLoader();
                var config = loader.LoadFullConfig(env);
                
                Console.WriteLine($"✅ {env.ToUpperInvariant()} configuration loaded successfully");
                Console.WriteLine($"  Tasks: {config.EcsConfiguration?.TaskDefinition?.Count ?? 0}");
                
                var webAppTasks = config.EcsConfiguration?.TaskDefinition?.Count(t => !t.IsScheduledJob) ?? 0;
                var scheduledTasks = config.EcsConfiguration?.TaskDefinition?.Count(t => t.IsScheduledJob) ?? 0;
                
                Console.WriteLine($"  Web App Tasks: {webAppTasks}");
                Console.WriteLine($"  Scheduled Tasks: {scheduledTasks}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to load {env} configuration: {ex.Message}");
            }
        }
    }
}
