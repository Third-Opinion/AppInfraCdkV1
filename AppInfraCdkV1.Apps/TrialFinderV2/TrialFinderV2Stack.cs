using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.ExternalResources;
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
        // Validate external dependencies before creating resources
        ValidateExternalDependencies(context);
        
        // Add TrialFinderV2-specific resources here
        CreateTrialFinderSpecificResources(context);
    }

    private void CreateTrialFinderSpecificResources(DeploymentContext context)
    {
        CreateTrialDocumentStorage(context);
        // CreateAsyncProcessingQueue(context);
        // CreateNotificationServices(context);
    }



    private void CreateTrialDocumentStorage(DeploymentContext context)
    {
        // Document storage for trial PDFs, protocols, etc.
        Bucket documentsBucket = new Bucket(this, "CDKTestDocumentsBucket", new BucketProps
        {
            BucketName = context.Namer.S3Bucket(StoragePurpose.Documents),
            Versioned = false,
            RemovalPolicy = RemovalPolicy.RETAIN_ON_UPDATE_OR_DELETE,
            AutoDeleteObjects = false,
            EventBridgeEnabled = false,
            // LifecycleRules = new ILifecycleRule[]
            // {
            //     new LifecycleRule
            //     {
            //         Id = "ArchiveOldVersions",
            //         Enabled = true,
            //         NoncurrentVersionExpiration
            //             = Duration.Days(context.Environment.IsProductionClass ? 365 : 90),
            //         Transitions = new[]
            //         {
            //             new Transition
            //             {
            //                 StorageClass = StorageClass.INFREQUENT_ACCESS,
            //                 TransitionAfter = Duration.Days(30)
            //             },
            //             new Transition
            //             {
            //                 StorageClass = StorageClass.GLACIER,
            //                 TransitionAfter = Duration.Days(90)
            //             }
            //         }
            //     }
            // }
        });
    }

    private void CreateAsyncProcessingQueue(DeploymentContext context)
    {
        // Dead letter queue for failed processing
        var deadLetterQueue = new Queue(this, "ProcessingDeadLetterQueue", new QueueProps
        {
            QueueName = context.Namer.SqsQueue(QueuePurpose.DeadLetter),
            RetentionPeriod = Duration.Days(14)
        });

        // Main processing queue for trial data imports
        var processingQueue = new Queue(this, "TrialProcessingQueue", new QueueProps
        {
            QueueName = context.Namer.SqsQueue(QueuePurpose.Processing),
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
            QueueName = context.Namer.SqsQueue(QueuePurpose.Urgent),
            VisibilityTimeout = Duration.Minutes(5),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = deadLetterQueue,
                MaxReceiveCount = 5
            }
        });
    }

    private void CreateNotificationServices(DeploymentContext context)
    {
        // SNS topic for trial status updates
        var trialUpdatesTopic = new Topic(this, "TrialUpdatesTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.TrialUpdates),
            DisplayName = "Clinical Trial Updates"
        });

        // SNS topic for system alerts
        var alertsTopic = new Topic(this, "SystemAlertsTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.SystemAlerts),
            DisplayName = "System Alerts and Monitoring"
        });

        // SNS topic for user notifications
        var userNotificationsTopic = new Topic(this, "UserNotificationsTopic", new TopicProps
        {
            TopicName = context.Namer.SnsTopics(NotificationPurpose.UserNotifications),
            DisplayName = "User Notifications"
        });
    }

    /// <summary>
    /// Validates that all required external resources exist and are properly configured
    /// </summary>
    private void ValidateExternalDependencies(DeploymentContext context)
    {
        Console.WriteLine("🔍 Validating TrialFinderV2 external dependencies...");
        
        var requirements = new TrialFinderV2ExternalDependencies();
        var requirementsList = requirements.GetRequirements(context);
        
        // Check if external resources are expected to exist
        if (!context.AllExternalResourcesValid && requirementsList.Any())
        {
            Console.WriteLine("⚠️  External resource validation not completed - assuming resources exist");
            Console.WriteLine("   Run external resource validation before deployment in production");
            
            // In development/testing, we'll proceed with warnings
            foreach (var requirement in requirementsList)
            {
                Console.WriteLine($"   Expected: {requirement.ResourceType} - {requirement.ExpectedName}");
                Console.WriteLine($"   ARN: {requirement.ExpectedArn}");
            }
        }
        else if (context.AllExternalResourcesValid)
        {
            Console.WriteLine("✅ All TrialFinderV2 external dependencies validated");
        }
        else if (context.ExternalResourceErrors.Any())
        {
            Console.WriteLine("❌ External resource validation failed:");
            foreach (var error in context.ExternalResourceErrors)
            {
                Console.WriteLine($"   {error}");
            }
            
            // Generate creation commands for missing resources
            Console.WriteLine("\n📋 To create missing resources, run:");
            var validator = new ExternalResourceValidator();
            foreach (var requirement in requirementsList)
            {
                var commands = validator.GenerateCreationCommands(requirement, context);
                Console.WriteLine(commands);
                Console.WriteLine();
            }
            
            throw new InvalidOperationException("External resource dependencies not met. See console output for details.");
        }
    }
}