namespace AppInfraCdkV1.Core.Models;

public class VpcCidrConfig
{
    private string _primaryCidr = string.Empty;
    private List<string> _secondaryCidrs = new();

    /// <summary>
    ///     Primary CIDR block for the VPC
    /// </summary>
    public string PrimaryCidr
    {
        get => _primaryCidr;
        set => _primaryCidr = value;
    }

    /// <summary>
    ///     Secondary CIDR blocks if needed
    /// </summary>
    public List<string> SecondaryCidrs
    {
        get => _secondaryCidrs;
        set => _secondaryCidrs = value;
    }

    /// <summary>
    ///     Gets a default CIDR based on environment name to avoid conflicts
    /// </summary>
    public static VpcCidrConfig GetDefaultForEnvironment(string environmentName)
    {
        return environmentName switch
        {
            "Development" => new VpcCidrConfig { PrimaryCidr = "10.0.0.0/16" },
            "Staging" => new VpcCidrConfig { PrimaryCidr = "10.10.0.0/16" },
            "Production" => new VpcCidrConfig { PrimaryCidr = "10.20.0.0/16" },
            _ => new VpcCidrConfig { PrimaryCidr = "10.99.0.0/16" } // Default fallback
        };
    }
}