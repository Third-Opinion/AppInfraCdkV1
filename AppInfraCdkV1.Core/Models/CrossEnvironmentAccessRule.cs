namespace AppInfraCdkV1.Core.Models;

public class CrossEnvironmentAccessRule
{
    private string _sourceEnvironment = string.Empty;
    private string _targetEnvironment = string.Empty;
    private List<string> _allowedServices = new();
    private List<int> _allowedPorts = new();
    private string _description = string.Empty;

    public string SourceEnvironment
    {
        get => _sourceEnvironment;
        set => _sourceEnvironment = value;
    }

    public string TargetEnvironment
    {
        get => _targetEnvironment;
        set => _targetEnvironment = value;
    }

    public List<string> AllowedServices
    {
        get => _allowedServices;
        set => _allowedServices = value;
    }

    public List<int> AllowedPorts
    {
        get => _allowedPorts;
        set => _allowedPorts = value;
    }

    public string Description
    {
        get => _description;
        set => _description = value;
    }
}