namespace AppInfraCdkV1.Core.Enums;

/// <summary>
/// IAM purposes for roles, users, and policies
/// </summary>
public enum IamPurpose
{
    /// <summary>
    /// ECS task role for application runtime permissions
    /// </summary>
    EcsTask,
    
    /// <summary>
    /// ECS execution role for container startup and logging
    /// </summary>
    EcsExecution,
    
    /// <summary>
    /// S3 bucket access permissions
    /// </summary>
    S3Access,
    
    /// <summary>
    /// General service account
    /// </summary>
    Service,
    
    /// <summary>
    /// GitHub Actions role for CDK infrastructure deployments
    /// </summary>
    GitHubActionsCdk,
    
    /// <summary>
    /// GitHub Actions role for ECS application deployments
    /// </summary>
    GitHubActionsEcs,
    
    /// <summary>
    /// GitHub Actions OIDC provider for authentication
    /// </summary>
    GitHubActionsOidc
}