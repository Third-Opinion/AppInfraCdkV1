using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

public class EnvironmentConfig
{
    private string _name = string.Empty;
    private string _accountId = string.Empty;
    private string _region = string.Empty;
    private AccountType _accountType = AccountType.NonProduction;
    private Dictionary<string, string> _tags = new();

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public string AccountId
    {
        get => _accountId;
        set => _accountId = value;
    }

    public string Region
    {
        get => _region;
        set => _region = value;
    }

    public AccountType AccountType
    {
        get => _accountType;
        set => _accountType = value;
    }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        set => _tags = value;
    }


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