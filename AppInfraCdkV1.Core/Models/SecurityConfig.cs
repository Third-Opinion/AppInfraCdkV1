namespace AppInfraCdkV1.Core.Models;

public class SecurityConfig
{
    public List<string> AllowedCidrBlocks { get; set; } = new();
    public bool EnableWaf { get; set; } = true;
    public string CertificateArn { get; set; } = string.Empty;

    /// <summary>
    ///     Cross-environment security rules
    /// </summary>
    public CrossEnvironmentSecurityConfig CrossEnvironmentSecurity { get; set; } = new();

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