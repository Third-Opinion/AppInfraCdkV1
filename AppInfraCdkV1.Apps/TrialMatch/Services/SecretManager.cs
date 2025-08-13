using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Services;

/// <summary>
/// Manages AWS Secrets Manager operations for the TrialMatch application
/// 
/// This service handles:
/// - Secret existence checking using AWS SDK
/// - Secret creation with generated values
/// - Secret import for existing secrets
/// - Multi-value secret handling
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
    /// Check if secret exists using AWS SDK
    /// </summary>
    public bool SecretExists(string secretName)
    {
        try
        {
            using var secretsManagerClient = new AmazonSecretsManagerClient();
            var describeSecretRequest = new DescribeSecretRequest
            {
                SecretId = secretName
            };
            
            var response = secretsManagerClient.DescribeSecretAsync(describeSecretRequest).Result;
            return response != null;
        }
        catch (ResourceNotFoundException)
        {
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
    public ISecret GetOrCreateSecret(string secretName, string fullSecretName, DeploymentContext context)
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
            
            // Regular secret with generated values
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
    /// Get or create a multi-value secret in Secrets Manager
    /// This method is specifically for handling secrets that contain multiple key-value pairs,
    /// like the tm-frontend-env-vars secret.
    /// </summary>
    public ISecret GetOrCreateMultiValueSecret(string secretName, string fullSecretName, DeploymentContext context)
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
            // Secret doesn't exist - create it with the proper structure for frontend environment variables
            Console.WriteLine($"          ✨ Creating new multi-value secret '{fullSecretName}' with frontend environment variables");
            
            // Create the JSON structure for frontend environment variables
            var frontendEnvVarsJson = $@"{{
  ""NEXT_PUBLIC_COGNITO_USER_POOL_ID"": ""your_value"",
  ""NEXT_PUBLIC_COGNITO_CLIENT_ID"": ""your_value"", 
  ""NEXT_PUBLIC_COGNITO_CLIENT_SECRET"": ""your_value"",
  ""NEXT_PUBLIC_COGNITO_DOMAIN"": ""your_value"",
  ""NEXT_PUBLIC_API_URL"": ""your_value"",
  ""NEXT_PUBLIC_API_MODE"": ""your_value""
}}";
            
            // Multi-value secret with the proper JSON structure
            var secret = new Amazon.CDK.AWS.SecretsManager.Secret(this, $"Secret-{secretName}", new SecretProps
            {
                SecretName = fullSecretName,
                Description = $"Multi-value secret '{secretName}' for {context.Application.Name} in {context.Environment.Name}",
                GenerateSecretString = new SecretStringGenerator
                {
                    SecretStringTemplate = frontendEnvVarsJson,
                    GenerateStringKey = "value"
                }
            });

            // Add the CDKManaged tag required by IAM policy
            Tags.Of(secret).Add("CDKManaged", "true");

            _createdSecrets[secretName] = secret;
            return secret;
        }
    }

    /// <summary>
    /// Get container secrets for ECS task definition
    /// </summary>
    public Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, DeploymentContext context)
    {
        var secrets = new Dictionary<string, Amazon.CDK.AWS.ECS.Secret>();

        if (secretNames?.Count > 0)
        {
            Console.WriteLine($"     🔐 Processing {secretNames.Count} secret(s):");
            
            foreach (var envVarName in secretNames)
            {
                // Special handling for tm-frontend-env-vars secret
                if (envVarName == "tm-frontend-env-vars")
                {
                    var secretName = GetSecretNameFromEnvVar(envVarName);
                    var fullSecretName = BuildSecretName(secretName, context);
                    
                    Console.WriteLine($"        - Multi-value secret '{envVarName}' -> Secret '{secretName}'");
                    Console.WriteLine($"          Full secret path: {fullSecretName}");
                    
                    var secret = GetOrCreateMultiValueSecret(secretName, fullSecretName, context);
                    
                    // Create individual secret references for each environment variable
                    var frontendEnvVars = new[]
                    {
                        "NEXT_PUBLIC_COGNITO_USER_POOL_ID",
                        "NEXT_PUBLIC_COGNITO_CLIENT_ID", 
                        "NEXT_PUBLIC_COGNITO_CLIENT_SECRET",
                        "NEXT_PUBLIC_COGNITO_DOMAIN",
                        "NEXT_PUBLIC_API_URL",
                        "NEXT_PUBLIC_API_MODE"
                    };
                    
                    foreach (var envVar in frontendEnvVars)
                    {
                        secrets[envVar] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret, envVar);
                    }
                }
                else
                {
                    // Handle individual secrets as before
                    var secretName = GetSecretNameFromEnvVar(envVarName);
                    var fullSecretName = BuildSecretName(secretName, context);
                    
                    Console.WriteLine($"        - Environment variable '{envVarName}' -> Secret '{secretName}'");
                    Console.WriteLine($"          Full secret path: {fullSecretName}");
                    
                    var secret = GetOrCreateSecret(secretName, fullSecretName, context);
                    
                    secrets[envVarName] = Amazon.CDK.AWS.ECS.Secret.FromSecretsManager(secret);
                }
            }
        }

        return secrets;
    }

    /// <summary>
    /// Export secret ARNs as CloudFormation outputs
    /// </summary>
    public void ExportSecretArns()
    {
        foreach (var kvp in _createdSecrets)
        {
            var secretName = kvp.Key;
            var secret = kvp.Value;

            // Convert secret name to valid export name (replace all underscores with hyphens and convert to lowercase)
            var validExportName = secretName.Replace("_", "-").Replace("__", "-").ToLowerInvariant();

            new CfnOutput(this, $"SecretArn-{validExportName}", new CfnOutputProps
            {
                Value = secret.SecretArn,
                Description = $"TrialMatch Secret ARN for {secretName}",
                ExportName = $"{_context.Environment.Name}-trial-match-secret-{validExportName}-arn"
            });
        }
    }

    /// <summary>
    /// Get all created secrets for external access
    /// </summary>
    public Dictionary<string, ISecret> GetCreatedSecrets()
    {
        return new Dictionary<string, ISecret>(_createdSecrets);
    }

    /// <summary>
    /// Check if a secret has been created in this deployment
    /// </summary>
    public bool HasSecret(string secretName)
    {
        return _createdSecrets.ContainsKey(secretName);
    }
} 