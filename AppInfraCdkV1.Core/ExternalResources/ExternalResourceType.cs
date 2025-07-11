namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Types of external resources that CDK can depend on
/// </summary>
public enum ExternalResourceType
{
    /// <summary>
    /// IAM Role
    /// </summary>
    IamRole,
    
    /// <summary>
    /// IAM Policy
    /// </summary>
    IamPolicy,
    
    /// <summary>
    /// IAM User
    /// </summary>
    IamUser,
    
    /// <summary>
    /// KMS Key
    /// </summary>
    KmsKey,
    
    /// <summary>
    /// S3 Bucket (existing)
    /// </summary>
    S3Bucket,
    
    /// <summary>
    /// VPC (existing)
    /// </summary>
    Vpc,
    
    /// <summary>
    /// Security Group (existing)
    /// </summary>
    SecurityGroup
}