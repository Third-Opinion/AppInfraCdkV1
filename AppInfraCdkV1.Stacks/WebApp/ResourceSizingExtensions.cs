using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Stacks.WebApp;

public static class ResourceSizingExtensions
{
    public static int GetMemoryLimit(this ResourceSizing sizing)
    {
        return sizing.InstanceType switch
        {
            "t3.micro" => 1024,
            "t3.small" => 2048,
            "t3.medium" => 4096,
            "t3.large" => 8192,
            "t3.xlarge" => 16384,
            _ => 2048
        };
    }

    public static int GetCpuLimit(this ResourceSizing sizing)
    {
        return sizing.InstanceType switch
        {
            "t3.micro" => 256,
            "t3.small" => 512,
            "t3.medium" => 1024,
            "t3.large" => 2048,
            "t3.xlarge" => 4096,
            _ => 512
        };
    }
}