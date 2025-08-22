using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.AWS.IAM;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using Constructs;
using CognitoStackOutputs = AppInfraCdkV1.Apps.TrialFinderV2.TrialFinderV2EcsStack.CognitoStackOutputs;
using System.Linq;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Services;

/// <summary>
/// Manages AWS Secrets Manager operations for the TrialFinderV2 application
/// 
/// This service handles:
/// - Secret existence checking using AWS SDK
/// - Secret creation with generated values
/// - Secret import for existing secrets
/// - Cognito secret integration
/// - Secret name mapping and validation
/// - Secret ARN export
/// </summary>
public class SecretManager : Construct
{
    private readonly DeploymentContext _context;
    private readonly Dictionary<string, ISecret> _createdSecrets = new();
    private readonly Dictionary<string, string> _envVarToSecretNameMapping = new();

    public SecretManager(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Build secret name mapping for environment variables
    /// </summary>
    public void BuildSecretNameMapping(List<string> secretNames)
    {
        foreach (var secretName in secretNames)
        {
            // Only add if not already present to avoid overwriting
            if (!_envVarToSecretNameMapping.ContainsKey(secretName))
            {
                // Convert the secret name to a valid AWS Secrets Manager name
                // Replace __ with - and convert to lowercase
                var mappedSecretName = secretName.Replace("__", "-").ToLowerInvariant();
                _envVarToSecretNameMapping[secretName] = mappedSecretName;
            }
        }
    }

    /// <summary>
    /// Get secret name from environment variable
    /// </summary>
    public string GetSecretNameFromEnvVar(string secretName)
    {
        // Use the dynamically built mapping
        if (_envVarToSecretNameMapping.TryGetValue(secretName, out var mappedSecretName))
        {
            return mappedSecretName;
        }
        
        // Fallback: convert the environment variable name to a valid secret name
        // Replace __ with - and convert to lowercase
        return secretName.ToLowerInvariant().Replace("__", "-");
    }

    /// <summary>
    /// Build full secret name with environment and application prefix
    /// </summary>
    public string BuildSecretName(string secretName, DeploymentContext context)
    {
        var environmentPrefix = context.Environment.Name.ToLowerInvariant();
        var applicationName = context.Application.Name.ToLowerInvariant();
        return $"/{environmentPrefix}/{applicationName}/{secretName}";
    }

    /// <summary>
    /// Check if a secret exists in Secrets Manager using AWS SDK
    /// This method performs a synchronous check to determine if a secret already exists
    /// before attempting to create it, preventing accidental overwrites.
    /// </summary>
    /// <param name="secretName">The full name of the secret to check</param>
    /// <returns>True if the secret exists, false otherwise</returns>
    public async Task<bool> SecretExistsAsync(string secretName)
    {
        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = await secretsManagerClient.DescribeSecretAsync(describeSecretRequest);
            return response != null;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("AccessDenied") || ex.Message.Contains("not authorized"))
        {
            // Access denied due to IAM policy restrictions (e.g., missing CDKManaged tag)
            // Treat this as "secret doesn't exist" to allow creation to proceed
            Console.WriteLine($"          ⚠️  Access denied checking secret '{secretName}' (likely missing CDKManaged tag): {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it with proper tags");
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if secret '{secretName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it");
            return false;
        }
    }

