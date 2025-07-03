namespace AppInfraCdkV1.Core.Models;

public class ResourceSizing
{
    public string InstanceType { get; set; } = "t3.micro";
    public int MinCapacity { get; set; } = 1;
    public int MaxCapacity { get; set; } = 3;
    public string DatabaseInstanceClass { get; set; } = "db.t3.micro";

    /// <summary>
    ///     Gets production-appropriate sizing for production-class environments
    /// </summary>
    public static ResourceSizing GetProductionSizing()
    {
        return new ResourceSizing
        {
            InstanceType = "t3.medium",
            MinCapacity = 2,
            MaxCapacity = 10,
            DatabaseInstanceClass = "db.t3.small"
        };
    }

    /// <summary>
    ///     Gets development-appropriate sizing for non-production environments
    /// </summary>
    public static ResourceSizing GetDevelopmentSizing()
    {
        return new ResourceSizing
        {
            InstanceType = "t3.small",
            MinCapacity = 1,
            MaxCapacity = 3,
            DatabaseInstanceClass = "db.t3.micro"
        };
    }

    /// <summary>
    ///     Gets sizing appropriate for the environment type
    /// </summary>
    public static ResourceSizing GetSizingForEnvironment(AccountType accountType)
    {
        return accountType == AccountType.Production
            ? GetProductionSizing()
            : GetDevelopmentSizing();
    }
}