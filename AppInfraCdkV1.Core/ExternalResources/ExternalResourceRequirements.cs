using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Base class for defining external resource requirements for CDK stacks
/// </summary>
public abstract class ExternalResourceRequirements
{
    /// <summary>
    /// Gets the list of external resource requirements for the given deployment context
    /// </summary>
    /// <param name="context">Deployment context with environment and application info</param>
    /// <returns>List of external resource requirements</returns>
    public abstract List<ExternalResourceRequirement> GetRequirements(DeploymentContext context);
    
    /// <summary>
    /// Validates all requirements and returns validation results
    /// </summary>
    /// <param name="context">Deployment context</param>
    /// <param name="validator">External resource validator</param>
    /// <returns>Dictionary of requirement descriptions and their validation results</returns>
    public async Task<Dictionary<string, ExternalResourceValidationResult>> ValidateAllAsync(
        DeploymentContext context, 
        IExternalResourceValidator validator)
    {
        var requirements = GetRequirements(context);
        var results = new Dictionary<string, ExternalResourceValidationResult>();
        
        foreach (var requirement in requirements)
        {
            var key = $"{requirement.ResourceType}:{requirement.Purpose}";
            var result = await validator.ValidateResourceAsync(requirement, context);
            results[key] = result;
        }
        
        return results;
    }
    
    /// <summary>
    /// Gets a summary of validation results
    /// </summary>
    /// <param name="validationResults">Results from ValidateAllAsync</param>
    /// <returns>Summary of validation status</returns>
    public ExternalResourceValidationSummary GetValidationSummary(
        Dictionary<string, ExternalResourceValidationResult> validationResults)
    {
        var summary = new ExternalResourceValidationSummary();
        
        foreach (var (resource, result) in validationResults)
        {
            summary.TotalResources++;
            
            if (result.IsValid)
                summary.ValidResources++;
            else
                summary.InvalidResources++;
                
            if (!result.Exists)
                summary.MissingResources++;
                
            if (!result.FollowsNamingConvention)
                summary.InvalidNames++;
                
            summary.AllErrors.AddRange(result.Errors.Select(e => $"{resource}: {e}"));
            summary.AllWarnings.AddRange(result.Warnings.Select(w => $"{resource}: {w}"));
        }
        
        summary.AllValid = summary.InvalidResources == 0;
        
        return summary;
    }
}

/// <summary>
/// Summary of external resource validation results
/// </summary>
public class ExternalResourceValidationSummary
{
    public int TotalResources { get; set; }
    public int ValidResources { get; set; }
    public int InvalidResources { get; set; }
    public int MissingResources { get; set; }
    public int InvalidNames { get; set; }
    public bool AllValid { get; set; }
    public List<string> AllErrors { get; set; } = new();
    public List<string> AllWarnings { get; set; } = new();
}