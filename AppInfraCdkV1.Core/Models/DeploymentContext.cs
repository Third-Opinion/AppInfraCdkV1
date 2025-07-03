using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

public class DeploymentContext
{
    private ResourceNamer? _namer;
    private EnvironmentConfig _environment = new();
    private ApplicationConfig _application = new();
    private string _deploymentId = Guid.NewGuid().ToString("N")[..8];
    private string _deployedBy = "CDK";
    private DateTime _deployedAt = DateTime.UtcNow;

    public EnvironmentConfig Environment
    {
        get => _environment;
        set => _environment = value;
    }

    public ApplicationConfig Application
    {
        get => _application;
        set => _application = value;
    }

    public string DeploymentId
    {
        get => _deploymentId;
        set => _deploymentId = value;
    }

    public string DeployedBy
    {
        get => _deployedBy;
        set => _deployedBy = value;
    }

    public DateTime DeployedAt
    {
        get => _deployedAt;
        set => _deployedAt = value;
    }

    /// <summary>
    ///     Gets the resource namer for this deployment context
    ///     Think of this as your personal naming assistant that knows your project details
    /// </summary>
    public ResourceNamer Namer => _namer ??= new ResourceNamer(this);

    public Dictionary<string, string> GetCommonTags()
    {
        var tags = new Dictionary<string, string>(Environment.Tags)
        {
            ["Environment"] = Environment.Name,
            ["Application"] = Application.Name,
            ["Version"] = Application.Version,
            ["DeploymentId"] = DeploymentId,
            ["DeployedBy"] = DeployedBy,
            ["DeployedAt"] = DeployedAt.ToString("yyyy-MM-dd")
        };

        return tags;
    }

    /// <summary>
    ///     Validates that all naming convention requirements are met
    /// </summary>
    public void ValidateNamingContext()
    {
        try
        {
            NamingConvention.GetEnvironmentPrefix(Environment.Name);
            NamingConvention.GetApplicationCode(Application.Name);
            NamingConvention.GetRegionCode(Environment.Region);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Naming convention validation failed: {ex.Message}", ex);
        }
    }
}