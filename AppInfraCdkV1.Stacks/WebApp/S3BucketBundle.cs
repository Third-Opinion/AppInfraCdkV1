using Amazon.CDK.AWS.S3;

namespace AppInfraCdkV1.Stacks.WebApp;

public record S3BucketBundle(
    IBucket AppBucket,
    IBucket UploadsBucket,
    IBucket BackupsBucket
);