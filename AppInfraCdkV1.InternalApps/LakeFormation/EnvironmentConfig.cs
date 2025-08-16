using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppInfraCdkV1.InternalApps.LakeFormation
{
    public class LakeFormationEnvironmentConfig
    {
        [JsonPropertyName("environment")]
        public string Environment { get; set; } = string.Empty;
        
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;
        
        [JsonPropertyName("region")]
        public string Region { get; set; } = "us-east-2";
        
        [JsonPropertyName("identityCenter")]
        public IdentityCenterConfig IdentityCenter { get; set; } = new();
        
        [JsonPropertyName("groupMappings")]
        public Dictionary<string, GroupPermissions> GroupMappings { get; set; } = new();
        
        [JsonPropertyName("bucketConfig")]
        public DataLakeBucketConfig BucketConfig { get; set; } = new();
        
        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; } = new();
        
        [JsonPropertyName("healthLake")]
        public List<HealthLakeConfig> HealthLake { get; set; } = new();
    }
    
    public class HealthLakeConfig
    {
        [JsonPropertyName("datastoreId")]
        public string DatastoreId { get; set; } = string.Empty;
        
        [JsonPropertyName("datastoreArn")]
        public string DatastoreArn { get; set; } = string.Empty;
        
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty; // GUID for this single tenant
        
        [JsonPropertyName("tenantName")]
        public string TenantName { get; set; } = string.Empty; // Human-readable tenant/customer name
        
        [JsonPropertyName("enableSampleData")]
        public bool EnableSampleData { get; set; } = false; // For test instances
        
        [JsonPropertyName("markAsPHI")]
        public bool MarkAsPHI { get; set; } = false; // Mark sample data as PHI
    }
    
    public class IdentityCenterConfig
    {
        [JsonPropertyName("instanceArn")]
        public string InstanceArn { get; set; } = "arn:aws:sso:::instance/ssoins-66849025a110d385";
        
        [JsonPropertyName("identityStoreId")]
        public string IdentityStoreId { get; set; } = "d-9a677c3adb";
        
        [JsonPropertyName("identityCenterAccountId")]
        public string IdentityCenterAccountId { get; set; } = "442042533707";
    }
    
    public class GroupPermissions
    {
        [JsonPropertyName("groupName")]
        public string GroupName { get; set; } = string.Empty;
        
        [JsonPropertyName("groupEmail")]
        public string GroupEmail { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("permissions")]
        public List<string> Permissions { get; set; } = new();
        
        [JsonPropertyName("allowedDatabases")]
        public List<string> AllowedDatabases { get; set; } = new();
        
        [JsonPropertyName("allowedTables")]
        public List<string> AllowedTables { get; set; } = new();
        
        [JsonPropertyName("excludePHI")]
        public bool ExcludePHI { get; set; } = true;
        
        [JsonPropertyName("isDataLakeAdmin")]
        public bool IsDataLakeAdmin { get; set; } = false;
    }
    
    public class DataLakeBucketConfig
    {
        [JsonPropertyName("bucketPrefix")]
        public string BucketPrefix { get; set; } = "thirdopinion";
        
        [JsonPropertyName("enableVersioning")]
        public bool EnableVersioning { get; set; } = true;
        
        [JsonPropertyName("enableEncryption")]
        public bool EnableEncryption { get; set; } = true;
        
        [JsonPropertyName("enableAccessLogging")]
        public bool EnableAccessLogging { get; set; } = true;
        
        [JsonPropertyName("lifecycle")]
        public DataLifecycleConfig Lifecycle { get; set; } = new();
        
        [JsonPropertyName("singleTenantId")]
        public string SingleTenantId { get; set; } = string.Empty; // GUID for single tenant mode
    }
    
    public class DataLifecycleConfig
    {
        [JsonPropertyName("transitionToIADays")]
        public int TransitionToIADays { get; set; } = 30;
        
        [JsonPropertyName("transitionToGlacierDays")]
        public int TransitionToGlacierDays { get; set; } = 90;
        
        [JsonPropertyName("expirationDays")]
        public int ExpirationDays { get; set; } = 0;
        
        [JsonPropertyName("enableIntelligentTiering")]
        public bool EnableIntelligentTiering { get; set; } = true;
    }
    
    public class LakeFormationConfigRoot
    {
        [JsonPropertyName("environments")]
        public Dictionary<string, LakeFormationEnvironmentConfig> Environments { get; set; } = new();
    }
    
    public static class LakeFormationEnvironmentConfigFactory
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        
        public static LakeFormationEnvironmentConfig CreateConfig(string environment, string accountId)
        {
            // Normalize environment name to lowercase
            var envKey = environment.ToLower();
            
            // Find the configuration file
            var configPath = FindConfigFile();
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Lake Formation configuration file not found at: {configPath}");
            }
            
            // Read and parse the configuration
            var jsonContent = File.ReadAllText(configPath);
            var configRoot = JsonSerializer.Deserialize<LakeFormationConfigRoot>(jsonContent, JsonOptions);
            
            if (configRoot?.Environments == null)
            {
                throw new InvalidOperationException("Invalid Lake Formation configuration: missing environments section");
            }
            
            // Find the matching environment
            if (!configRoot.Environments.TryGetValue(envKey, out var config))
            {
                throw new ArgumentException($"Unknown environment: {environment}. Available environments: {string.Join(", ", configRoot.Environments.Keys)}");
            }
            
            // Override account ID if provided
            if (!string.IsNullOrEmpty(accountId))
            {
                config.AccountId = accountId;
            }
            
            return config;
        }
        
        private static string FindConfigFile()
        {
            // Try multiple locations for the config file
            var possiblePaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "lakeformation-config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "AppInfraCdkV1.InternalApps", "LakeFormation", "lakeformation-config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lakeformation-config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "AppInfraCdkV1.InternalApps", "LakeFormation", "lakeformation-config.json")
            };
            
            foreach (var path in possiblePaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            
            // Default location
            return Path.Combine(Directory.GetCurrentDirectory(), "..", "AppInfraCdkV1.InternalApps", "LakeFormation", "lakeformation-config.json");
        }
    }
}