using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.KMS;
using Constructs;
using System.Collections.Generic;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Stacks
{
    public class DataLakeStorageStack : Stack
    {
        public Bucket RawDataBucket { get; private set; }
        public Bucket CuratedDataBucket { get; private set; }
        public Bucket? PhiDataBucket { get; private set; }
        public Bucket LoggingBucket { get; private set; }
        public Key? DataEncryptionKey { get; private set; }
        
        public DataLakeStorageStack(Construct scope, string id, IStackProps props, LakeFormationEnvironmentConfig config) 
            : base(scope, id, props)
        {
            // Create KMS key for encryption (production only for PHI data)
            if (config.Environment.ToLower() == "prod" || config.Environment.ToLower() == "production")
            {
                DataEncryptionKey = new Key(this, "DataLakeEncryptionKey", new KeyProps
                {
                    Description = "KMS key for encrypting PHI data in Lake Formation",
                    EnableKeyRotation = true,
                    RemovalPolicy = RemovalPolicy.RETAIN,
                    Alias = $"alias/{config.BucketConfig.BucketPrefix}-datalake-key"
                });
            }
            
            // Create logging bucket
            LoggingBucket = new Bucket(this, "LoggingBucket", new BucketProps
            {
                BucketName = $"{config.BucketConfig.BucketPrefix}-logs-{config.Environment.ToLower()}-{config.Region}",
                Versioned = false,
                LifecycleRules = new[]
                {
                    new LifecycleRule
                    {
                        Id = "DeleteOldLogs",
                        Expiration = Duration.Days(90),
                        Enabled = true
                    }
                },
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = BucketEncryption.S3_MANAGED,
                RemovalPolicy = RemovalPolicy.RETAIN,
                // Enable ACLs for log delivery
                ObjectOwnership = ObjectOwnership.BUCKET_OWNER_PREFERRED
            });
            
            // Create Raw Data Bucket
            RawDataBucket = new Bucket(this, "RawDataBucket", new BucketProps
            {
                BucketName = $"{config.BucketConfig.BucketPrefix}-raw-{config.Environment.ToLower()}-{config.Region}",
                Versioned = config.BucketConfig.EnableVersioning,
                LifecycleRules = CreateLifecycleRules(config.BucketConfig.Lifecycle),
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = config.BucketConfig.EnableEncryption ? BucketEncryption.S3_MANAGED : BucketEncryption.S3_MANAGED,
                ServerAccessLogsBucket = config.BucketConfig.EnableAccessLogging ? LoggingBucket : null,
                ServerAccessLogsPrefix = config.BucketConfig.EnableAccessLogging ? "raw-data/" : null,
                RemovalPolicy = config.Environment.ToLower() == "prod" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
            });
            
            // Create Curated Data Bucket
            CuratedDataBucket = new Bucket(this, "CuratedDataBucket", new BucketProps
            {
                BucketName = $"{config.BucketConfig.BucketPrefix}-curated-{config.Environment.ToLower()}-{config.Region}",
                Versioned = config.BucketConfig.EnableVersioning,
                LifecycleRules = CreateLifecycleRules(config.BucketConfig.Lifecycle),
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = config.BucketConfig.EnableEncryption ? BucketEncryption.S3_MANAGED : BucketEncryption.S3_MANAGED,
                ServerAccessLogsBucket = config.BucketConfig.EnableAccessLogging ? LoggingBucket : null,
                ServerAccessLogsPrefix = config.BucketConfig.EnableAccessLogging ? "curated-data/" : null,
                RemovalPolicy = config.Environment.ToLower() == "prod" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
            });
            
            // Create PHI Data Bucket (Production only)
            if (config.Environment.ToLower() == "prod" || config.Environment.ToLower() == "production")
            {
                PhiDataBucket = new Bucket(this, "PhiDataBucket", new BucketProps
                {
                    BucketName = $"{config.BucketConfig.BucketPrefix}-phi-{config.Environment.ToLower()}-{config.Region}",
                    Versioned = true,
                    LifecycleRules = new[]
                    {
                        new LifecycleRule
                        {
                            Id = "IntelligentTiering",
                            Transitions = new[]
                            {
                                new Transition
                                {
                                    StorageClass = StorageClass.INTELLIGENT_TIERING,
                                    TransitionAfter = Duration.Days(0)
                                }
                            },
                            Enabled = true
                        }
                    },
                    BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                    Encryption = BucketEncryption.KMS,
                    EncryptionKey = DataEncryptionKey,
                    ServerAccessLogsBucket = LoggingBucket,
                    ServerAccessLogsPrefix = "phi-data/",
                    RemovalPolicy = RemovalPolicy.RETAIN
                });
            }
            
            // Tag all buckets
            Amazon.CDK.Tags.Of(this).Add("Component", "DataLakeStorage");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
            
            // Create outputs
            new CfnOutput(this, "RawDataBucketName", new CfnOutputProps
            {
                Value = RawDataBucket.BucketName,
                Description = "Raw data bucket name",
                ExportName = $"{config.Environment}-raw-data-bucket"
            });
            
            new CfnOutput(this, "CuratedDataBucketName", new CfnOutputProps
            {
                Value = CuratedDataBucket.BucketName,
                Description = "Curated data bucket name",
                ExportName = $"{config.Environment}-curated-data-bucket"
            });
            
            if (PhiDataBucket != null)
            {
                new CfnOutput(this, "PhiDataBucketName", new CfnOutputProps
                {
                    Value = PhiDataBucket.BucketName,
                    Description = "PHI data bucket name",
                    ExportName = $"{config.Environment}-phi-data-bucket"
                });
            }
        }
        
        private LifecycleRule[] CreateLifecycleRules(DataLifecycleConfig lifecycle)
        {
            var rules = new List<LifecycleRule>();
            
            if (lifecycle.EnableIntelligentTiering)
            {
                rules.Add(new LifecycleRule
                {
                    Id = "IntelligentTiering",
                    Transitions = new[]
                    {
                        new Transition
                        {
                            StorageClass = StorageClass.INTELLIGENT_TIERING,
                            TransitionAfter = Duration.Days(0)
                        }
                    },
                    Enabled = true
                });
            }
            else
            {
                if (lifecycle.TransitionToIADays > 0)
                {
                    rules.Add(new LifecycleRule
                    {
                        Id = "TransitionToIA",
                        Transitions = new[]
                        {
                            new Transition
                            {
                                StorageClass = StorageClass.INFREQUENT_ACCESS,
                                TransitionAfter = Duration.Days(lifecycle.TransitionToIADays)
                            }
                        },
                        Enabled = true
                    });
                }
                
                if (lifecycle.TransitionToGlacierDays > 0)
                {
                    rules.Add(new LifecycleRule
                    {
                        Id = "TransitionToGlacier",
                        Transitions = new[]
                        {
                            new Transition
                            {
                                StorageClass = StorageClass.GLACIER,
                                TransitionAfter = Duration.Days(lifecycle.TransitionToGlacierDays)
                            }
                        },
                        Enabled = true
                    });
                }
            }
            
            if (lifecycle.ExpirationDays > 0)
            {
                rules.Add(new LifecycleRule
                {
                    Id = "ExpireOldData",
                    Expiration = Duration.Days(lifecycle.ExpirationDays),
                    Enabled = true
                });
            }
            
            return rules.ToArray();
        }
    }
}