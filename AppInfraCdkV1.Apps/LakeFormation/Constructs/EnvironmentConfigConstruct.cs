using Amazon.CDK;
using Amazon.CDK.AWS.SSM;
using Constructs;
using System.Collections.Generic;
using System.Text.Json;

namespace AppInfraCdkV1.Apps.LakeFormation.Constructs
{
    public interface IEnvironmentConfigConstructProps
    {
        string Environment { get; }
        string AccountId { get; }
        bool EnablePHIInNonProd { get; }
        Dictionary<string, EnvironmentPermissionConfig> EnvironmentConfigs { get; }
    }

    public class EnvironmentPermissionConfig
    {
        public bool AllowPHIAccess { get; set; } = false;
        public string[] AllowedDataTypes { get; set; } = new string[0];
        public string[] AllowedSensitivityLevels { get; set; } = new string[0];
        public Dictionary<string, string[]> RolePermissions { get; set; } = new Dictionary<string, string[]>();
        public bool EnableBroadAccess { get; set; } = false;
        public bool EnableAuditLogging { get; set; } = true;
        public int MaxPermissionDuration { get; set; } = 24; // hours
        public string[] RequiredTags { get; set; } = new string[0];
    }

    public class EnvironmentConfigConstructProps : IEnvironmentConfigConstructProps
    {
        public string Environment { get; set; } = "Development";
        public string AccountId { get; set; } = string.Empty;
        public bool EnablePHIInNonProd { get; set; } = true;
        public Dictionary<string, EnvironmentPermissionConfig> EnvironmentConfigs { get; set; } = new Dictionary<string, EnvironmentPermissionConfig>();
    }

    /// <summary>
    /// CDK Construct for managing environment-specific Lake Formation permission patterns
    /// Handles different access controls and validation rules across dev/staging/production
    /// </summary>
    public class EnvironmentConfigConstruct : Construct
    {
        public Dictionary<string, StringParameter> ConfigParameters { get; private set; }
        public Dictionary<string, EnvironmentPermissionConfig> EnvironmentConfigs { get; private set; }

        private readonly IEnvironmentConfigConstructProps _props;

        public EnvironmentConfigConstruct(
            Construct scope,
            string id,
            IEnvironmentConfigConstructProps props)
            : base(scope, id)
        {
            _props = props;
            ConfigParameters = new Dictionary<string, StringParameter>();
            EnvironmentConfigs = new Dictionary<string, EnvironmentPermissionConfig>();

            CreateDefaultEnvironmentConfigs();
            CreateSSMParametersForConfiguration();
            CreateEnvironmentSpecificValidation();

            // Add tags to the construct
            Amazon.CDK.Tags.Of(this).Add("Component", "EnvironmentConfig");
            Amazon.CDK.Tags.Of(this).Add("Environment", _props.Environment);
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }

