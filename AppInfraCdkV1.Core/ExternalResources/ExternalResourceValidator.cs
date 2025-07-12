using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Extensions;
using AppInfraCdkV1.Core.Models;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Validates external resources that CDK depends on
/// </summary>
public class ExternalResourceValidator : IExternalResourceValidator
{
    private readonly IAmazonIdentityManagementService? _iamClient;
    
    public ExternalResourceValidator(IAmazonIdentityManagementService? iamClient = null)
    {
        _iamClient = iamClient;
    }

    public async Task<ExternalResourceValidationResult> ValidateResourceAsync(
        ExternalResourceRequirement requirement, 
        DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(context);
        
        var result = new ExternalResourceValidationResult();
        
        try
        {
            switch (requirement.ResourceType)
            {
                case ExternalResourceType.IamRole:
                    return await ValidateIamRoleRequirementAsync(requirement, context);
                    
                case ExternalResourceType.IamPolicy:
                    return await ValidateIamPolicyRequirementAsync(requirement, context);
                    
                default:
                    result.Errors.Add($"Validation for resource type {requirement.ResourceType} not implemented");
                    result.IsValid = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error validating resource: {ex.Message}");
            result.IsValid = false;
        }
        
        return result;
    }

    public async Task<List<ExternalResourceValidationResult>> ValidateResourcesAsync(
        List<ExternalResourceRequirement> requirements, 
        DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(context);
        
        var results = new List<ExternalResourceValidationResult>();
        
        foreach (var requirement in requirements)
        {
            var result = await ValidateResourceAsync(requirement, context);
            results.Add(result);
        }
        
        return results;
    }

    public async Task<IExternalIamRole> ValidateIamRoleAsync(string roleName, List<string> requirements)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        ArgumentNullException.ThrowIfNull(requirements);
        
        var externalRole = new ExternalIamRole
        {
            RoleName = roleName,
            Name = roleName,
            ValidationResult = new ExternalResourceValidationResult()
        };

        if (_iamClient == null)
        {
            // Mock validation for testing/development
            externalRole.ValidationResult.Errors.Add("IAM client not configured - using mock validation");
            externalRole.ValidationResult.IsValid = false;
            return externalRole;
        }

        try
        {
            // Check if role exists
            var roleResponse = await _iamClient.GetRoleAsync(new GetRoleRequest { RoleName = roleName });
            externalRole.Exists = true;
            externalRole.Arn = roleResponse.Role.Arn;
            externalRole.ValidationResult.Exists = true;

            // Parse trust policy to check trusted services
            var trustPolicy = roleResponse.Role.AssumeRolePolicyDocument;
            externalRole.CanAssumeEcsTasks = trustPolicy?.Contains("ecs-tasks.amazonaws.com") ?? false;

            // Check attached policies
            var attachedPoliciesResponse = await _iamClient.ListAttachedRolePoliciesAsync(
                new ListAttachedRolePoliciesRequest { RoleName = roleName });
            
            externalRole.ManagedPolicyArns = attachedPoliciesResponse.AttachedPolicies
                .Select(p => p.PolicyArn)
                .ToList();
            
            externalRole.HasEcsExecutionPolicy = externalRole.ManagedPolicyArns
                .Any(arn => arn.Contains("AmazonECSTaskExecutionRolePolicy"));

            // Validate against requirements
            foreach (var requirement in requirements)
            {
                ValidateRoleRequirement(externalRole, requirement);
            }

            externalRole.ValidationResult.IsValid = !externalRole.ValidationResult.Errors.Any();
        }
        catch (NoSuchEntityException)
        {
            externalRole.Exists = false;
            externalRole.ValidationResult.Exists = false;
            externalRole.ValidationResult.Errors.Add($"IAM role '{roleName}' does not exist");
            externalRole.ValidationResult.IsValid = false;
        }
        catch (Exception ex)
        {
            externalRole.ValidationResult.Errors.Add($"Error validating IAM role: {ex.Message}");
            externalRole.ValidationResult.IsValid = false;
        }

        return externalRole;
    }

