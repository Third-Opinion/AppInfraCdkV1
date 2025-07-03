namespace AppInfraCdkV1.Core.Models;

public class SecurityConfig
{
    private List<string> _allowedCidrBlocks = new();
    private bool _enableWaf = true;
    private string _certificateArn = string.Empty;
    private CrossEnvironmentSecurityConfig _crossEnvironmentSecurity = new();

    public List<string> AllowedCidrBlocks
    {
        get => _allowedCidrBlocks;
        set => _allowedCidrBlocks = value;
    }

    public bool EnableWaf
    {
        get => _enableWaf;
        set => _enableWaf = value;
    }

    public string CertificateArn
    {
        get => _certificateArn;
        set => _certificateArn = value;
    }

    /// <summary>
    ///     Cross-environment security rules
    /// </summary>
    public CrossEnvironmentSecurityConfig CrossEnvironmentSecurity
    {
        get => _crossEnvironmentSecurity;
        set => _crossEnvironmentSecurity = value;
    }

    /// <summary>
    ///     Gets security configuration appropriate for the account type
    /// </summary>
    public static SecurityConfig GetSecurityConfigForAccountType(AccountType accountType)
    {
        return accountType == AccountType.Production
            ? GetProductionSecurityConfig()
            : GetDevelopmentSecurityConfig();
    }

    private static SecurityConfig GetProductionSecurityConfig()
    {
        return new SecurityConfig
        {
            EnableWaf = true,
            AllowedCidrBlocks = new List<string>(), // Restrictive by default
            CrossEnvironmentSecurity = new CrossEnvironmentSecurityConfig
            {
                AllowCrossEnvironmentAccess = false,
                RequireEncryptionInTransit = true,
                RequireEncryptionAtRest = true
            }
        };
    }

    private static SecurityConfig GetDevelopmentSecurityConfig()
    {
        return new SecurityConfig
        {
            EnableWaf = false,
            AllowedCidrBlocks = new List<string>
                { "10.0.0.0/8" }, // More permissive for development
            CrossEnvironmentSecurity = new CrossEnvironmentSecurityConfig
            {
                AllowCrossEnvironmentAccess = true,
                RequireEncryptionInTransit = false,
                RequireEncryptionAtRest = false
            }
        };
    }
}