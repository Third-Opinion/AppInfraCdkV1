using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.IAM;
using Constructs;
using System.Collections.Generic;
using System.Linq;

namespace AppInfraCdkV1.Apps.LakeFormation.Constructs
{
    public interface ILakeFormationPermissionsConstructProps
    {
        string Environment { get; }
        string AccountId { get; }
        Dictionary<string, PermissionGroup> PermissionGroups { get; }
        bool EnablePHIAccess { get; }
        bool EnableTenantFiltering { get; }
    }

    public class PermissionGroup
    {
        public string RoleName { get; set; } = string.Empty;
        public string[] Permissions { get; set; } = new[] { "SELECT", "DESCRIBE" };
        public Dictionary<string, string[]> AllowedTagValues { get; set; } = new Dictionary<string, string[]>();
        public string Description { get; set; } = string.Empty;
        public bool IsPHIAuthorized { get; set; } = false;
    }

    public class LakeFormationPermissionsConstructProps : ILakeFormationPermissionsConstructProps
    {
        public string Environment { get; set; } = "Development";
        public string AccountId { get; set; } = string.Empty;
        public Dictionary<string, PermissionGroup> PermissionGroups { get; set; } = new Dictionary<string, PermissionGroup>();
        public bool EnablePHIAccess { get; set; } = true;
        public bool EnableTenantFiltering { get; set; } = true;
    }

    /// <summary>
    /// CDK Construct for managing Lake Formation permissions based on LF-Tags
    /// Handles role-based PHI access control and tenant-based query filtering
    /// </summary>
    public class LakeFormationPermissionsConstruct : Construct
    {
        public Dictionary<string, CfnPermissions> PHIPermissions { get; private set; }
        public Dictionary<string, CfnPermissions> NonPHIPermissions { get; private set; }
        public Dictionary<string, CfnPermissions> TenantPermissions { get; private set; }
        public Dictionary<string, CfnPermissions> AdminPermissions { get; private set; }

        private readonly ILakeFormationPermissionsConstructProps _props;
        private readonly LakeFormationTagsConstruct _tagsConstruct;

        public LakeFormationPermissionsConstruct(
            Construct scope, 
            string id, 
            ILakeFormationPermissionsConstructProps props,
            LakeFormationTagsConstruct tagsConstruct) 
            : base(scope, id)
        {
            _props = props;
            _tagsConstruct = tagsConstruct;
            
            PHIPermissions = new Dictionary<string, CfnPermissions>();
            NonPHIPermissions = new Dictionary<string, CfnPermissions>();
            TenantPermissions = new Dictionary<string, CfnPermissions>();
            AdminPermissions = new Dictionary<string, CfnPermissions>();

            CreateDefaultPermissionGroups();
            CreatePHIBasedPermissions();
            CreateAdminPermissions();
            CreateEnvironmentSpecificPermissions();

            // Add tags to the construct
            Amazon.CDK.Tags.Of(this).Add("Component", "LakeFormationPermissions");
            Amazon.CDK.Tags.Of(this).Add("Environment", _props.Environment);
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }

