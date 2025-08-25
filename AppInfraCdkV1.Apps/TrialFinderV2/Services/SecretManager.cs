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
    /// Get the complete ARN of an existing secret including the unique identifier
    /// </summary>
    /// <param name="secretName">The full name of the secret</param>
    /// <returns>The complete ARN with unique identifier, or null if secret doesn't exist</returns>
    public string? GetSecretCompleteArn(string secretName)
    {
        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = secretsManagerClient.DescribeSecretAsync(describeSecretRequest).GetAwaiter().GetResult();
            return response?.ARN;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex.Message.Contains("AccessDenied") || ex.Message.Contains("not authorized"))
        {
            // Access denied due to IAM policy restrictions
            Console.WriteLine($"          ⚠️  Access denied getting ARN for secret '{secretName}' (likely missing CDKManaged tag): {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"          ⚠️  Error getting ARN for secret '{secretName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate a consistent construct ID that matches the original CDK pattern
    /// This ensures the same logical ID is used across deployments to prevent resource recreation
    /// </summary>
    /// <param name="secretName">The secret name to generate ID for</param>
    /// <returns>A consistent construct ID that matches CDK's original pattern</returns>
    private string GenerateConsistentConstructId(string secretName)
    {
        // Remove hyphens and underscores, capitalize first letter of each word
        // This matches the pattern CDK originally used: SecretManagerSecretopenaioptionsapikey
        var cleanName = secretName.Replace("-", "").Replace("_", "");
        
        // Add the "SecretManagerSecret" prefix that CDK originally used
        return $"SecretManagerSecret{cleanName}";
    }

    /// <summary>
    /// Get or create a secret in Secrets Manager
    /// This method uses AWS SDK to check if secrets exist before creating them.
    /// If a secret exists, it creates a CloudFormation-managed secret with the same name
    /// to ensure proper lifecycle management while preserving existing values.
    /// If a secret doesn't exist, it creates a new secret with generated values.
    /// 
    /// IMPORTANT: This approach ensures that all secrets are properly managed by CloudFormation
    /// and will appear in the CloudFormation resources section, preventing them from disappearing
    /// when the stack is updated or redeployed.
    /// </summary>
    public ISecret GetOrCreateSecret(string secretName, string fullSecretName, CognitoStackOutputs? cognitoOutputs, DeploymentContext context)
    {
        // Check if we already created this secret in this deployment
        if (_createdSecrets.ContainsKey(secretName))
        {
            Console.WriteLine($"          ℹ️  Using existing secret reference for '{secretName}'");
            return _createdSecrets[secretName];
        }

        // Use AWS SDK to check if secret exists and get its current value
        Console.WriteLine($"          🔍 Checking if secret '{fullSecretName}' exists using AWS SDK...");
        
        var (secretExists, secretArn, currentValue) = GetSecretWithValue(fullSecretName);
        
        if (secretExists)
        {
            // Secret exists - create a CloudFormation-managed secret with the same name
            // This ensures proper lifecycle management while preserving existing values
            Console.WriteLine($"          ✅ Found existing secret '{fullSecretName}' - creating CloudFormation-managed secret (preserving existing values)");
            
            // For Cognito secrets, use actual values if available
            if (cognitoOutputs != null && IsCognitoSecret(secretName))
            {
                Console.WriteLine($"          🔐 Creating Cognito secret '{secretName}' with actual Cognito values");
                
                string? actualValue = GetCognitoActualValue(secretName, cognitoOutputs);
                
                if (!string.IsNullOrEmpty(actualValue))
                {
                    Console.WriteLine($"          ✅ Using actual Cognito value for '{secretName}': {actualValue}");
                    
                    var cognitoSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, GenerateConsistentConstructId(secretName), new SecretProps
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
            
            // For existing secrets, create a CloudFormation-managed secret with the same name
            // This will either preserve existing values or create new ones if the secret was deleted
            if (ShouldPreserveExistingValue(secretName, currentValue))
            {
                Console.WriteLine($"          🔄 Preserving existing value for secret '{secretName}'");
            }
            else
            {
                Console.WriteLine($"          🔄 Creating new value for existing secret '{secretName}' (not preserving current value)");
            }
            
            var existingSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, GenerateConsistentConstructId(secretName), new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                SecretStringValue = ShouldPreserveExistingValue(secretName, currentValue)
                    ? SecretValue.UnsafePlainText(currentValue!)
                    : SecretValue.UnsafePlainText($"{{\"secretName\":\"{secretName}\",\"managedBy\":\"CDK\",\"environment\":\"{context.Environment.Name}\",\"existingSecret\":\"true\",\"value\":\"{Guid.NewGuid():N}\"}}")
            });

            // Add the CDKManaged tag required by IAM policy
            Tags.Of(existingSecret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = existingSecret;
            return existingSecret;
        }
        else
        {
            // Secret doesn't exist - create it with generated values or Cognito values
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
                    var cognitoSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, GenerateConsistentConstructId(secretName), new SecretProps
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
            var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, GenerateConsistentConstructId(secretName), new SecretProps
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
            var cognitoSecret = new Amazon.CDK.AWS.SecretsManager.Secret(this, GenerateConsistentConstructId(secretName), new SecretProps
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

    /// <summary>
    /// Get secret information from AWS Secrets Manager using AWS SDK
    /// Returns a tuple with (exists, arn, currentValue) where arn and currentValue are null if secret doesn't exist
    /// </summary>
    private async Task<(bool exists, string? arn, string? currentValue)> GetSecretWithValueAsync(string secretName)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(secretName))
        {
            Console.WriteLine($"          ⚠️  Secret name is null or empty");
            return (false, null, null);
        }

        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            
            Console.WriteLine($"          🔍 Checking secret with name: {secretName}");
            
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = await secretsManagerClient.DescribeSecretAsync(describeSecretRequest);
            
            // Try to get the current secret value
            string? currentValue = null;
            try
            {
                var getSecretValueRequest = new GetSecretValueRequest
                {
                    SecretId = secretName
                };
                
                var secretValueResponse = await secretsManagerClient.GetSecretValueAsync(getSecretValueRequest);
                currentValue = secretValueResponse.SecretString;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"          ⚠️  Could not retrieve current secret value: {ex.Message}");
                // Continue without the current value
            }
            
            return (true, response.ARN, currentValue);
        }
        catch (ResourceNotFoundException)
        {
            return (false, null, null);
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the deployment
            Console.WriteLine($"          ⚠️  Error checking if secret '{secretName}' exists: {ex.Message}");
            Console.WriteLine($"          ℹ️  Assuming secret doesn't exist and will create it");
            return (false, null, null);
        }
    }

    /// <summary>
    /// Get secret information from AWS Secrets Manager using AWS SDK (synchronous wrapper)
    /// </summary>
    private (bool exists, string? arn, string? currentValue) GetSecretWithValue(string secretName)
    {
        // Use GetAwaiter().GetResult() instead of Task.Run().Result to avoid potential deadlocks
        return GetSecretWithValueAsync(secretName).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Determine if we should preserve existing secret values
    /// </summary>
    private bool ShouldPreserveExistingValue(string secretName, string? currentValue)
    {
        // Don't preserve if no current value exists
        if (string.IsNullOrEmpty(currentValue))
            return false;
            
        // Don't preserve Cognito secrets as they should use actual Cognito values
        if (IsCognitoSecret(secretName))
            return false;
            
        // Don't preserve test secrets
        if (secretName.Equals("test-secret", StringComparison.OrdinalIgnoreCase))
            return false;
            
        // Preserve other existing secrets
        return true;
    }
}

/// <summary>
/// Helper class to hold Cognito stack outputs
/// </summary>

