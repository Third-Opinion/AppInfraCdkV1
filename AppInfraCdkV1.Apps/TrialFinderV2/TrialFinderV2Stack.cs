using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Stacks.WebApp;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

public class TrialFinderV2Stack : WebApplicationStack
{
    public TrialFinderV2Stack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props, context)
    {
        // Add TrialFinderV2-specific resources here
        CreateTrialFinderSpecificResources(context);
    }

    private void CreateTrialFinderSpecificResources(DeploymentContext context)
    {
        // Think of this like customizing the apartment for specific tenant needs
        CreateTrialDocumentStorage();
        CreateAsyncProcessingQueue();
        CreateNotificationServices();
    }



    private void CreateTrialDocumentStorage()
    {
        // Document storage for trial PDFs, protocols, etc.
        var documentsBucket = new Bucket(this, "DocumentsBucket", new BucketProps
        {
            BucketName = _context.Namer.S3Bucket("documents"),
            Versioned = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            AutoDeleteObjects = false,
            LifecycleRules = new[]
            {
                new LifecycleRule
                {
                    Id = "ArchiveOldVersions",
                    Enabled = true,
                    NoncurrentVersionExpiration
                        = Duration.Days(_context.Environment.IsProductionClass ? 365 : 90),
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

        // Archive bucket for long-term document retention
        var archiveBucket = new Bucket(this, "ArchiveBucket", new BucketProps
        {
            BucketName = _context.Namer.S3Bucket("archive"),
            RemovalPolicy = RemovalPolicy.RETAIN,
            AutoDeleteObjects = false
        });
    }

    private void CreateAsyncProcessingQueue()
    {
        // Dead letter queue for failed processing
        var deadLetterQueue = new Queue(this, "ProcessingDeadLetterQueue", new QueueProps
        {
            QueueName = _context.Namer.SqsQueue("processing-dlq"),
            RetentionPeriod = Duration.Days(14)
        });

        // Main processing queue for trial data imports
        var processingQueue = new Queue(this, "TrialProcessingQueue", new QueueProps
        {
            QueueName = _context.Namer.SqsQueue("processing"),
            VisibilityTimeout = Duration.Minutes(15),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 3
            }
        });

        // High priority queue for urgent updates
        var urgentQueue = new Queue(this, "UrgentProcessingQueue", new QueueProps
        {
            QueueName = _context.Namer.SqsQueue("urgent"),
            VisibilityTimeout = Duration.Minutes(5),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 5
            }
        });
    }

    private void CreateNotificationServices()
    {
        // SNS topic for trial status updates
        var trialUpdatesTopic = new Topic(this, "TrialUpdatesTopic", new TopicProps
        {
            TopicName = _context.Namer.SnsTopics("trial-updates"),
            DisplayName = "Clinical Trial Updates"
        });

        // SNS topic for system alerts
        var alertsTopic = new Topic(this, "SystemAlertsTopic", new TopicProps
        {
            TopicName = _context.Namer.SnsTopics("system-alerts"),
            DisplayName = "System Alerts and Monitoring"
        });

        // SNS topic for user notifications
        var userNotificationsTopic = new Topic(this, "UserNotificationsTopic", new TopicProps
        {
            TopicName = _context.Namer.SnsTopics("user-notifications"),
            DisplayName = "User Notifications"
        });
    }
}