    /// <summary>
    /// Check if a secret exists in Secrets Manager using AWS SDK (synchronous version)
    /// This method performs a synchronous check to determine if a secret already exists
    /// before attempting to create it, preventing accidental overwrites.
    /// </summary>
    /// <param name="secretName">The full name of the secret to check</param>
    /// <returns>True if the secret exists, false otherwise</returns>
    public bool SecretExists(string secretName)
    {
        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = secretsManagerClient.DescribeSecretAsync(describeSecretRequest).GetAwaiter().GetResult();
            return response != null;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("AccessDenied") || ex.Message.Contains("not authorized"))
        {
            // Access denied due to IAM policy restrictions (e.g., missing CDKManaged tag)
            // Treat this as "secret doesn't exist" to allow creation to proceed
            Console.WriteLine($"          ⚠️  Access denied checking secret '{secretName}' (likely missing CDKManaged tag): {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it with proper tags");
            return false;
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if secret '{secretName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it");
            return false;
        }
    }

    /// <summary>
    /// Get or create a secret in Secrets Manager
    /// This method uses AWS SDK to check if secrets exist before creating them.
    /// If a secret exists, it imports the existing secret reference to preserve manual values.
    /// If a secret doesn't exist, it creates a new secret with generated values.
    /// </summary>
    public ISecret GetOrCreateSecret(string secretName, string fullSecretName, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        // Use AWS SDK to check if secret exists
        Console.WriteLine($"          🔍 Checking if secret '{fullSecretName}' exists using AWS SDK...");
        
        if (SecretExists(fullSecretName))
        {
            // Secret exists - import it to preserve manual values
            Console.WriteLine($"          ✅ Found existing secret '{fullSecretName}' - importing reference (preserving manual values)");
            var existingSecret = Amazon.CDK.AWS.SecretsManager.Secret.FromSecretNameV2(this, $"ImportedSecret-{secretName}", fullSecretName);
            
            // Add the CDKManaged tag to existing secrets to ensure IAM policy compliance
            Tags.Of(existingSecret).Add("CDKManaged", "true");
            
            _createdSecrets[secretName] = existingSecret;
            return existingSecret;
        }
        else
        {
            // Secret doesn't exist - create it with generated values
            Console.WriteLine($"          ✨ Creating new secret '{fullSecretName}' with generated values");
            
            // For Cognito secrets, create them with actual values if available
            if (cognitoOutputs != null && IsCognitoSecret(secretName))
            {
                Console.WriteLine($"          🔐 Creating Cognito secret '{secretName}' with actual Cognito values");
                
                // Determine the actual value based on secret name
                string? actualValue = GetCognitoActualValue(secretName, cognitoOutputs);
                
                if (!string.IsNullOrEmpty(actualValue))
                {
                    Console.WriteLine($"          ✅ Using actual Cognito value for '{secretName}': {actualValue}");
                    
                    // Create secret with actual Cognito value
                    var cognitoSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
                    {
                        SecretName = fullSecretName,
                        Description = $"Secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                        SecretStringValue = SecretValue.UnsafePlainText(actualValue)
                    });

                    // Add the CDKManaged tag required by IAM policy
                    Tags.Of(cognitoSecret).Add("CDKManaged", "true");

                    _createdSecrets[secretName] = cognitoSecret;
                    return cognitoSecret;
                }
                else
                {
                    Console.WriteLine($"          ⚠️  Could not determine actual value for Cognito secret '{secretName}', using generated value");
                }
            }
            
            // Regular secret with generated values (fallback for Cognito secrets without actual values)
            var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                GenerateSecretString = new SecretStringGenerator
                {
                    SecretStringTemplate = $"{{\"secretName\":\"{secretName}\",\"managedBy\":\"CDK\",\"environment\":\"{context.Environment.Name}\"}}",
                    GenerateStringKey = "value",
                    PasswordLength = 32,
                    ExcludeCharacters = "\"@/\\"
                }
            });

            // Add the CDKManaged tag required by IAM policy
            Tags.Of(secret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = secret;
            return secret;
        }
    }

    /// <summary>
    /// Get actual Cognito value based on secret name and Cognito outputs
    /// </summary>
    private string? GetCognitoActualValue(string secretName, CognitoStackOutputs cognitoOutputs)
    {
        return secretName.ToLowerInvariant() switch
        {
            "cognito-clientid" => cognitoOutputs.AppClientId,
            "cognito-clientsecret" => cognitoOutputs.AppClientSecret, // Now exposed in outputs
            "cognito-userpoolid" => cognitoOutputs.UserPoolId,
            "cognito-domain" => cognitoOutputs.DomainUrl, //use url instead of domain name
            "cognito-domainurl" => cognitoOutputs.DomainUrl,
            "cognito-userpoolarn" => cognitoOutputs.UserPoolArn,
            _ => null
        };
    }

    /// <summary>
    /// Check if a secret name corresponds to a Cognito secret
    /// </summary>
    private bool IsCognitoSecret(string secretName)
    {
        var cognitoSecretNames = new[]
        {
            "cognito-clientid",
            "cognito-clientsecret", 
            "cognito-userpoolid",
            "cognito-domainurl",
            "cognito-domain",
            "cognito-userpoolarn"
        };
        
        return cognitoSecretNames.Contains(secretName.ToLowerInvariant());
    }

    /// <summary>
    /// Create Cognito-specific secret with actual values
    /// </summary>
    public ISecret CreateCognitoSecret(string secretName, string fullSecretName, CognitoStackOutputs cognitoOutputs, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing Cognito secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        Console.WriteLine($"          🔐 Creating Cognito secret '{secretName}' with actual Cognito values");
        
        // Determine the actual value based on secret name
        string? actualValue = GetCognitoActualValue(secretName, cognitoOutputs);
        
        if (!string.IsNullOrEmpty(actualValue))
        {
            Console.WriteLine($"          ✅ Using actual Cognito value for '{secretName}': {actualValue}");
            
            // Create secret with actual Cognito value
            var cognitoSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Cognito secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                SecretStringValue = SecretValue.UnsafePlainText(actualValue)
            });

            // Add the CDKManaged tag required by IAM policy
            Tags.Of(cognitoSecret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = cognitoSecret;
            return cognitoSecret;
        }
        else
        {
            Console.WriteLine($"          ⚠️  Could not determine actual value for Cognito secret '{secretName}', using generated value");
            
            // Fallback to regular secret creation
            return GetOrCreateSecret(secretName, fullSecretName, cognitoOutputs, context);
        }
    }

    /// <summary>
    /// Export secret ARNs for all created secrets
    /// </summary>
    public void ExportSecretArns()
    {
        foreach (var (secretName, secret) in _createdSecrets)
        {
            var exportName = $"{_context.Environment.Name}-{_context.Application.Name}-{secretName}-secret-arn";
            new CfnOutput(this, $"SecretArn-{secretName}", new CfnOutputProps
            {
                Value = secret.SecretArn,
                Description = $"ARN for secret '{secretName}'",
                ExportName = exportName
            });
        }
    }

    /// <summary>
    /// Get all created secrets for external access
    /// </summary>
    public Dictionary<string, ISecret> GetCreatedSecrets()
    {
        return _createdSecrets;
    }

    /// <summary>
    /// Get container secrets from Secrets Manager for ECS task definitions
    /// </summary>
    public Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();
        
        if (secretNames?.Count > 0)
        {
            Console.WriteLine($"     🔐 Processing {secretNames.Count} secret(s):");
            foreach (var envVarName in secretNames)
            {
                // Use the mapping to get the secret name, or fall back to the original name
                var secretName = GetSecretNameFromEnvVar(envVarName);
                
                var fullSecretName = BuildSecretName(secretName, context);
                var secret = GetOrCreateSecret(secretName, fullSecretName, cognitoOutputs, context);
                
                // Use the original environment variable name from the configuration
                secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
                
                Console.WriteLine($"        - Environment variable '{envVarName}' -> Secret '{secretName}'");
                Console.WriteLine($"          Full secret path: {secret.SecretArn}");
            }
        }
        
        return secrets;
    }
}

/// <summary>
/// Helper class to hold Cognito stack outputs
/// </summary>