        /// <summary>
        /// Creates default permission groups if none are provided
        /// </summary>
        private void CreateDefaultPermissionGroups()
        {
            if (_props.PermissionGroups.Count == 0)
            {
                // PHI-authorized role (can access ALL PHI data across tenants)
                _props.PermissionGroups["PHIAuthorized"] = new PermissionGroup
                {
                    RoleName = $"{_props.Environment.ToLower()}-phi-authorized-role",
                    Permissions = new[] { "SELECT", "DESCRIBE" },
                    Description = "Role authorized to access PHI data across all tenants",
                    IsPHIAuthorized = true,
                    AllowedTagValues = new Dictionary<string, string[]>
                    {
                        ["PHI"] = new[] { "true" }
                    }
                };

                // Non-PHI role (can access ALL non-PHI data across tenants)
                _props.PermissionGroups["NonPHI"] = new PermissionGroup
                {
                    RoleName = $"{_props.Environment.ToLower()}-non-phi-role",
                    Permissions = new[] { "SELECT", "DESCRIBE" },
                    Description = "Role for accessing non-PHI data across all tenants",
                    IsPHIAuthorized = false,
                    AllowedTagValues = new Dictionary<string, string[]>
                    {
                        ["PHI"] = new[] { "false" }
                    }
                };

                // Admin role (can access everything)
                _props.PermissionGroups["Admin"] = new PermissionGroup
                {
                    RoleName = $"{_props.Environment.ToLower()}-lake-formation-admin-role",
                    Permissions = new[] { "SELECT", "DESCRIBE", "INSERT", "DELETE", "ALTER" },
                    Description = "Administrative role with full Lake Formation access",
                    IsPHIAuthorized = true,
                    AllowedTagValues = new Dictionary<string, string[]>
                    {
                        ["DataType"] = new[] { "clinical", "research", "operational", "administrative", "reference" }
                    }
                };

                // Data Engineering role (can access non-PHI for ETL)
                _props.PermissionGroups["DataEngineering"] = new PermissionGroup
                {
                    RoleName = $"{_props.Environment.ToLower()}-data-engineering-role",
                    Permissions = new[] { "SELECT", "DESCRIBE", "INSERT" },
                    Description = "Data engineering role for ETL processes (non-PHI only)",
                    IsPHIAuthorized = false,
                    AllowedTagValues = new Dictionary<string, string[]>
                    {
                        ["PHI"] = new[] { "false" },
                        ["DataType"] = new[] { "operational", "reference" }
                    }
                };

                // Analytics role (environment-specific PHI access)
                var isPhiAllowed = _props.Environment.ToLower() != "production";
                _props.PermissionGroups["Analytics"] = new PermissionGroup
                {
                    RoleName = $"{_props.Environment.ToLower()}-analytics-role",
                    Permissions = new[] { "SELECT", "DESCRIBE" },
                    Description = $"Analytics role - PHI access: {(isPhiAllowed ? "Yes (non-prod)" : "No (prod)")}",
                    IsPHIAuthorized = isPhiAllowed,
                    AllowedTagValues = new Dictionary<string, string[]>
                    {
                        ["PHI"] = isPhiAllowed ? new[] { "true", "false" } : new[] { "false" },
                        ["DataType"] = new[] { "clinical", "research", "operational" }
                    }
                };
            }
        }

        /// <summary>
        /// Creates PHI-based permissions following the role-based access model
        /// </summary>
        private void CreatePHIBasedPermissions()
        {
            if (!_props.EnablePHIAccess) return;

            foreach (var group in _props.PermissionGroups)
            {
                var groupName = group.Key;
                var groupConfig = group.Value;
                
                var roleArn = $"arn:aws:iam::{_props.AccountId}:role/{groupConfig.RoleName}";

                // Create permissions based on PHI authorization
                if (groupConfig.IsPHIAuthorized && groupConfig.AllowedTagValues.ContainsKey("PHI"))
                {
                    var phiValues = groupConfig.AllowedTagValues["PHI"];
                    
                    var phiPermission = new CfnPermissions(this, $"PHI{groupName}Permissions", new CfnPermissionsProps
                    {
                        DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                        {
                            DataLakePrincipalIdentifier = roleArn
                        },
                        Resource = new CfnPermissions.ResourceProperty
                        {
                            LfTag = new CfnPermissions.LfTagKeyResourceProperty
                            {
                                TagKey = "PHI",
                                TagValues = phiValues
                            }
                        },
                        Permissions = groupConfig.Permissions
                    });

                    if (phiValues.Contains("true"))
                    {
                        PHIPermissions[groupName] = phiPermission;
                    }
                    else
                    {
                        NonPHIPermissions[groupName] = phiPermission;
                    }
                }

                // Create permissions for other tag-based access
                foreach (var tagAccess in groupConfig.AllowedTagValues.Where(x => x.Key != "PHI"))
                {
                    var tagPermission = new CfnPermissions(this, $"{groupName}{tagAccess.Key}Permissions", new CfnPermissionsProps
                    {
                        DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                        {
                            DataLakePrincipalIdentifier = roleArn
                        },
                        Resource = new CfnPermissions.ResourceProperty
                        {
                            LfTag = new CfnPermissions.LfTagKeyResourceProperty
                            {
                                TagKey = tagAccess.Key,
                                TagValues = tagAccess.Value
                            }
                        },
                        Permissions = groupConfig.Permissions
                    });

                    if (tagAccess.Key == "TenantID")
                    {
                        TenantPermissions[$"{groupName}_{tagAccess.Key}"] = tagPermission;
                    }
                }
            }
        }

        /// <summary>
        /// Creates administrative permissions with full access
        /// </summary>
        private void CreateAdminPermissions()
        {
            var adminGroups = _props.PermissionGroups.Where(g => g.Value.Permissions.Contains("ALTER") || g.Value.Permissions.Contains("DELETE"));

            foreach (var adminGroup in adminGroups)
            {
                var groupName = adminGroup.Key;
                var groupConfig = adminGroup.Value;
                var roleArn = $"arn:aws:iam::{_props.AccountId}:role/{groupConfig.RoleName}";

                // Grant admin access to all data types
                var adminPermission = new CfnPermissions(this, $"Admin{groupName}Permissions", new CfnPermissionsProps
                {
                    DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                    {
                        DataLakePrincipalIdentifier = roleArn
                    },
                    Resource = new CfnPermissions.ResourceProperty
                    {
                        LfTag = new CfnPermissions.LfTagKeyResourceProperty
                        {
                            TagKey = "DataType",
                            TagValues = new[] { "clinical", "research", "operational", "administrative", "reference" }
                        }
                    },
                    Permissions = groupConfig.Permissions
                });

                AdminPermissions[groupName] = adminPermission;
            }
        }

