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
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using Constructs;
using System.Linq;

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
                    SecretStringTemplate = string.Format(ConfigurationConstants.SecretGeneration.DefaultSecretTemplate, secretName, context.Environment.Name),
                    GenerateStringKey = "value",
                    PasswordLength = ConfigurationConstants.SecretGeneration.DefaultPasswordLength,
                    ExcludeCharacters = ConfigurationConstants.SecretGeneration.ExcludedCharacters
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
    public ISecret GetOrCreateMultiValueSecret(string secretName, string fullSecretName, DeploymentContext context, FrontendEnvironmentVariables? frontendConfig = null)
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
            
            // Create the JSON structure for frontend environment variables using configuration or defaults
            var frontendEnvVarsJson = CreateFrontendEnvironmentVariablesJson(frontendConfig);
            
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
    /// Create JSON structure for frontend environment variables using configuration or sensible defaults
    /// </summary>
    private string CreateFrontendEnvironmentVariablesJson(FrontendEnvironmentVariables? frontendConfig)
    {
        var envVars = new Dictionary<string, string>
        {
            ["NEXT_PUBLIC_COGNITO_USER_POOL_ID"] = frontendConfig?.CognitoUserPoolId ?? "your_cognito_user_pool_id",
            ["NEXT_PUBLIC_COGNITO_CLIENT_ID"] = frontendConfig?.CognitoClientId ?? "your_cognito_client_id",
            ["NEXT_PUBLIC_COGNITO_CLIENT_SECRET"] = frontendConfig?.CognitoClientSecret ?? "your_cognito_client_secret",
            ["NEXT_PUBLIC_COGNITO_DOMAIN"] = frontendConfig?.CognitoDomain ?? "your_cognito_domain",
            ["NEXT_PUBLIC_API_URL"] = frontendConfig?.ApiUrl ?? "your_api_url",
            ["NEXT_PUBLIC_API_MODE"] = frontendConfig?.ApiMode ?? "development"
        };

        // Convert to JSON format
        var jsonEntries = envVars.Select(kvp => $"\"{kvp.Key}\": \"{kvp.Value}\"");
        return "{\n  " + string.Join(",\n  ", jsonEntries) + "\n}";
    }

    /// <summary>
    /// Get container secrets for ECS task definition
    /// </summary>
    public Dictionary<string, Amazon.CDK.AWS.ECS.Secret> GetContainerSecrets(List<string>? secretNames, DeploymentContext context, FrontendEnvironmentVariables? frontendConfig = null, CognitoStackOutputs? cognitoOutputs = null)
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
                    
                    // Create frontend environment variables using Cognito outputs if available
                    var frontendConfigWithCognito = CreateFrontendConfigWithCognitoOutputs(frontendConfig, cognitoOutputs);
                    var secret = GetOrCreateMultiValueSecret(secretName, fullSecretName, context, frontendConfigWithCognito);
                    
                    // Create individual secret references for each environment variable
                    var frontendEnvVars = FrontendEnvironmentVariables.GetEnvironmentVariableNames();
                    
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
    /// Create frontend configuration by merging configuration with Cognito stack outputs
    /// This ensures frontend environment variables use actual Cognito values instead of placeholders
    /// </summary>
    private FrontendEnvironmentVariables? CreateFrontendConfigWithCognitoOutputs(FrontendEnvironmentVariables? frontendConfig, CognitoStackOutputs? cognitoOutputs)
    {
        if (cognitoOutputs == null)
        {
            return frontendConfig;
        }

        // Create a new configuration that prioritizes Cognito outputs over configuration values
        var mergedConfig = new FrontendEnvironmentVariables
        {
            // Use Cognito outputs if available, otherwise fall back to configuration or defaults
            CognitoUserPoolId = !string.IsNullOrEmpty(cognitoOutputs.UserPoolId) ? cognitoOutputs.UserPoolId : frontendConfig?.CognitoUserPoolId,
            CognitoClientId = !string.IsNullOrEmpty(cognitoOutputs.UserPoolClientId) ? cognitoOutputs.UserPoolClientId : frontendConfig?.CognitoClientId,
            CognitoClientSecret = frontendConfig?.CognitoClientSecret, // Keep from config as this is sensitive
            CognitoDomain = !string.IsNullOrEmpty(cognitoOutputs.UserPoolDomain) ? cognitoOutputs.UserPoolDomain : frontendConfig?.CognitoDomain,
            ApiUrl = frontendConfig?.ApiUrl,
            ApiMode = frontendConfig?.ApiMode
        };

        Console.WriteLine($"          🔐 Merged Cognito outputs with frontend configuration:");
        Console.WriteLine($"            - User Pool ID: {mergedConfig.CognitoUserPoolId ?? "not set"}");
        Console.WriteLine($"            - Client ID: {mergedConfig.CognitoClientId ?? "not set"}");
        Console.WriteLine($"            - Domain: {mergedConfig.CognitoDomain ?? "not set"}");

        return mergedConfig;
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