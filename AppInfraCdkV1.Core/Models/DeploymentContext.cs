using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

public class DeploymentContext
{
    private ResourceNamer? _namer;
    public EnvironmentConfig Environment { get; set; } = new();
    public ApplicationConfig Application { get; set; } = new();
    public string DeploymentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DeployedBy { get; set; } = "CDK";
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;

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