    public string GenerateCreationCommands(ExternalResourceRequirement requirement, DeploymentContext context)
    {
        return requirement.ResourceType switch
        {
            ExternalResourceType.IamRole => GenerateIamRoleCreationCommands(requirement, context),
            ExternalResourceType.IamPolicy => GenerateIamPolicyCreationCommands(requirement, context),
            _ => $"# Creation commands for {requirement.ResourceType} not implemented"
        };
    }

    private async Task<ExternalResourceValidationResult> ValidateIamRoleRequirementAsync(
        ExternalResourceRequirement requirement, 
        DeploymentContext context)
    {
        var result = new ExternalResourceValidationResult();
        
        if (requirement.Purpose is not IamPurpose purpose)
        {
            result.Errors.Add("IAM role requirement must have IamPurpose");
            result.IsValid = false;
            return result;
        }

        var expectedName = context.Namer.IamRole(purpose);
        var roleValidation = await ValidateIamRoleAsync(expectedName, requirement.ValidationRules);
        
        result.Exists = roleValidation.Exists;
        result.FollowsNamingConvention = roleValidation.RoleName == expectedName;
        result.HasRequiredPermissions = roleValidation.ValidationResult.IsValid;
        result.Errors.AddRange(roleValidation.ValidationResult.Errors);
        result.Warnings.AddRange(roleValidation.ValidationResult.Warnings);
        result.IsValid = result.Exists && result.FollowsNamingConvention && result.HasRequiredPermissions;
        
        return result;
    }

    private Task<ExternalResourceValidationResult> ValidateIamPolicyRequirementAsync(
        ExternalResourceRequirement requirement, 
        DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(context);
        
        // Placeholder for IAM policy validation
        var result = new ExternalResourceValidationResult();
        result.Errors.Add("IAM policy validation not implemented yet");
        result.IsValid = false;
        return Task.FromResult(result);
    }

    private void ValidateRoleRequirement(ExternalIamRole role, string requirement)
    {
        switch (requirement.ToLower())
        {
            case "canassumeecstasks":
                if (!role.CanAssumeEcsTasks)
                    role.ValidationResult.Errors.Add("Role cannot assume ECS tasks");
                break;
                
            case "hasecsexecutionpolicy":
                if (!role.HasEcsExecutionPolicy)
                    role.ValidationResult.Errors.Add("Role missing ECS execution policy");
                break;
                
            case "hass3access":
                // Would need to check specific S3 permissions
                role.ValidationResult.Warnings.Add("S3 access validation not fully implemented");
                break;
                
            default:
                role.ValidationResult.Warnings.Add($"Unknown validation requirement: {requirement}");
                break;
        }
    }

    private string GenerateIamRoleCreationCommands(ExternalResourceRequirement requirement, DeploymentContext context)
    {
        if (requirement.Purpose is not IamPurpose purpose)
            return "# Invalid IAM purpose for role creation";

        var roleName = context.Namer.IamRole(purpose);
        var commands = new List<string>
        {
            "# Create IAM role for " + purpose.ToStringValue(),
            $"aws iam create-role --role-name {roleName} \\",
            "  --assume-role-policy-document '{",
            "    \"Version\": \"2012-10-17\",",
            "    \"Statement\": [",
            "      {",
            "        \"Effect\": \"Allow\",",
            "        \"Principal\": { \"Service\": \"ecs-tasks.amazonaws.com\" },",
            "        \"Action\": \"sts:AssumeRole\"",
            "      }",
            "    ]",
            "  }'"
        };

        // Add managed policies based on purpose
        if (purpose == IamPurpose.EcsExecution)
        {
            commands.Add("");
            commands.Add($"aws iam attach-role-policy --role-name {roleName} \\");
            commands.Add("  --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy");
        }

        return string.Join(Environment.NewLine, commands);
    }

    private string GenerateIamPolicyCreationCommands(ExternalResourceRequirement requirement, DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(context);
        
        return "# IAM policy creation commands not implemented yet";
    }
}