using AppInfraCdkV1.Core.ExternalResources;
using AppInfraCdkV1.Core.Naming;

namespace AppInfraCdkV1.Core.Models;

public class DeploymentContext
{
    private ResourceNamer? _namer;
    private EnvironmentConfig _environment = new();
    private ApplicationConfig _application = new();
    private string _deploymentId = Guid.NewGuid().ToString("N")[..8];
    private string _deployedBy = "CDK";
    private DateTime _deployedAt = DateTime.UtcNow;
    private Dictionary<string, ExternalResourceValidationResult> _externalResourceValidation = new();

    public EnvironmentConfig Environment
    {
        get => _environment;
        set => _environment = value;
    }

    public ApplicationConfig Application
    {
        get => _application;
        set => _application = value;
    }

    public string DeploymentId
    {
        get => _deploymentId;
        set => _deploymentId = value;
    }

    public string DeployedBy
    {
        get => _deployedBy;
        set => _deployedBy = value;
    }

    public DateTime DeployedAt
    {
        get => _deployedAt;
        set => _deployedAt = value;
    }

    /// <summary>
    ///     Gets the resource namer for this deployment context
    ///     Think of this as your personal naming assistant that knows your project details
    /// </summary>
    public ResourceNamer Namer => _namer ??= new ResourceNamer(this);

    /// <summary>
    /// External resource validation results
    /// </summary>
    public Dictionary<string, ExternalResourceValidationResult> ExternalResourceValidation
    {
        get => _externalResourceValidation;
        set => _externalResourceValidation = value;
    }

    /// <summary>
    /// Whether all external resource validations passed
    /// </summary>
    public bool AllExternalResourcesValid => 
        ExternalResourceValidation.Values.All(v => v.IsValid);

    /// <summary>
    /// Gets a list of all external resource validation errors
    /// </summary>
    public List<string> ExternalResourceErrors =>
        ExternalResourceValidation.Values
            .SelectMany(v => v.Errors)
            .ToList();

    public Dictionary<string, string> GetCommonTags()
    {
        var tags = new Dictionary<string, string>(Environment.Tags)
        {
            ["Environment"] = Environment.Name,
            ["Application"] = Application.Name,
            ["Version"] = Application.Version,
            ["DeploymentId"] = DeploymentId,
            ["DeployedBy"] = DeployedBy,
            ["DeployedAt"] = DeployedAt.ToString("yyyy-MM-dd")
        };

        return tags;
    }

    /// <summary>
    ///     Validates that all naming convention requirements are met
    /// </summary>
    public void ValidateNamingContext()
    {
        try
        {
            NamingConvention.GetEnvironmentPrefix(Environment.Name);
            NamingConvention.GetApplicationCode(Application.Name);
            NamingConvention.GetRegionCode(Environment.Region);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Naming convention validation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates external resource dependencies
    /// </summary>
    /// <param name="requirements">External resource requirements to validate</param>
    /// <param name="validator">External resource validator</param>
    /// <returns>True if all validations pass</returns>
    public async Task<bool> ValidateExternalDependenciesAsync(
        ExternalResourceRequirements requirements,
        IExternalResourceValidator validator)
    {
        Console.WriteLine("🔍 Validating external resource dependencies...");
        
        ExternalResourceValidation = await requirements.ValidateAllAsync(this, validator);
        var summary = requirements.GetValidationSummary(ExternalResourceValidation);
        
        Console.WriteLine($"📊 External Resource Validation Summary:");
        Console.WriteLine($"   Total Resources: {summary.TotalResources}");
        Console.WriteLine($"   Valid Resources: {summary.ValidResources}");
        Console.WriteLine($"   Invalid Resources: {summary.InvalidResources}");
        Console.WriteLine($"   Missing Resources: {summary.MissingResources}");
        
        if (summary.AllErrors.Any())
        {
            Console.WriteLine("❌ External Resource Errors:");
            foreach (var error in summary.AllErrors)
            {
                Console.WriteLine($"   {error}");
            }
        }
        
        if (summary.AllWarnings.Any())
        {
            Console.WriteLine("⚠️  External Resource Warnings:");
            foreach (var warning in summary.AllWarnings)
            {
                Console.WriteLine($"   {warning}");
            }
        }
        
        if (summary.AllValid)
        {
            Console.WriteLine("✅ All external resource dependencies validated successfully");
        }
        else
        {
            Console.WriteLine("❌ External resource validation failed");
        }
        
        return summary.AllValid;
    }
}