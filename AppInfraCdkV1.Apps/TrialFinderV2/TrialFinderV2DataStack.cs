using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public class TrialFinderV2DataStack : Stack
{
    private readonly DeploymentContext _context;
    private Bucket? _documentsBucket;
    private IBucket? _appBucket;
    private IBucket? _uploadsBucket;
    private IBucket? _backupsBucket;
    
    public TrialFinderV2DataStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        
        // Create data storage resources
        CreateTrialDocumentStorage(context);
        
        // Future: Add RDS database when needed
        // CreateDatabase(context);
        
        // Export outputs for other stacks
        ExportStackOutputs();
    }

    /// <summary>
    /// Create S3 buckets for trial document storage
    /// </summary>
    private void CreateTrialDocumentStorage(DeploymentContext context)
    {
        // Document storage for trial PDFs, protocols, etc.
        _documentsBucket = new Bucket(this, "TrialDocumentsBucket", new BucketProps
        {
            // Use auto-generated bucket name to avoid conflicts
            Versioned = false,
            RemovalPolicy = RemovalPolicy.RETAIN_ON_UPDATE_OR_DELETE,
            AutoDeleteObjects = false,
            EventBridgeEnabled = false,
            LifecycleRules = new ILifecycleRule[]
            {
                new LifecycleRule
                {
                    Id = "ArchiveOldVersions",
                    Enabled = true,
                    Transitions = new[]
                    {
                        new Transition
                        {
                            StorageClass = StorageClass.INFREQUENT_ACCESS,
                            TransitionAfter = Duration.Days(30)
                        },
                        new Transition
                        {
                            StorageClass = StorageClass.GLACIER,
                            TransitionAfter = Duration.Days(90)
                        }
                    }
                }
            }
        });

        // Import existing buckets instead of creating new ones to avoid conflicts
        // App-specific bucket for application data
        _appBucket = Bucket.FromBucketName(this, "TrialFinderAppBucket", context.Namer.S3Bucket(StoragePurpose.App));

        // Uploads bucket for user uploads
        _uploadsBucket = Bucket.FromBucketName(this, "TrialFinderUploadsBucket", context.Namer.S3Bucket(StoragePurpose.Uploads));

        // Backups bucket for database and application backups
        _backupsBucket = Bucket.FromBucketName(this, "TrialFinderBackupsBucket", context.Namer.S3Bucket(StoragePurpose.Backups));
    }

    /// <summary>
    /// Future: Create RDS database when needed
    /// </summary>
    private void CreateDatabase(DeploymentContext context)
    {
        // TODO: Add RDS database creation when database requirements are defined
        // This would include:
        // - RDS instance or Aurora cluster
        // - Database security groups
        // - Parameter groups
        // - Subnet groups
        // - Backup configuration
        // - Monitoring and logging
    }

    /// <summary>
    /// Export stack outputs for consumption by other stacks
    /// </summary>
    private void ExportStackOutputs()
    {
        // Export bucket names for application use
        if (_documentsBucket != null)
        {
            new CfnOutput(this, "DocumentsBucketName", new CfnOutputProps
            {
                Value = _documentsBucket.BucketName,
                ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-documents-bucket-name",
                Description = "Name of the trial documents S3 bucket"
            });
        }

        if (_appBucket != null)
        {
            new CfnOutput(this, "AppBucketName", new CfnOutputProps
            {
                Value = _appBucket.BucketName,
                ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-app-bucket-name",
                Description = "Name of the application data S3 bucket"
            });
        }

        if (_uploadsBucket != null)
        {
            new CfnOutput(this, "UploadsBucketName", new CfnOutputProps
            {
                Value = _uploadsBucket.BucketName,
                ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-uploads-bucket-name",
                Description = "Name of the uploads S3 bucket"
            });
        }

        if (_backupsBucket != null)
        {
            new CfnOutput(this, "BackupsBucketName", new CfnOutputProps
            {
                Value = _backupsBucket.BucketName,
                ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-backups-bucket-name",
                Description = "Name of the backups S3 bucket"
            });
        }

        // Future: Export database connection information when RDS is added
        // new CfnOutput(this, "DatabaseEndpoint", new CfnOutputProps
        // {
        //     Value = database.InstanceEndpoint.Hostname,
        //     ExportName = $"{_context.Environment.Name}-{_context.Application.Name}-db-endpoint",
        //     Description = "Database endpoint for application connections"
        // });
    }
}