        /// <summary>
        /// Creates default environment-specific configurations
        /// </summary>
        private void CreateDefaultEnvironmentConfigs()
        {
            // Development Environment Configuration
            var developmentConfig = new EnvironmentPermissionConfig
            {
                AllowPHIAccess = _props.EnablePHIInNonProd,
                AllowedDataTypes = new[] { "clinical", "research", "operational", "administrative", "reference" },
                AllowedSensitivityLevels = new[] { "public", "internal", "confidential", "restricted" },
                EnableBroadAccess = true, // Broader access for development testing
                EnableAuditLogging = true,
                MaxPermissionDuration = 72, // 3 days for development
                RequiredTags = new[] { "Environment", "DataType" },
                RolePermissions = new Dictionary<string, string[]>
                {
                    ["dev-test-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["dev-analytics-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["dev-data-engineering-role"] = new[] { "SELECT", "DESCRIBE", "INSERT" },
                    ["dev-admin-role"] = new[] { "SELECT", "DESCRIBE", "INSERT", "DELETE", "ALTER" }
                }
            };

            // Staging Environment Configuration
            var stagingConfig = new EnvironmentPermissionConfig
            {
                AllowPHIAccess = false, // No PHI access in staging by default
                AllowedDataTypes = new[] { "research", "operational", "administrative", "reference" },
                AllowedSensitivityLevels = new[] { "public", "internal", "confidential" }, // No restricted
                EnableBroadAccess = false,
                EnableAuditLogging = true,
                MaxPermissionDuration = 48, // 2 days for staging
                RequiredTags = new[] { "Environment", "DataType", "Sensitivity" },
                RolePermissions = new Dictionary<string, string[]>
                {
                    ["staging-test-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["staging-analytics-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["staging-admin-role"] = new[] { "SELECT", "DESCRIBE", "INSERT", "ALTER" }
                }
            };

            // Production Environment Configuration
            var productionConfig = new EnvironmentPermissionConfig
            {
                AllowPHIAccess = true, // PHI access only for specifically authorized roles
                AllowedDataTypes = new[] { "clinical", "research", "operational", "administrative", "reference" },
                AllowedSensitivityLevels = new[] { "public", "internal", "confidential", "restricted" },
                EnableBroadAccess = false, // Strict access controls
                EnableAuditLogging = true,
                MaxPermissionDuration = 24, // 1 day for production
                RequiredTags = new[] { "Environment", "DataType", "Sensitivity", "TenantID" },
                RolePermissions = new Dictionary<string, string[]>
                {
                    ["prod-phi-authorized-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["prod-non-phi-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["prod-analytics-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["prod-audit-role"] = new[] { "SELECT", "DESCRIBE" },
                    ["prod-admin-role"] = new[] { "SELECT", "DESCRIBE", "INSERT", "ALTER" }
                }
            };

            // Store configurations
            EnvironmentConfigs["development"] = developmentConfig;
            EnvironmentConfigs["staging"] = stagingConfig;
            EnvironmentConfigs["production"] = productionConfig;

            // Override with provided configurations
            foreach (var envConfig in _props.EnvironmentConfigs)
            {
                EnvironmentConfigs[envConfig.Key] = envConfig.Value;
            }
        }

        /// <summary>
        /// Creates SSM parameters to store environment configurations
        /// </summary>
        private void CreateSSMParametersForConfiguration()
        {
            var currentEnvConfig = EnvironmentConfigs.GetValueOrDefault(_props.Environment.ToLower(), EnvironmentConfigs["development"]);
            var envLower = _props.Environment.ToLower();

            // Store environment configuration as JSON in SSM
            var configJson = JsonSerializer.Serialize(currentEnvConfig, new JsonSerializerOptions { WriteIndented = true });
            
            var configParameter = new StringParameter(this, "EnvironmentConfigParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/environment-config",
                StringValue = configJson,
                Description = $"Lake Formation environment configuration for {_props.Environment}",
                Type = ParameterType.STRING
            });

            ConfigParameters["EnvironmentConfig"] = configParameter;

            // Create individual parameters for key settings
            var phiAccessParameter = new StringParameter(this, "PHIAccessParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/phi-access-enabled",
                StringValue = currentEnvConfig.AllowPHIAccess.ToString().ToLower(),
                Description = $"PHI access enabled for {_props.Environment} environment",
                Type = ParameterType.STRING
            });

            ConfigParameters["PHIAccess"] = phiAccessParameter;

            var broadAccessParameter = new StringParameter(this, "BroadAccessParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/broad-access-enabled",
                StringValue = currentEnvConfig.EnableBroadAccess.ToString().ToLower(),
                Description = $"Broad access enabled for {_props.Environment} environment",
                Type = ParameterType.STRING
            });

            ConfigParameters["BroadAccess"] = broadAccessParameter;

            var auditLoggingParameter = new StringParameter(this, "AuditLoggingParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/audit-logging-enabled",
                StringValue = currentEnvConfig.EnableAuditLogging.ToString().ToLower(),
                Description = $"Audit logging enabled for {_props.Environment} environment",
                Type = ParameterType.STRING
            });

            ConfigParameters["AuditLogging"] = auditLoggingParameter;

            var allowedDataTypesParameter = new StringParameter(this, "AllowedDataTypesParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/allowed-data-types",
                StringValue = string.Join(",", currentEnvConfig.AllowedDataTypes),
                Description = $"Allowed data types for {_props.Environment} environment",
                Type = ParameterType.STRING_LIST
            });

            ConfigParameters["AllowedDataTypes"] = allowedDataTypesParameter;

            var allowedSensitivityParameter = new StringParameter(this, "AllowedSensitivityParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/allowed-sensitivity-levels",
                StringValue = string.Join(",", currentEnvConfig.AllowedSensitivityLevels),
                Description = $"Allowed sensitivity levels for {_props.Environment} environment",
                Type = ParameterType.STRING_LIST
            });

            ConfigParameters["AllowedSensitivity"] = allowedSensitivityParameter;

