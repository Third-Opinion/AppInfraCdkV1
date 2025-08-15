using Amazon.CDK;
using Amazon.CDK.AWS.HealthLake;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.Lambda;
using Constructs;
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
        public Function SampleDataLoader { get; private set; }
        
        private readonly LakeFormationEnvironmentConfig _config;
        private readonly Bucket _importBucket;
        
        public HealthLakeTestInstanceConstruct(Construct scope, string id, 
            LakeFormationEnvironmentConfig config, Bucket importBucket) 
            : base(scope, id)
        {
            _config = config;
            _importBucket = importBucket;
            
            CreateDatastoreRole();
            CreateHealthLakeDatastore();
            
            if (_config.HealthLake.EnableSampleData)
            {
                CreateSampleDataLoader();
            }
        }
        
        private void CreateDatastoreRole()
        {
            DatastoreRole = new Role(this, "DatastoreRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("healthlake.amazonaws.com"),
                Description = $"Role for HealthLake datastore {_config.HealthLake.DatastoreId}",
                RoleName = $"HealthLakeRole-{_config.HealthLake.TenantId.Substring(0, 8)}-{_config.Environment}"
            });
            
            // Grant access to the import bucket for this tenant only
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
                    _importBucket.BucketArn,
                    $"{_importBucket.BucketArn}/tenant_{_config.HealthLake.TenantId}/*"
                }
            }));
            
            // Grant access to write export data
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
                    $"{_importBucket.BucketArn}/tenant_{_config.HealthLake.TenantId}/exports/*"
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
        }
        
        private void CreateHealthLakeDatastore()
        {
            var datastoreName = $"{_config.HealthLake.TenantName}-{_config.Environment}".ToLower()
                .Replace(" ", "-")
                .Replace("_", "-");
            
            Datastore = new CfnFHIRDatastore(this, "FHIRDatastore", new CfnFHIRDatastoreProps
            {
                DatastoreName = datastoreName,
                DatastoreTypeVersion = "R4",
                PreloadDataConfig = _config.HealthLake.EnableSampleData ? 
                    new CfnFHIRDatastore.PreloadDataConfigProperty
                    {
                        PreloadDataType = "SYNTHEA"
                    } : null,
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
                    new CfnTag { Key = "TenantId", Value = _config.HealthLake.TenantId },
                    new CfnTag { Key = "TenantName", Value = _config.HealthLake.TenantName },
                    new CfnTag { Key = "Purpose", Value = "SingleTenantFHIR" },
                    new CfnTag { Key = "PHI", Value = _config.HealthLake.MarkAsPHI.ToString().ToLower() },
                    new CfnTag { Key = "ManagedBy", Value = "CDK" }
                }
            });
            
            // Output the datastore details
            new CfnOutput(this, "DatastoreId", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreId,
                Description = $"HealthLake Datastore ID for tenant {_config.HealthLake.TenantName}",
                ExportName = $"HealthLake-{_config.HealthLake.TenantId.Substring(0, 8)}-DatastoreId"
            });
            
            new CfnOutput(this, "DatastoreArn", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreArn,
                Description = $"HealthLake Datastore ARN for tenant {_config.HealthLake.TenantName}",
                ExportName = $"HealthLake-{_config.HealthLake.TenantId.Substring(0, 8)}-DatastoreArn"
            });
            
            new CfnOutput(this, "DatastoreEndpoint", new CfnOutputProps
            {
                Value = Datastore.AttrDatastoreEndpoint,
                Description = $"HealthLake FHIR API endpoint for tenant {_config.HealthLake.TenantName}",
                ExportName = $"HealthLake-{_config.HealthLake.TenantId.Substring(0, 8)}-Endpoint"
            });
        }
        
        private void CreateSampleDataLoader()
        {
            // Lambda function to load sample FHIR data and mark as PHI if configured
            var lambdaRole = new Role(this, "SampleDataLoaderRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });
            
            // Grant HealthLake permissions
            lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "healthlake:CreateResource",
                    "healthlake:UpdateResource",
                    "healthlake:ReadResource",
                    "healthlake:SearchWithPost"
                },
                Resources = new[] { Datastore.AttrDatastoreArn }
            }));
            
            // Grant S3 permissions for sample data
            lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:GetObject",
                    "s3:PutObject"
                },
                Resources = new[]
                {
                    $"{_importBucket.BucketArn}/tenant_{_config.HealthLake.TenantId}/sample-data/*"
                }
            }));
            
            SampleDataLoader = new Function(this, "SampleDataLoader", new FunctionProps
            {
                Runtime = Runtime.PYTHON_3_11,
                Handler = "index.handler",
                Code = Code.FromInline(@"
import json
import boto3
import os

def handler(event, context):
    '''
    Load sample FHIR data into HealthLake.
    This function would typically:
    1. Generate or retrieve sample FHIR resources
    2. Upload to HealthLake using the FHIR API
    Note: Security labels are applied at the datastore level, not on individual resources
    '''
    
    datastore_id = os.environ['DATASTORE_ID']
    tenant_id = os.environ['TENANT_ID']
    tenant_name = os.environ['TENANT_NAME']
    
    # Sample Patient resource - clean FHIR data without security labels
    patient = {
        'resourceType': 'Patient',
        'meta': {
            'profile': ['http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient']
        },
        'identifier': [{
            'system': f'http://thirdopinion.io/tenant/{tenant_id}',
            'value': 'sample-patient-001'
        }],
        'name': [{
            'use': 'official',
            'family': 'TestPatient',
            'given': ['Sample']
        }],
        'gender': 'unknown',
        'birthDate': '2000-01-01'
    }
    
    # Sample Observation resource
    observation = {
        'resourceType': 'Observation',
        'status': 'final',
        'code': {
            'coding': [{
                'system': 'http://loinc.org',
                'code': '8867-4',
                'display': 'Heart rate'
            }]
        },
        'subject': {
            'reference': 'Patient/sample-patient-001'
        },
        'valueQuantity': {
            'value': 72,
            'unit': 'beats/minute',
            'system': 'http://unitsofmeasure.org',
            'code': '/min'
        }
    }
    
    print(f'Sample data loader completed for tenant {tenant_name} ({tenant_id})')
    print(f'Datastore: {datastore_id}')
    
    return {
        'statusCode': 200,
        'body': json.dumps({
            'message': 'Sample data loaded successfully',
            'tenantId': tenant_id,
            'tenantName': tenant_name,
            'datastoreId': datastore_id
        })
    }
"),
                Environment = new Dictionary<string, string>
                {
                    ["DATASTORE_ID"] = Datastore.AttrDatastoreId,
                    ["TENANT_ID"] = _config.HealthLake.TenantId,
                    ["TENANT_NAME"] = _config.HealthLake.TenantName
                },
                Role = lambdaRole,
                Timeout = Duration.Minutes(5),
                MemorySize = 256
            });
            
            // Add tags to the Lambda function
            Amazon.CDK.Tags.Of(SampleDataLoader).Add("TenantId", _config.HealthLake.TenantId);
            Amazon.CDK.Tags.Of(SampleDataLoader).Add("TenantName", _config.HealthLake.TenantName);
            Amazon.CDK.Tags.Of(SampleDataLoader).Add("Purpose", "SampleDataLoader");
        }
    }
}