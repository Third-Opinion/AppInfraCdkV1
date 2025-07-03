namespace AppInfraCdkV1.Core.Models;

public class ApplicationConfig
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, object> Settings { get; set; } = new();
    public ResourceSizing Sizing { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public MultiEnvironmentConfig MultiEnvironment { get; set; } = new();
}