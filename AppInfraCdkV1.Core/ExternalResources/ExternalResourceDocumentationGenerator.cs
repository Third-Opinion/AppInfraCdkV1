using System.Text;
using AppInfraCdkV1.Core.Models;

namespace AppInfraCdkV1.Core.ExternalResources;

/// <summary>
/// Generates documentation for external resource requirements
/// </summary>
public class ExternalResourceDocumentationGenerator
{
    /// <summary>
    /// Generates markdown documentation for external resource requirements
    /// </summary>
    /// <param name="requirements">External resource requirements</param>
    /// <param name="context">Deployment context</param>
    /// <returns>Markdown documentation</returns>
    public string GenerateMarkdownDocumentation(
        ExternalResourceRequirements requirements, 
        DeploymentContext context)
    {
        var sb = new StringBuilder();
        var requirementsList = requirements.GetRequirements(context);
        
        sb.AppendLine($"# External Resource Requirements for {context.Application.Name}");
        sb.AppendLine();
        sb.AppendLine($"**Environment:** {context.Environment.Name}");
        sb.AppendLine($"**Account ID:** {context.Environment.AccountId}");
        sb.AppendLine($"**Region:** {context.Environment.Region}");
        sb.AppendLine();
        
        sb.AppendLine("## Overview");
        sb.AppendLine($"This document lists the external resources that must exist before deploying {context.Application.Name} to the {context.Environment.Name} environment.");
        sb.AppendLine();
        
        sb.AppendLine($"**Total Required Resources:** {requirementsList.Count}");
        sb.AppendLine();
        
        // Group by resource type
        var groupedRequirements = requirementsList.GroupBy(r => r.ResourceType);
        
        foreach (var group in groupedRequirements)
        {
            sb.AppendLine($"## {group.Key} Resources");
            sb.AppendLine();
            
            foreach (var requirement in group)
            {
                sb.AppendLine($"### {requirement.ExpectedName}");
                sb.AppendLine();
                sb.AppendLine($"**Description:** {requirement.Description}");
                sb.AppendLine($"**Required:** {(requirement.IsRequired ? "Yes" : "No")}");
                sb.AppendLine($"**Expected ARN:** `{requirement.ExpectedArn}`");
                sb.AppendLine();
                
                if (requirement.ValidationRules.Any())
                {
                    sb.AppendLine("**Validation Rules:**");
                    foreach (var rule in requirement.ValidationRules)
                    {
                        sb.AppendLine($"- {rule}");
                    }
                    sb.AppendLine();
                }
                
                if (requirement.ExpectedTags.Any())
                {
                    sb.AppendLine("**Expected Tags:**");
                    foreach (var tag in requirement.ExpectedTags)
                    {
                        sb.AppendLine($"- {tag.Key}: {tag.Value}");
                    }
                    sb.AppendLine();
                }
                
                // Generate creation commands
                sb.AppendLine("**Creation Commands:**");
                sb.AppendLine("```bash");
                var validator = new ExternalResourceValidator();
                var commands = validator.GenerateCreationCommands(requirement, context);
                sb.AppendLine(commands);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("## Validation");
        sb.AppendLine("Before deploying, validate that all external resources exist and are properly configured:");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        sb.AppendLine("var validator = new ExternalResourceValidator();");
        sb.AppendLine("var requirements = new TrialFinderV2ExternalDependencies();");
        sb.AppendLine("var results = await context.ValidateExternalDependenciesAsync(requirements, validator);");
        sb.AppendLine("```");
        sb.AppendLine();
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Generates a checklist for operations teams
    /// </summary>
    /// <param name="requirements">External resource requirements</param>
    /// <param name="context">Deployment context</param>
    /// <returns>Checklist in markdown format</returns>
    public string GenerateOperationsChecklist(
        ExternalResourceRequirements requirements, 
        DeploymentContext context)
    {
        var sb = new StringBuilder();
        var requirementsList = requirements.GetRequirements(context);
        
        sb.AppendLine($"# Pre-Deployment Checklist: {context.Application.Name} ({context.Environment.Name})");
        sb.AppendLine();
        sb.AppendLine("Complete this checklist before deploying the CDK stack.");
        sb.AppendLine();
        
        foreach (var requirement in requirementsList)
        {
            sb.AppendLine($"- [ ] **{requirement.ResourceType}**: {requirement.ExpectedName}");
            sb.AppendLine($"  - ARN: `{requirement.ExpectedArn}`");
            sb.AppendLine($"  - Purpose: {requirement.Description}");
            
            if (requirement.ValidationRules.Any())
            {
                sb.AppendLine("  - Validation:");
                foreach (var rule in requirement.ValidationRules)
                {
                    sb.AppendLine($"    - [ ] {rule}");
                }
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
}