using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.IAM;
using Constructs;
using System.Collections.Generic;
using System.Linq;
using AppInfraCdkV1.InternalApps.LakeFormation.Constructs;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Stacks
{
    public class LakeFormationSetupStack : Stack
    {
        public List<CfnDatabase> Databases { get; private set; }
        public Role LakeFormationServiceRole { get; private set; }
        public CfnDataLakeSettings DataLakeSettings { get; private set; }
        public LakeFormationIdentityCenterRolesConstruct IdentityCenterRoles { get; private set; }
        
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly DataLakeStorageStack _storageStack;
        
        public LakeFormationSetupStack(Construct scope, string id, IStackProps props, 
            LakeFormationEnvironmentConfig config, DataLakeStorageStack storageStack) 
            : base(scope, id, props)
        {
            _config = config;
            _storageStack = storageStack;
            Databases = new List<CfnDatabase>();
            
            // Add explicit dependency on storage stack
            AddDependency(storageStack);
            
            CreateLakeFormationServiceRole();
            CreateIdentityCenterRoles();
            ConfigureDataLakeSettings();
            CreateGlueDatabases();
            RegisterS3Locations();
            CreateLakeFormationTags();
            
            Amazon.CDK.Tags.Of(this).Add("Component", "LakeFormationSetup");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }
        
        private void CreateLakeFormationServiceRole()
        {
            LakeFormationServiceRole = new Role(this, "LakeFormationServiceRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lakeformation.amazonaws.com"),
                Description = "Service role for Lake Formation",
                RoleName = $"LakeFormationServiceRole-{_config.Environment}",
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("AWSLakeFormationDataAdmin")
                }
            });
            
            LakeFormationServiceRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:GetObject",
                    "s3:PutObject",
                    "s3:DeleteObject",
                    "s3:ListBucket",
                    "s3:GetBucketLocation",
                    "s3:ListBucketMultipartUploads",
                    "s3:ListMultipartUploadParts",
                    "s3:AbortMultipartUpload"
                },
                Resources = new[]
                {
                    _storageStack.RawDataBucket.BucketArn,
                    $"{_storageStack.RawDataBucket.BucketArn}/*",
                    _storageStack.CuratedDataBucket.BucketArn,
                    $"{_storageStack.CuratedDataBucket.BucketArn}/*"
                }
            }));
            
            if (_storageStack.PhiDataBucket != null)
            {
                LakeFormationServiceRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Effect = Effect.ALLOW,
                    Actions = new[]
                    {
                        "s3:GetObject",
                        "s3:PutObject",
                        "s3:DeleteObject",
                        "s3:ListBucket"
                    },
                    Resources = new[]
                    {
                        _storageStack.PhiDataBucket.BucketArn,
                        $"{_storageStack.PhiDataBucket.BucketArn}/*"
                    }
                }));
                
                if (_storageStack.DataEncryptionKey != null)
                {
                    LakeFormationServiceRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
                    {
                        Effect = Effect.ALLOW,
                        Actions = new[]
                        {
                            "kms:Decrypt",
                            "kms:Encrypt",
                            "kms:GenerateDataKey",
                            "kms:DescribeKey"
                        },
                        Resources = new[] { _storageStack.DataEncryptionKey.KeyArn }
                    }));
                }
            }
        }
        
        private void CreateIdentityCenterRoles()
        {
            var rolesProps = new LakeFormationIdentityCenterRolesConstructProps
            {
                Environment = _config.Environment,
                AccountId = _config.AccountId,
                IdentityCenterInstanceArn = _config.IdentityCenter.InstanceArn,
                IdentityCenterGroupIds = _config.IdentityCenter.GroupIds ?? new Dictionary<string, string>(),
                CreateProductionRoles = _config.Environment.ToLower() == "production"
            };

            IdentityCenterRoles = new LakeFormationIdentityCenterRolesConstruct(
                this, 
                "IdentityCenterRoles", 
                rolesProps
            );
        }
        
        private void ConfigureDataLakeSettings()
        {
            var lakeFormationConfig = Node.TryGetContext($"environments:{_config.Environment}:lakeFormation");
            var admins = new List<string> { LakeFormationServiceRole.RoleArn };
            
            // Add Identity Center-based admin roles
            var developmentRoles = IdentityCenterRoles.GetDevelopmentRoles();
            if (developmentRoles.ContainsKey("Admin"))
            {
                admins.Add(developmentRoles["Admin"].RoleArn);
            }

            // Add production admin role if this is production environment or if production roles are created
            var productionRoles = IdentityCenterRoles.GetProductionRoles();
            if (productionRoles.ContainsKey("Admin"))
            {
                admins.Add(productionRoles["Admin"].RoleArn);
            }
            
            if (lakeFormationConfig != null)
            {
                var configAdmins = (lakeFormationConfig as Dictionary<string, object>)?["dataLakeAdmins"] as object[];
                if (configAdmins != null)
                {
                    admins.AddRange(configAdmins.Select(a => a.ToString()));
                }
            }
            
            DataLakeSettings = new CfnDataLakeSettings(this, "DataLakeSettings", new CfnDataLakeSettingsProps
            {
                Admins = admins.Select(admin => new CfnDataLakeSettings.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = admin
                }).ToArray(),
                
                // Note: CreateDatabaseDefaultPermissions and CreateTableDefaultPermissions 
                // are not supported for federated principals (SAML/OIDC roles)
                // Catalog creation permissions will be granted via individual resource permissions
                
                TrustedResourceOwners = new[] { _config.AccountId }
            });
        }
        
        private void CreateGlueDatabases()
        {
            // Get tenant ID from configuration (first 8 chars for database naming)
            var tenantId = _config.BucketConfig.SingleTenantId;
            var shortTenantId = tenantId.Length > 8 ? tenantId.Substring(0, 8) : tenantId;
            
            // Create raw database for single tenant FHIR data
            var rawDatabase = new CfnDatabase(this, "ExternalFhirRawDatabase", new CfnDatabaseProps
            {
                CatalogId = _config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = $"fhir_raw_{shortTenantId}_{_config.Environment.ToLower()}",
                    Description = $"Raw FHIR data for tenant ({tenantId}) in {_config.Environment}",
                    LocationUri = $"s3://{_storageStack.RawDataBucket.BucketName}/tenant_{tenantId}/raw/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "json",
                        ["dataFormat"] = "ndjson",
                        ["tenantId"] = tenantId,
                        ["partitionKeys"] = "source_system,import_date"
                    }
                }
            });
            
            Databases.Add(rawDatabase);
            
            // Create curated database for processed FHIR data ready for HealthLake import
            var curatedDatabase = new CfnDatabase(this, "ExternalFhirCuratedDatabase", new CfnDatabaseProps
            {
                CatalogId = _config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = $"fhir_curated_{shortTenantId}_{_config.Environment.ToLower()}",
                    Description = $"Processed FHIR data for tenant ({tenantId}) ready for HealthLake import",
                    LocationUri = $"s3://{_storageStack.CuratedDataBucket.BucketName}/tenant_{tenantId}/curated/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "json",
                        ["dataFormat"] = "ndjson",
                        ["tenantId"] = tenantId,
                        ["partitionKeys"] = "import_date",
                        ["purpose"] = "healthlake-import-staging",
                        ["healthLakeDatastoreIds"] = string.Join(",", _config.HealthLake.Select(h => h.DatastoreId))
                    }
                }
            });
            
            Databases.Add(curatedDatabase);
            
            // Note: HealthLake will create its own Glue catalog with Iceberg tables after import
            // Those databases/tables will be named according to HealthLake's convention
            // and will support SQL queries via Athena with ACID transactions
            
            // Create metadata database for tracking imports and ETL jobs for this tenant
            var metadataDatabase = new CfnDatabase(this, "MetadataDatabase", new CfnDatabaseProps
            {
                CatalogId = _config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = $"fhir_metadata_{shortTenantId}_{_config.Environment.ToLower()}",
                    Description = $"Metadata for tenant ({tenantId}) FHIR imports and ETL jobs",
                    LocationUri = $"s3://{_storageStack.RawDataBucket.BucketName}/tenant_{tenantId}/metadata/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "parquet",
                        ["compressionType"] = "snappy",
                        ["tenantId"] = tenantId
                    }
                }
            });
            
            Databases.Add(metadataDatabase);
        }
        
        private void RegisterS3Locations()
        {
            new Amazon.CDK.AWS.LakeFormation.CfnResource(this, "RawDataResource", new Amazon.CDK.AWS.LakeFormation.CfnResourceProps
            {
                ResourceArn = _storageStack.RawDataBucket.BucketArn,
                RoleArn = LakeFormationServiceRole.RoleArn,
                UseServiceLinkedRole = false
            });
            
            new Amazon.CDK.AWS.LakeFormation.CfnResource(this, "CuratedDataResource", new Amazon.CDK.AWS.LakeFormation.CfnResourceProps
            {
                ResourceArn = _storageStack.CuratedDataBucket.BucketArn,
                RoleArn = LakeFormationServiceRole.RoleArn,
                UseServiceLinkedRole = false
            });
            
            if (_storageStack.PhiDataBucket != null)
            {
                new Amazon.CDK.AWS.LakeFormation.CfnResource(this, "PhiDataResource", new Amazon.CDK.AWS.LakeFormation.CfnResourceProps
                {
                    ResourceArn = _storageStack.PhiDataBucket.BucketArn,
                    RoleArn = LakeFormationServiceRole.RoleArn,
                    UseServiceLinkedRole = false
                });
            }
        }
        
        private void CreateLakeFormationTags()
        {
            // Single tenant ID from configuration
            var tenantId = _config.BucketConfig.SingleTenantId;
            var tenantNames = _config.HealthLake.Select(h => h.TenantName).ToArray();
            
            var tags = new Dictionary<string, string[]>
            {
                ["Environment"] = new[] { _config.Environment },
                ["DataClassification"] = new[] { "Public", "Internal", "Confidential" },
                ["PHI"] = new[] { "true", "false" },
                ["TenantID"] = new[] { tenantId }, // Single tenant GUID
                ["TenantName"] = tenantNames, // Human-readable tenant names
                ["DataType"] = new[] { "clinical", "research", "operational", "administrative", "reference" },
                ["Sensitivity"] = new[] { "public", "internal", "confidential", "restricted" },
                ["SourceSystem"] = new[] { "epic", "cerner", "allscripts", "healthlake", "external-api" },
                ["HealthLakeDatastore"] = _config.HealthLake.Select(h => h.DatastoreId).ToArray()
            };
            
            if (_config.Environment.ToLower() == "prod" || _config.Environment.ToLower() == "production")
            {
                tags["Compliance"] = new[] { "HIPAA" };
            }
            
            foreach (var tag in tags)
            {
                new Amazon.CDK.AWS.LakeFormation.CfnTag(this, $"LFTag-{tag.Key}", new CfnTagProps
                {
                    TagKey = tag.Key,
                    TagValues = tag.Value
                });
            }
        }
    }
}