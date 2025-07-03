namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentAccess
{
    /// <summary>
    ///     Environments that this environment can access
    /// </summary>
    public List<string> CanAccessEnvironments { get; set; } = new();

    /// <summary>
    ///     Environments that can access this environment
    /// </summary>
    public List<string> CanBeAccessedByEnvironments { get; set; } = new();

    /// <summary>
    ///     Whether this environment can access shared services
    /// </summary>
    public bool CanAccessSharedServices { get; set; } = true;

    /// <summary>
    ///     Specific shared services this environment can access
    /// </summary>
    public List<string> AllowedSharedServices { get; set; } = new();
}