        /// <summary>
        /// Creates environment-specific permission patterns
        /// </summary>
        private void CreateEnvironmentSpecificPermissions()
        {
            var envLower = _props.Environment.ToLower();

            // Development environment - broader access for testing
            if (envLower == "development")
            {
                CreateDevelopmentPermissions();
            }
            // Production environment - restricted access
            else if (envLower == "production")
            {
                CreateProductionPermissions();
            }
            // Staging environment - production-like but with some relaxed rules
            else if (envLower == "staging")
            {
                CreateStagingPermissions();
            }
        }

        private void CreateDevelopmentPermissions()
        {
            // In development, create a broader access role for testing
            var devTestRoleArn = $"arn:aws:iam::{_props.AccountId}:role/dev-test-role";

            var devTestPermission = new CfnPermissions(this, "DevTestPermissions", new CfnPermissionsProps
            {
                DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = devTestRoleArn
                },
                Resource = new CfnPermissions.ResourceProperty
                {
                    LfTag = new CfnPermissions.LfTagKeyResourceProperty
                    {
                        TagKey = "Environment",
                        TagValues = new[] { "development" }
                    }
                },
                Permissions = new[] { "SELECT", "DESCRIBE" }
            });
        }

        private void CreateProductionPermissions()
        {
            // Production has stricter controls - only through defined roles
            // Additional logging and audit permissions could be added here
            
            // Example: Create audit role with read-only access
            var auditRoleArn = $"arn:aws:iam::{_props.AccountId}:role/prod-audit-role";

            var auditPermission = new CfnPermissions(this, "ProdAuditPermissions", new CfnPermissionsProps
            {
                DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = auditRoleArn
                },
                Resource = new CfnPermissions.ResourceProperty
                {
                    LfTag = new CfnPermissions.LfTagKeyResourceProperty
                    {
                        TagKey = "Sensitivity",
                        TagValues = new[] { "public", "internal" } // No confidential/restricted for audit
                    }
                },
                Permissions = new[] { "SELECT", "DESCRIBE" }
            });
        }

        private void CreateStagingPermissions()
        {
            // Staging environment permissions - similar to production but slightly more permissive
            var stagingTestRoleArn = $"arn:aws:iam::{_props.AccountId}:role/staging-test-role";

            var stagingPermission = new CfnPermissions(this, "StagingTestPermissions", new CfnPermissionsProps
            {
                DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = stagingTestRoleArn
                },
                Resource = new CfnPermissions.ResourceProperty
                {
                    LfTag = new CfnPermissions.LfTagKeyResourceProperty
                    {
                        TagKey = "Environment",
                        TagValues = new[] { "staging" }
                    }
                },
                Permissions = new[] { "SELECT", "DESCRIBE" }
            });
        }

        /// <summary>
        /// Helper method to create custom permission for a specific role and tag combination
        /// </summary>
        public CfnPermissions CreateCustomPermission(
            string permissionId,
            string roleArn,
            string tagKey,
            string[] tagValues,
            string[] permissions)
        {
            return new CfnPermissions(this, permissionId, new CfnPermissionsProps
            {
                DataLakePrincipal = new CfnPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = roleArn
                },
                Resource = new CfnPermissions.ResourceProperty
                {
                    LfTag = new CfnPermissions.LfTagKeyResourceProperty
                    {
                        TagKey = tagKey,
                        TagValues = tagValues
                    }
                },
                Permissions = permissions
            });
        }

        /// <summary>
        /// Helper method to get all permissions for monitoring and validation
        /// </summary>
        public Dictionary<string, CfnPermissions> GetAllPermissions()
        {
            var allPermissions = new Dictionary<string, CfnPermissions>();

            foreach (var perm in PHIPermissions)
                allPermissions[$"PHI_{perm.Key}"] = perm.Value;

            foreach (var perm in NonPHIPermissions)
                allPermissions[$"NonPHI_{perm.Key}"] = perm.Value;

            foreach (var perm in TenantPermissions)
                allPermissions[$"Tenant_{perm.Key}"] = perm.Value;

            foreach (var perm in AdminPermissions)
                allPermissions[$"Admin_{perm.Key}"] = perm.Value;

            return allPermissions;
        }
    }
}