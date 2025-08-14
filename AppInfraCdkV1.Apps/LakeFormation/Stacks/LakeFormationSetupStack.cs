using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.IAM;
using Constructs;
using System.Collections.Generic;
using System.Linq;

namespace AppInfraCdkV1.Apps.LakeFormation.Stacks
{
    public class LakeFormationSetupStack : Stack
    {
        public List<CfnDatabase> Databases { get; private set; }
        public Role LakeFormationServiceRole { get; private set; }
        public CfnDataLakeSettings DataLakeSettings { get; private set; }
        
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly DataLakeStorageStack _storageStack;
        
        public LakeFormationSetupStack(Construct scope, string id, IStackProps props, 
            LakeFormationEnvironmentConfig config, DataLakeStorageStack storageStack) 
            : base(scope, id, props)
        {
            _config = config;
            _storageStack = storageStack;
            Databases = new List<CfnDatabase>();
            
            CreateLakeFormationServiceRole();
            ConfigureDataLakeSettings();
            CreateGlueDatabases();
            RegisterS3Locations();
            // Temporarily disabled - requires manual Lake Formation admin setup first
            // CreateLakeFormationTags();
            
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
        
        private void ConfigureDataLakeSettings()
        {
            var lakeFormationConfig = Node.TryGetContext($"environments:{_config.Environment}:lakeFormation");
            var admins = new List<string> { LakeFormationServiceRole.RoleArn };
            
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
                TrustedResourceOwners = new[] { _config.AccountId }
            });
        }
        
        private void CreateGlueDatabases()
        {
            // Create raw database for external FHIR data
            var rawDatabase = new CfnDatabase(this, "ExternalFhirRawDatabase", new CfnDatabaseProps
            {
                CatalogId = _config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = "external_fhir_raw",
                    Description = $"Raw FHIR data from external sources for {_config.Environment} environment",
                    LocationUri = $"s3://{_storageStack.RawDataBucket.BucketName}/raw/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "json",
                        ["dataFormat"] = "ndjson",
                        ["partitionKeys"] = $"{_config.BucketConfig.TenantPartitionKey},source_system,import_date",
                        ["multiTenant"] = "true"
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
                    Name = "external_fhir_curated",
                    Description = $"Processed FHIR data ready for HealthLake import for {_config.Environment} environment",
                    LocationUri = $"s3://{_storageStack.CuratedDataBucket.BucketName}/curated/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "json",
                        ["dataFormat"] = "ndjson",
                        ["partitionKeys"] = $"{_config.BucketConfig.TenantPartitionKey},import_date",
                        ["multiTenant"] = "true",
                        ["purpose"] = "healthlake-import-staging"
                    }
                }
            });
            
            Databases.Add(curatedDatabase);
            
            // Note: HealthLake will create its own Glue catalog with Iceberg tables after import
            // Those databases/tables will be named according to HealthLake's convention
            // and will support SQL queries via Athena with ACID transactions
            
            // Create metadata database for tracking imports and ETL jobs
            var metadataDatabase = new CfnDatabase(this, "MetadataDatabase", new CfnDatabaseProps
            {
                CatalogId = _config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = $"fhir_import_metadata_{_config.Environment.ToLower()}",
                    Description = "Metadata database for tracking FHIR imports and ETL jobs",
                    LocationUri = $"s3://{_storageStack.RawDataBucket.BucketName}/metadata/",
                    Parameters = new Dictionary<string, string>
                    {
                        ["classification"] = "parquet",
                        ["compressionType"] = "snappy"
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
            var tags = new Dictionary<string, string[]>
            {
                ["Environment"] = new[] { _config.Environment },
                ["DataClassification"] = new[] { "Public", "Internal", "Confidential" },
                ["PHI"] = new[] { "true", "false" }
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