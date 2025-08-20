using AppInfraCdkV1.Core.Enums;

namespace AppInfraCdkV1.Core.Extensions;

/// <summary>
/// Extension methods for converting purpose enums to string values used in naming
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Converts ResourcePurpose to string value for naming
    /// </summary>
    public static string ToStringValue(this ResourcePurpose purpose) => purpose switch
    {
        ResourcePurpose.Main => "main",
        ResourcePurpose.Web => "web",
        ResourcePurpose.Api => "api",
        ResourcePurpose.Internal => "internal",
        ResourcePurpose.Primary => "primary",
        ResourcePurpose.Admin => "admin",
        ResourcePurpose.Auth => "auth",
        ResourcePurpose.PublicWebsite => "public-website",
        ResourcePurpose.JwkGenerator => "jwk-generator",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    /// <summary>
    /// Converts StoragePurpose to string value for naming
    /// </summary>
    public static string ToStringValue(this StoragePurpose purpose) => purpose switch
    {
        StoragePurpose.App => "app",
        StoragePurpose.Documents => "docs",
        StoragePurpose.Uploads => "uploads",
        StoragePurpose.Backups => "backups",
        StoragePurpose.Cache => "cache",
        StoragePurpose.Session => "session",
        StoragePurpose.PublicWebsite => "public-website",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    /// <summary>
    /// Converts IamPurpose to string value for naming
    /// </summary>
    public static string ToStringValue(this IamPurpose purpose) => purpose switch
    {
        IamPurpose.EcsTask => "ecs-task",
        IamPurpose.EcsExecution => "ecs-exec",
        IamPurpose.S3Access => "s3-access",
        IamPurpose.Service => "service",
        IamPurpose.GithubActionsDeploy => "github-actions-deploy",
        IamPurpose.LambdaExecution => "lambda-exec",
        IamPurpose.SecretsAccess => "secrets-access",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    /// <summary>
    /// Converts QueuePurpose to string value for naming
    /// </summary>
    public static string ToStringValue(this QueuePurpose purpose) => purpose switch
    {
        QueuePurpose.Processing => "processing",
        QueuePurpose.DeadLetter => "dlq",
        QueuePurpose.Urgent => "urgent",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    /// <summary>
    /// Converts NotificationPurpose to string value for naming
    /// </summary>
    public static string ToStringValue(this NotificationPurpose purpose) => purpose switch
    {
        NotificationPurpose.General => "notifications",
        NotificationPurpose.TrialUpdates => "trial-updates",
        NotificationPurpose.SystemAlerts => "alerts",
        NotificationPurpose.UserNotifications => "user-notify",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };
}