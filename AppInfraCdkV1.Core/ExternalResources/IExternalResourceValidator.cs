using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Interface for validating external resources
/// </summary>
public interface IExternalResourceValidator
{
    /// <summary>
    /// Validates that an external resource meets the specified requirements
    /// </summary>
    /// <param name="requirement">The resource requirement to validate</param>
    /// <param name="context">Deployment context for naming and environment info</param>
    /// <returns>Validation result</returns>
    Task<ExternalResourceValidationResult> ValidateResourceAsync(
        ExternalResourceRequirement requirement, 
        DeploymentContext context);
    
    /// <summary>
    /// Validates multiple external resource requirements
    /// </summary>
    /// <param name="requirements">List of requirements to validate</param>
    /// <param name="context">Deployment context</param>
    /// <returns>List of validation results</returns>
    Task<List<ExternalResourceValidationResult>> ValidateResourcesAsync(
        List<ExternalResourceRequirement> requirements, 
        DeploymentContext context);
    
    /// <summary>
    /// Checks if an IAM role exists and meets requirements
    /// </summary>
    /// <param name="roleName">Name of the role to check</param>
    /// <param name="requirements">Validation requirements</param>
    /// <returns>External IAM role with validation results</returns>
    Task<IExternalIamRole> ValidateIamRoleAsync(string roleName, List<string> requirements);
    
    /// <summary>
    /// Generates AWS CLI commands to create missing resources
    /// </summary>
    /// <param name="requirement">The requirement for the missing resource</param>
    /// <param name="context">Deployment context</param>
    /// <returns>AWS CLI commands to create the resource</returns>
    string GenerateCreationCommands(ExternalResourceRequirement requirement, DeploymentContext context);
}