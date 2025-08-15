using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Constructs;
using System.Collections.Generic;
using System.Linq;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Stacks
{
    public class LakeFormationPermissionsStack : Stack
    {
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly LakeFormationSetupStack _lakeFormationStack;
        
        public LakeFormationPermissionsStack(Construct scope, string id, IStackProps props, 
            LakeFormationEnvironmentConfig config, LakeFormationSetupStack lakeFormationStack) 
            : base(scope, id, props)
        {
            _config = config;
            _lakeFormationStack = lakeFormationStack;
            
            GrantGroupPermissions();
            
            Amazon.CDK.Tags.Of(this).Add("Component", "LakeFormationPermissions");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }
        
        private void GrantGroupPermissions()
        {
            foreach (var groupMapping in _config.GroupMappings)
            {
                var groupName = groupMapping.Key;
                var groupConfig = groupMapping.Value;
                
                var principalIdentifier = BuildIdentityCenterPrincipal(groupName);
                
                if (groupConfig.IsDataLakeAdmin)
                {
                    GrantDataLakeAdminPermissions(groupName, principalIdentifier);
                }
                else
                {
                    foreach (var database in groupConfig.AllowedDatabases)
                    {
                        if (database == "*")
                        {
                            GrantAllDatabasePermissions(groupName, principalIdentifier, groupConfig.Permissions.ToArray());
                        }
                        else
                        {
                            GrantDatabasePermissions(groupName, database, principalIdentifier, groupConfig.Permissions.ToArray());
                        }
                    }
                }
                
                // Tag-based permissions will be added later when groups are synced
                // ApplyLakeFormationTags(groupName, principalIdentifier, groupConfig);
            }
        }
        
        private string BuildIdentityCenterPrincipal(string groupName)
        {
            var param = new CfnParameter(this, $"GroupId-{groupName}", new CfnParameterProps
            {
                Type = "String",
                Description = $"Identity Center Group ID for {groupName}",
                Default = "PLACEHOLDER"
            });
            
            return $"arn:aws:identitystore::{_config.IdentityCenter.IdentityCenterAccountId}:group/{param.ValueAsString}";
        }
        
        private void GrantDataLakeAdminPermissions(string groupName, string principalIdentifier)
        {
            new CfnPrincipalPermissions(this, $"AdminPermissions-{groupName}", new CfnPrincipalPermissionsProps
            {
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = principalIdentifier
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Catalog = new Dictionary<string, object>()
                },
                Permissions = new[] { "CREATE_DATABASE", "CREATE_TABLE", "DATA_LOCATION_ACCESS" },
                PermissionsWithGrantOption = new string[] { }
            });
        }
        
        private void GrantAllDatabasePermissions(string groupName, string principalIdentifier, string[] permissions)
        {
            // Grant permissions to all databases for this single tenant
            foreach (var database in _lakeFormationStack.Databases)
            {
                var databaseName = (database.DatabaseInput as CfnDatabase.DatabaseInputProperty)?.Name;
                if (databaseName != null)
                {
                    GrantDatabasePermissions(groupName, databaseName, principalIdentifier, permissions);
                }
            }
        }
        
        private void GrantSingleTenantTablePermissions(string groupName, string databaseName, 
            string principalIdentifier, string[] permissions)
        {
            // Grant permissions on all tables for this single tenant's database
            // All data in the database belongs to the same tenant
            var tenantId = _config.BucketConfig.SingleTenantId;
            
            new CfnPrincipalPermissions(this, $"SingleTenantPermissions-{groupName}", new CfnPrincipalPermissionsProps
            {
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = principalIdentifier
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    TableWithColumns = new CfnPrincipalPermissions.TableWithColumnsResourceProperty
                    {
                        CatalogId = _config.AccountId,
                        DatabaseName = databaseName,
                        Name = "ALL_TABLES",
                        ColumnWildcard = new Dictionary<string, object>()
                    }
                },
                Permissions = permissions.Contains("ALL") 
                    ? new[] { "SELECT", "DESCRIBE", "ALTER", "INSERT", "DELETE" }
                    : permissions,
                PermissionsWithGrantOption = new string[] { }
            });
            
            // Add tag-based permission for this specific tenant
            ApplyTenantTagPermissions(groupName, principalIdentifier, tenantId);
        }
        
        private void GrantDatabasePermissions(string groupName, string databaseName, 
            string principalIdentifier, string[] permissions)
        {
            new CfnPrincipalPermissions(this, $"DbPermissions-{groupName}-{databaseName}", new CfnPrincipalPermissionsProps
            {
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = principalIdentifier
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Database = new CfnPrincipalPermissions.DatabaseResourceProperty
                    {
                        CatalogId = _config.AccountId,
                        Name = databaseName
                    }
                },
                Permissions = new[] { "DESCRIBE" },
                PermissionsWithGrantOption = new string[] { }
            });
            
            var tablePermissions = permissions.Contains("ALL") 
                ? new[] { "SELECT", "INSERT", "DELETE", "DESCRIBE", "ALTER", "DROP" }
                : permissions;
            
            new CfnPrincipalPermissions(this, $"TablePermissions-{groupName}-{databaseName}", new CfnPrincipalPermissionsProps
            {
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = principalIdentifier
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Table = new CfnPrincipalPermissions.TableResourceProperty
                    {
                        CatalogId = _config.AccountId,
                        DatabaseName = databaseName,
                        TableWildcard = new Dictionary<string, object>()
                    }
                },
                Permissions = tablePermissions,
                PermissionsWithGrantOption = new string[] { }
            });
        }
        
        private void ApplyTenantTagPermissions(string groupName, string principalIdentifier, string tenantId)
        {
            // Grant permissions based on TenantID tag for single-tenant isolation
            new CfnPrincipalPermissions(this, $"TenantTagPermissions-{groupName}", new CfnPrincipalPermissionsProps
            {
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = principalIdentifier
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    LfTag = new CfnPrincipalPermissions.LFTagKeyResourceProperty
                    {
                        CatalogId = _config.AccountId,
                        TagKey = "TenantID",
                        TagValues = new[] { tenantId }
                    }
                },
                Permissions = new[] { "DESCRIBE" },
                PermissionsWithGrantOption = new string[] { }
            });
        }
        
        // Apply PHI tag permissions based on group configuration
        // private void ApplyPhiTagPermissions(string groupName, string principalIdentifier, GroupPermissions groupConfig)
        // {
        //     if (groupConfig.ExcludePHI)
        //     {
        //         new CfnPrincipalPermissions(this, $"PhiTagPermissions-{groupName}", new CfnPrincipalPermissionsProps
        //         {
        //             Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
        //             {
        //                 DataLakePrincipalIdentifier = principalIdentifier
        //             },
        //             Resource = new CfnPrincipalPermissions.ResourceProperty
        //             {
        //                 LfTag = new CfnPrincipalPermissions.LFTagKeyResourceProperty
        //                 {
        //                     DatabaseName = "*",
        //                     TableWildcard = new Dictionary<string, object>()
        //                 }
        //             }
        //         });
        //     }
        // }
    }
}