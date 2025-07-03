namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentAccessRule
{
    public string SourceEnvironment { get; set; } = string.Empty;
    public string TargetEnvironment { get; set; } = string.Empty;
    public List<string> AllowedServices { get; set; } = new();
    public List<int> AllowedPorts { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}