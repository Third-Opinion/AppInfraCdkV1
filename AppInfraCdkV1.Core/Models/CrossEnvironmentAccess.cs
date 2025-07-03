namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentAccess
{
    private List<string> _canAccessEnvironments = new();
    private List<string> _canBeAccessedByEnvironments = new();
    private bool _canAccessSharedServices = true;
    private List<string> _allowedSharedServices = new();

    /// <summary>
    ///     Environments that this environment can access
    /// </summary>
    public List<string> CanAccessEnvironments
    {
        get => _canAccessEnvironments;
        set => _canAccessEnvironments = value;
    }

    /// <summary>
    ///     Environments that can access this environment
    /// </summary>
    public List<string> CanBeAccessedByEnvironments
    {
        get => _canBeAccessedByEnvironments;
        set => _canBeAccessedByEnvironments = value;
    }

    /// <summary>
    ///     Whether this environment can access shared services
    /// </summary>
    public bool CanAccessSharedServices
    {
        get => _canAccessSharedServices;
        set => _canAccessSharedServices = value;
    }

    /// <summary>
    ///     Specific shared services this environment can access
    /// </summary>
    public List<string> AllowedSharedServices
    {
        get => _allowedSharedServices;
        set => _allowedSharedServices = value;
    }
}