using Amazon.CDK;
using Amazon.CDK.AWS.HealthLake;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;
using System;
using System.Collections.Generic;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Constructs
{
    /// <summary>
    /// Creates test HealthLake instances for single-tenant architecture
    /// Each instance is dedicated to a single tenant/customer
    /// </summary>
    public class HealthLakeTestInstanceConstruct : Construct
    {
        public CfnFHIRDatastore Datastore { get; private set; }
        public Role DatastoreRole { get; private set; }
        
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly Bucket _rawBucket;
        private readonly Bucket _curatedBucket;
        
        public HealthLakeTestInstanceConstruct(Construct scope, string id, 
            LakeFormationEnvironmentConfig config, HealthLakeConfig healthLakeConfig, Bucket rawBucket, Bucket curatedBucket) 
            : base(scope, id)
        {
            _config = config;
            _rawBucket = rawBucket;
            _curatedBucket = curatedBucket;
            
            CreateDatastoreRole(healthLakeConfig);
            CreateHealthLakeDatastore(healthLakeConfig);
        }
        
        private void CreateDatastoreRole(HealthLakeConfig healthLakeConfig)
        {
            DatastoreRole = new Role(this, "DatastoreRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("healthlake.amazonaws.com"),
                Description = $"Role for HealthLake datastore {healthLakeConfig.DatastoreId}",
                RoleName = $"HealthLakeRole-{healthLakeConfig.TenantId.Substring(0, 8)}-{_config.Environment}"
            });
            
            // Grant access to the raw bucket for imports (reading FHIR data)
            DatastoreRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:GetObject",
                    "s3:ListBucket"
                },
                Resources = new[]
                {
                    _rawBucket.BucketArn,
                    $"{_rawBucket.BucketArn}/tenants/{healthLakeConfig.TenantId}/*"
                }
            }));
            
            // Grant access to the curated bucket for exports
            DatastoreRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:GetObject",
                    "s3:ListBucket"
                },
                Resources = new[]
                {
                    _curatedBucket.BucketArn,
                    $"{_curatedBucket.BucketArn}/tenant_{healthLakeConfig.TenantId}/*"
                }
            }));
            
            // Grant access to write export data and import results
            DatastoreRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:PutObject",
                    "s3:PutObjectAcl"
                },
                Resources = new[]
                {
                    $"{_rawBucket.BucketArn}/tenants/{healthLakeConfig.TenantId}/fhir/import-results/*",
                    $"{_curatedBucket.BucketArn}/tenant_{healthLakeConfig.TenantId}/exports/*"
                }
            }));
            
            // Grant CloudWatch Logs permissions
            DatastoreRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "logs:CreateLogGroup",
                    "logs:CreateLogStream",
                    "logs:PutLogEvents"
                },
                Resources = new[] { "*" }
            }));
            
            // Grant KMS permissions for S3 encryption
            DatastoreRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "kms:DescribeKey",
                    "kms:Decrypt",
                    "kms:GenerateDataKey"
                },
                Resources = new[] { "*" }
            }));
        }
        
        private void CreateHealthLakeDatastore(HealthLakeConfig healthLakeConfig)
        {
            var datastoreName = $"{healthLakeConfig.TenantName}-{_config.Environment}".ToLower()
                .Replace(" ", "-")
                .Replace("_", "-");
            
            Datastore = new CfnFHIRDatastore(this, "FHIRDatastore", new CfnFHIRDatastoreProps
            {
                DatastoreName = datastoreName,
                DatastoreTypeVersion = "R4",
                SseConfiguration = new CfnFHIRDatastore.SseConfigurationProperty
                {
                    KmsEncryptionConfig = new CfnFHIRDatastore.KmsEncryptionConfigProperty
                    {
                        CmkType = "AWS_OWNED_KMS_KEY"
                    }
                },
                Tags = new[]
                {
                    new CfnTag { Key = "Environment", Value = _config.Environment },
                    new CfnTag { Key = "TenantId", Value = healthLakeConfig.TenantId },
                    new CfnTag { Key = "TenantName", Value = healthLakeConfig.TenantName },
                    new CfnTag { Key = "Purpose", Value = "SingleTenantFHIR" },
                    new CfnTag { Key = "PHI", Value = healthLakeConfig.MarkAsPHI.ToString().ToLower() },
                    new CfnTag { Key = "ManagedBy", Value = "CDK" }
                }
            });
            
            // Output the datastore details
            new CfnOutput(this, "DatastoreId", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreId,
                Description = $"HealthLake Datastore ID for tenant {healthLakeConfig.TenantName}",
                ExportName = $"HealthLake-{healthLakeConfig.TenantId.Substring(0, 8)}-DatastoreId"
            });
            
            new CfnOutput(this, "DatastoreArn", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreArn,
                Description = $"HealthLake Datastore ARN for tenant {healthLakeConfig.TenantName}",
                ExportName = $"HealthLake-{healthLakeConfig.TenantId.Substring(0, 8)}-DatastoreArn"
            });
            
            new CfnOutput(this, "DatastoreEndpoint", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreEndpoint,
                Description = $"HealthLake FHIR API endpoint for tenant {healthLakeConfig.TenantName}",
                ExportName = $"HealthLake-{healthLakeConfig.TenantId.Substring(0, 8)}-Endpoint"
            });
        }
    }
}