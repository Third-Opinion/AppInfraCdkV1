namespace AppInfraCdkV1.Core.Models;

public class DatabaseSharingConfig
{
    private bool _shareInstances = false;
    private bool _useSeparateSchemas = true;
    private List<string> _sharingEnvironments = new();

    /// <summary>
    ///     Whether database instances are shared across environments
    /// </summary>
    public bool ShareInstances
    {
        get => _shareInstances;
        set => _shareInstances = value;
    }

    /// <summary>
    ///     Whether to use separate schemas/databases within shared instances
    /// </summary>
    public bool UseSeparateSchemas
    {
        get => _useSeparateSchemas;
        set => _useSeparateSchemas = value;
    }

    /// <summary>
    ///     Environments that share database instances
    /// </summary>
    public List<string> SharingEnvironments
    {
        get => _sharingEnvironments;
        set => _sharingEnvironments = value;
    }
}