namespace AppInfraCdkV1.Core.Models;

public class VpcCidrConfig
{
    /// <summary>
    ///     Primary CIDR block for the VPC
    /// </summary>
    public string PrimaryCidr { get; set; } = string.Empty;

    /// <summary>
    ///     Secondary CIDR blocks if needed
    /// </summary>
    public List<string> SecondaryCidrs { get; set; } = new();

    /// <summary>
    ///     Gets a default CIDR based on environment name to avoid conflicts
    /// </summary>
    public static VpcCidrConfig GetDefaultForEnvironment(string environmentName)
    {
        return environmentName switch
        {
            "Development" => new VpcCidrConfig { PrimaryCidr = "10.0.0.0/16" },
            "QA" => new VpcCidrConfig { PrimaryCidr = "10.1.0.0/16" },
            "Test" => new VpcCidrConfig { PrimaryCidr = "10.2.0.0/16" },
            "Integration" => new VpcCidrConfig { PrimaryCidr = "10.3.0.0/16" },
            "Staging" => new VpcCidrConfig { PrimaryCidr = "10.10.0.0/16" },
            "PreProduction" => new VpcCidrConfig { PrimaryCidr = "10.11.0.0/16" },
            "Production" => new VpcCidrConfig { PrimaryCidr = "10.20.0.0/16" },
            "UAT" => new VpcCidrConfig { PrimaryCidr = "10.12.0.0/16" },
            _ => new VpcCidrConfig { PrimaryCidr = "10.99.0.0/16" } // Default fallback
        };
    }
}