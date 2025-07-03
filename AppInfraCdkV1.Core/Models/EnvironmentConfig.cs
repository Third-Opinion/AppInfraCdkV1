using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

public class EnvironmentConfig
{
    public string Name { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public AccountType AccountType { get; set; } = AccountType.NonProduction;
    public Dictionary<string, string> Tags { get; set; } = new();
    public EnvironmentIsolationStrategy IsolationStrategy { get; set; } = new();

    /// <summary>
    ///     Indicates if this is a production-class environment (staging, production, etc.)
    /// </summary>
    public bool IsProductionClass => AccountType == AccountType.Production;

    /// <summary>
    ///     Legacy property for backward compatibility - maps to IsProductionClass
    /// </summary>
    public bool IsProd => IsProductionClass;

    public Amazon.CDK.Environment ToAwsEnvironment()
    {
        return new()
        {
            Account = AccountId,
            Region = Region
        };
    }

    /// <summary>
    ///     Gets other environments that share this account
    /// </summary>
    public List<string> GetAccountSiblingEnvironments()
    {
        return NamingConvention.GetEnvironmentsInSameAccount(Name);
    }
}