            var requiredTagsParameter = new StringParameter(this, "RequiredTagsParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/required-tags",
                StringValue = string.Join(",", currentEnvConfig.RequiredTags),
                Description = $"Required LF-Tags for {_props.Environment} environment",
                Type = ParameterType.STRING_LIST
            });

            ConfigParameters["RequiredTags"] = requiredTagsParameter;

            var maxPermissionDurationParameter = new StringParameter(this, "MaxPermissionDurationParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/max-permission-duration-hours",
                StringValue = currentEnvConfig.MaxPermissionDuration.ToString(),
                Description = $"Maximum permission duration in hours for {_props.Environment} environment",
                Type = ParameterType.STRING
            });

            ConfigParameters["MaxPermissionDuration"] = maxPermissionDurationParameter;
        }

        /// <summary>
        /// Creates environment-specific validation rules
        /// </summary>
        private void CreateEnvironmentSpecificValidation()
        {
            var envLower = _props.Environment.ToLower();
            var currentEnvConfig = EnvironmentConfigs.GetValueOrDefault(envLower, EnvironmentConfigs["development"]);

            // Create validation rules as SSM parameters
            var validationRules = new Dictionary<string, object>();

            // PHI validation rules
            if (!currentEnvConfig.AllowPHIAccess)
            {
                validationRules["PHI_ACCESS_DENIED"] = "PHI access is not allowed in this environment";
            }

            // Data type validation rules
            if (currentEnvConfig.AllowedDataTypes.Length > 0)
            {
                validationRules["ALLOWED_DATA_TYPES"] = currentEnvConfig.AllowedDataTypes;
            }

            // Sensitivity level validation rules
            if (currentEnvConfig.AllowedSensitivityLevels.Length > 0)
            {
                validationRules["ALLOWED_SENSITIVITY_LEVELS"] = currentEnvConfig.AllowedSensitivityLevels;
            }

            // Required tags validation
            if (currentEnvConfig.RequiredTags.Length > 0)
            {
                validationRules["REQUIRED_TAGS"] = currentEnvConfig.RequiredTags;
            }

            // Broad access rules
            if (!currentEnvConfig.EnableBroadAccess)
            {
                validationRules["BROAD_ACCESS_DISABLED"] = "Broad access is disabled - specific role permissions required";
            }

            // Store validation rules
            var validationRulesJson = JsonSerializer.Serialize(validationRules, new JsonSerializerOptions { WriteIndented = true });
            
            var validationParameter = new StringParameter(this, "ValidationRulesParameter", new StringParameterProps
            {
                ParameterName = $"/lake-formation/{envLower}/validation-rules",
                StringValue = validationRulesJson,
                Description = $"Validation rules for Lake Formation permissions in {_props.Environment}",
                Type = ParameterType.STRING
            });

            ConfigParameters["ValidationRules"] = validationParameter;
        }

        /// <summary>
        /// Helper method to get current environment configuration
        /// </summary>
        public EnvironmentPermissionConfig GetCurrentEnvironmentConfig()
        {
            var envLower = _props.Environment.ToLower();
            return EnvironmentConfigs.GetValueOrDefault(envLower, EnvironmentConfigs["development"]);
        }

        /// <summary>
        /// Helper method to check if PHI access is allowed for current environment
        /// </summary>
        public bool IsPHIAccessAllowed()
        {
            return GetCurrentEnvironmentConfig().AllowPHIAccess;
        }

        /// <summary>
        /// Helper method to check if broad access is enabled for current environment
        /// </summary>
        public bool IsBroadAccessEnabled()
        {
            return GetCurrentEnvironmentConfig().EnableBroadAccess;
        }

        /// <summary>
        /// Helper method to get allowed data types for current environment
        /// </summary>
        public string[] GetAllowedDataTypes()
        {
            return GetCurrentEnvironmentConfig().AllowedDataTypes;
        }

        /// <summary>
        /// Helper method to get allowed sensitivity levels for current environment
        /// </summary>
        public string[] GetAllowedSensitivityLevels()
        {
            return GetCurrentEnvironmentConfig().AllowedSensitivityLevels;
        }

        /// <summary>
        /// Helper method to get role permissions for current environment
        /// </summary>
        public Dictionary<string, string[]> GetRolePermissions()
        {
            return GetCurrentEnvironmentConfig().RolePermissions;
        }

        /// <summary>
        /// Helper method to validate if a data type is allowed in current environment
        /// </summary>
        public bool IsDataTypeAllowed(string dataType)
        {
            var allowedTypes = GetAllowedDataTypes();
            return allowedTypes.Length == 0 || allowedTypes.Contains(dataType);
        }

        /// <summary>
        /// Helper method to validate if a sensitivity level is allowed in current environment
        /// </summary>
        public bool IsSensitivityLevelAllowed(string sensitivityLevel)
        {
            var allowedLevels = GetAllowedSensitivityLevels();
            return allowedLevels.Length == 0 || allowedLevels.Contains(sensitivityLevel);
        }
    }
}