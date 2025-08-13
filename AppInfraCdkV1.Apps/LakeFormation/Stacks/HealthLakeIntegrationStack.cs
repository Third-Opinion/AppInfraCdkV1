using Amazon.CDK;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.StepFunctions;
using Amazon.CDK.AWS.StepFunctions.Tasks;
using Amazon.CDK.AWS.S3;
using Constructs;
using System.Collections.Generic;

namespace AppInfraCdkV1.Apps.LakeFormation.Stacks
{
    public class HealthLakeIntegrationStack : Stack
    {
        public Function ExportFunction { get; private set; }
        public CfnJob GlueETLJob { get; private set; }
        public Role GlueJobRole { get; private set; }
        
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly DataLakeStorageStack _storageStack;
        
        public HealthLakeIntegrationStack(Construct scope, string id, IStackProps props,
            LakeFormationEnvironmentConfig config, DataLakeStorageStack storageStack)
            : base(scope, id, props)
        {
            _config = config;
            _storageStack = storageStack;
            
            CreateHealthLakeExportRole();
            CreateGlueETLJob();
            CreateExportOrchestration();
            
            Amazon.CDK.Tags.Of(this).Add("Component", "HealthLakeIntegration");
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }
        
        private void CreateHealthLakeExportRole()
        {
            // Create IAM role for HealthLake export operations
            var exportRole = new Role(this, "HealthLakeExportRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("healthlake.amazonaws.com"),
                Description = "Role for HealthLake to export data to S3",
                RoleName = $"HealthLakeExportRole-{_config.Environment}"
            });
            
            exportRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:PutObject",
                    "s3:PutObjectAcl",
                    "s3:GetObject",
                    "s3:GetObjectVersion"
                },
                Resources = new[]
                {
                    $"{_storageStack.RawDataBucket.BucketArn}/*"
                }
            }));
            
            exportRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:ListBucket",
                    "s3:GetBucketLocation"
                },
                Resources = new[]
                {
                    _storageStack.RawDataBucket.BucketArn
                }
            }));
            
            // Add KMS permissions if encryption is enabled
            if (_storageStack.DataEncryptionKey != null)
            {
                exportRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Effect = Effect.ALLOW,
                    Actions = new[]
                    {
                        "kms:Decrypt",
                        "kms:GenerateDataKey",
                        "kms:CreateGrant"
                    },
                    Resources = new[] { _storageStack.DataEncryptionKey.KeyArn }
                }));
            }
        }
        
        private void CreateGlueETLJob()
        {
            // Create Glue job role
            GlueJobRole = new Role(this, "GlueETLJobRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("glue.amazonaws.com"),
                Description = "Role for Glue ETL job to process HealthLake exports",
                RoleName = $"GlueETLJobRole-{_config.Environment}",
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSGlueServiceRole")
                }
            });
            
            // Add S3 permissions for reading raw data and writing curated data
            GlueJobRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
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
                    _storageStack.RawDataBucket.BucketArn,
                    $"{_storageStack.RawDataBucket.BucketArn}/*",
                    _storageStack.CuratedDataBucket.BucketArn,
                    $"{_storageStack.CuratedDataBucket.BucketArn}/*"
                }
            }));
            
            // Create Glue job for ETL processing (using C# processor)
            GlueETLJob = new CfnJob(this, "HealthLakeETLJob", new CfnJobProps
            {
                Name = $"healthlake-etl-{_config.Environment}",
                Role = GlueJobRole.RoleArn,
                Command = new CfnJob.JobCommandProperty
                {
                    Name = "glueetl",
                    ScriptLocation = $"s3://{_storageStack.RawDataBucket.BucketName}/glue-scripts/healthlake-etl",
                    PythonVersion = "3"  // Still required even for non-Python scripts
                },
                DefaultArguments = new Dictionary<string, string>
                {
                    ["--TempDir"] = $"s3://{_storageStack.RawDataBucket.BucketName}/glue-temp/",
                    ["--job-bookmark-option"] = "job-bookmark-enable",
                    ["--enable-metrics"] = "",
                    ["--enable-continuous-cloudwatch-log"] = "true",
                    ["--enable-spark-ui"] = "true",
                    ["--spark-event-logs-path"] = $"s3://{_storageStack.RawDataBucket.BucketName}/spark-logs/",
                    ["--RAW_BUCKET"] = _storageStack.RawDataBucket.BucketName,
                    ["--CURATED_BUCKET"] = _storageStack.CuratedDataBucket.BucketName,
                    ["--TENANT_PARTITION_KEY"] = _config.BucketConfig.TenantPartitionKey,
                    ["--ENABLE_MULTI_TENANT"] = _config.HealthLake.EnableMultiTenancy.ToString()
                },
                GlueVersion = "3.0",
                MaxRetries = 1,
                Timeout = 2880, // 48 hours
                MaxCapacity = 10,
                Description = "ETL job to process HealthLake FHIR exports and organize by tenant"
            });
        }
        
        private void CreateExportOrchestration()
        {
            // Create Lambda function for triggering HealthLake exports
            var exportFunctionRole = new Role(this, "ExportFunctionRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });
            
            exportFunctionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "healthlake:StartFHIRExportJob",
                    "healthlake:DescribeFHIRExportJob",
                    "healthlake:ListFHIRExportJobs"
                },
                Resources = new[] { _config.HealthLake.DatastoreArn }
            }));
            
            exportFunctionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "glue:StartJobRun",
                    "glue:GetJobRun",
                    "glue:BatchStopJobRun"
                },
                Resources = new[] { $"arn:aws:glue:{_config.Region}:{_config.AccountId}:job/{GlueETLJob.Name}" }
            }));
            
            ExportFunction = new Function(this, "HealthLakeExportFunction", new FunctionProps
            {
                Runtime = Runtime.DOTNET_8,
                Handler = "AppInfraCdkV1.Tools.HealthLakeETL::AppInfraCdkV1.Tools.HealthLakeETL.HealthLakeETLFunction::FunctionHandler",
                Code = Code.FromAsset($"../AppInfraCdkV1.Tools/HealthLakeETL/bin/Release/net8.0/publish"),
                Description = "Triggers HealthLake exports and initiates ETL processing",
                MemorySize = 1024,
                Role = exportFunctionRole,
                Environment = new Dictionary<string, string>
                {
                    ["DATASTORE_ID"] = _config.HealthLake.DatastoreId,
                    ["OUTPUT_BUCKET"] = _storageStack.RawDataBucket.BucketName,
                    ["GLUE_JOB_NAME"] = GlueETLJob.Name ?? $"healthlake-etl-{_config.Environment}",
                    ["EXPORT_ROLE_ARN"] = $"arn:aws:iam::{_config.AccountId}:role/HealthLakeExportRole-{_config.Environment}",
                    ["KMS_KEY_ID"] = _storageStack.DataEncryptionKey?.KeyId ?? ""
                },
                Timeout = Duration.Minutes(15),
                FunctionName = $"healthlake-export-{_config.Environment}"
            });
            
            // Create EventBridge rule for scheduled exports
            var exportSchedule = new Rule(this, "HealthLakeExportSchedule", new RuleProps
            {
                Schedule = Amazon.CDK.AWS.Events.Schedule.Rate(Duration.Hours(24)), // Daily export
                Description = "Trigger daily HealthLake export to S3",
                RuleName = $"healthlake-export-schedule-{_config.Environment}"
            });
            
            exportSchedule.AddTarget(new LambdaFunction(ExportFunction));
            
            // Output the Lambda function ARN
            new CfnOutput(this, "ExportFunctionArn", new CfnOutputProps
            {
                Value = ExportFunction.FunctionArn,
                Description = "ARN of the HealthLake export Lambda function",
                ExportName = $"{_config.Environment}-healthlake-export-function"
            });
        }
    }
}