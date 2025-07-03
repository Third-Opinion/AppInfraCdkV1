namespace AppInfraCdkV1.Core.Models;

public class DatabaseSharingConfig
{
    /// <summary>
    ///     Whether database instances are shared across environments
    /// </summary>
    public bool ShareInstances { get; set; } = false;

    /// <summary>
    ///     Whether to use separate schemas/databases within shared instances
    /// </summary>
    public bool UseSeparateSchemas { get; set; } = true;

    /// <summary>
    ///     Environments that share database instances
    /// </summary>
    public List<string> SharingEnvironments { get; set; } = new();
}