using System;
using System.Collections.Generic;

namespace AppInfraCdkV1.Apps.LakeFormation
{
    public class LakeFormationEnvironmentConfig
    {
        public string Environment { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Region { get; set; } = "us-east-2";
        public IdentityCenterConfig IdentityCenter { get; set; } = new();
        public LakeFormationGroupMappings GroupMappings { get; set; } = new();
        public DataLakeBucketConfig BucketConfig { get; set; } = new();
        public Dictionary<string, string> Tags { get; set; } = new();
    }
    
    public class IdentityCenterConfig
    {
        public string InstanceArn { get; set; } = "arn:aws:sso:::instance/ssoins-66849025a110d385";
        public string IdentityStoreId { get; set; } = "d-9a677c3adb";
        public string IdentityCenterAccountId { get; set; } = "442042533707";
    }
    
    public class LakeFormationGroupMappings
    {
        public Dictionary<string, GroupPermissions> Groups { get; set; } = new();
        
        public LakeFormationGroupMappings()
        {
            Groups = new Dictionary<string, GroupPermissions>
            {
                ["data-analysts-dev"] = new GroupPermissions
                {
                    GroupName = "data-analysts-dev",
                    GroupEmail = "data-analysts-dev@thirdopinion.io",
                    Description = "Development data analysts with non-PHI access",
                    Permissions = new List<string> { "SELECT", "DESCRIBE" },
                    AllowedDatabases = new List<string> { "thirdopinion_dev", "thirdopinion_staging" },
                    AllowedTables = new List<string> { "*" },
                    ExcludePHI = true
                },
                ["data-analysts-phi"] = new GroupPermissions
                {
                    GroupName = "data-analysts-phi",
                    GroupEmail = "data-analysts-phi@thirdopinion.io",
                    Description = "Data analysts with PHI access",
                    Permissions = new List<string> { "SELECT", "DESCRIBE" },
                    AllowedDatabases = new List<string> { "thirdopinion_prod", "thirdopinion_phi" },
                    AllowedTables = new List<string> { "*" },
                    ExcludePHI = false
                },
                ["data-engineers-phi"] = new GroupPermissions
                {
                    GroupName = "data-engineers-phi",
                    GroupEmail = "data-engineers-phi@thirdopinion.io",
                    Description = "Data engineers with full PHI access",
                    Permissions = new List<string> { "ALL" },
                    AllowedDatabases = new List<string> { "*" },
                    AllowedTables = new List<string> { "*" },
                    ExcludePHI = false,
                    IsDataLakeAdmin = true
                }
            };
        }
    }
    
    public class GroupPermissions
    {
        public string GroupName { get; set; } = string.Empty;
        public string GroupEmail { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public List<string> AllowedDatabases { get; set; } = new();
        public List<string> AllowedTables { get; set; } = new();
        public bool ExcludePHI { get; set; } = true;
        public bool IsDataLakeAdmin { get; set; } = false;
    }
    
    public class DataLakeBucketConfig
    {
        public string BucketPrefix { get; set; } = "thirdopinion";
        public bool EnableVersioning { get; set; } = true;
        public bool EnableEncryption { get; set; } = true;
        public bool EnableAccessLogging { get; set; } = true;
        public DataLifecycleConfig Lifecycle { get; set; } = new();
    }
    
    public class DataLifecycleConfig
    {
        public int TransitionToIADays { get; set; } = 30;
        public int TransitionToGlacierDays { get; set; } = 90;
        public int ExpirationDays { get; set; } = 0;
        public bool EnableIntelligentTiering { get; set; } = true;
    }
    
    public static class LakeFormationEnvironmentConfigFactory
    {
        public static LakeFormationEnvironmentConfig CreateConfig(string environment, string accountId)
        {
            var config = new LakeFormationEnvironmentConfig
            {
                Environment = environment,
                AccountId = accountId,
                Region = "us-east-2"
            };
            
            switch (environment.ToLower())
            {
                case "dev":
                case "development":
                    config.Tags = new Dictionary<string, string>
                    {
                        ["Environment"] = "Development",
                        ["Project"] = "ThirdOpinionDataLake",
                        ["ManagedBy"] = "CDK",
                        ["Owner"] = "DataEngineering"
                    };
                    config.BucketConfig.Lifecycle.ExpirationDays = 90;
                    break;
                    
                case "prod":
                case "production":
                    config.Tags = new Dictionary<string, string>
                    {
                        ["Environment"] = "Production",
                        ["Project"] = "ThirdOpinionDataLake",
                        ["ManagedBy"] = "CDK",
                        ["Owner"] = "DataEngineering",
                        ["Compliance"] = "HIPAA"
                    };
                    config.BucketConfig.Lifecycle.ExpirationDays = 0;
                    config.BucketConfig.EnableAccessLogging = true;
                    config.BucketConfig.EnableEncryption = true;
                    break;
                    
                default:
                    throw new ArgumentException($"Unknown environment: {environment}");
            }
            
            return config;
        }
    }
}