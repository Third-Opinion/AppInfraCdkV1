namespace AppInfraCdkV1.Core.Models;

public class ApplicationConfig
{
    private string _name = string.Empty;
    private string _version = "1.0.0";
    private Dictionary<string, object> _settings = new();
    private ResourceSizing _sizing = new();
    private SecurityConfig _security = new();
    private MultiEnvironmentConfig _multiEnvironment = new();

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public string Version
    {
        get => _version;
        set => _version = value;
    }

    public Dictionary<string, object> Settings
    {
        get => _settings;
        set => _settings = value;
    }

    public ResourceSizing Sizing
    {
        get => _sizing;
        set => _sizing = value;
    }

    public SecurityConfig Security
    {
        get => _security;
        set => _security = value;
    }

    public MultiEnvironmentConfig MultiEnvironment
    {
        get => _multiEnvironment;
        set => _multiEnvironment = value;
    }
}