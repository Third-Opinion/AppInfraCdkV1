namespace AppInfraCdkV1.Core.Models;

/// <summary>
///     Represents the type of AWS account for logical grouping of environments
/// </summary>
public enum AccountType
{
    /// <summary>
    ///     Non-production account containing development, testing, and QA environments
    /// </summary>
    NonProduction,

    /// <summary>
    ///     Production account containing staging, pre-production, and production environments
    /// </summary>
    Production,

    /// <summary>
    ///     Sandbox account for experimental or training environments
    /// </summary>
    Sandbox,

    /// <summary>
    ///     Security account for compliance and audit environments
    /// </summary>
    Security,

    /// <summary>
    ///     Shared services account for common resources
    /// </summary>
    SharedServices
}