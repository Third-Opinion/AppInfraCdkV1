# AWS Lake Formation Identity Center integration

All the code below should be used as example and will be rewritten when our task is executed. It is not meant to be used as is, but rather as a reference for the implementation of the Lake Formation Identity Center integration.

# Lake Formation Implementation Plan with Existing SAML Configuration

## Executive Summary
This streamlined implementation plan leverages your existing Google Workspace SAML integration with AWS to add Lake Formation capabilities. Focus is on extending your current group structure, automating Lake Formation setup with C# CDK, and providing CLI scripts for the Identity Center integration gap. The solution will be integrated into your existing GitHub deployment solution.

## Phase 1: Extend Google Workspace Groups
This will be done manually in Google Workspace.
### Proposed Group Structure Extension

Add these groups to your existing Google Workspace setup:

```yaml
Existing Groups (Keep As-Is):
  - engineering@thirdopinion.io       # General engineering access
  - leadership@thirdopinion.io        # Leadership oversight
  - prod-access@thirdopinion.io       # DevOps production (no PHI)
  - technology@thirdopinion.io        # Technology team

New Lake Formation Groups:
  # Data Access Groups
  - data-analysts-dev@thirdopinion.io      # Development data access
  - data-analysts-phi@thirdopinion.io      # PHI data access (production only)
  
  # Administrative Groups
  - lakeformation-admins@thirdopinion.io   # Lake Formation administrators
  - data-engineers@thirdopinion.io         # ETL and pipeline management
```

### Permission Mapping Strategy

```markdown
| Google Group | AWS IAM Role (Auto-created by Identity Center) | Lake Formation Access |
|-------------|------------------------------------------------|----------------------|
| prod-access@ | AWSReservedSSO_ProdAccess_* | Infrastructure only, no data lake |
| data-analysts-dev@ | AWSReservedSSO_DataAnalystsDev_* | Dev tables, all columns |
| data-analysts-phi@ | AWSReservedSSO_DataAnalystsPHI_* | Prod tables with PHI, all columns |
| lakeformation-admins@ | AWSReservedSSO_LakeFormationAdmins_* | Full Lake Formation admin |
| data-engineers@ | AWSReservedSSO_DataEngineers_* | Create/modify tables, manage ETL |
```

## Phase 2: One-Time Lake Formation Identity Center Integration

### Prerequisites Check Script

```bash
#!/bin/bash
# check-prerequisites.sh

echo "Checking Lake Formation prerequisites..."

# Get Identity Center instance ARN
INSTANCE_ARN=$(aws sso-admin list-instances --query 'Instances[0].InstanceArn' --output text)
echo "✓ Identity Center Instance: $INSTANCE_ARN"

# Get current account ID
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
echo "✓ Account ID: $ACCOUNT_ID"

# Check if Lake Formation is already configured
LF_CONFIG=$(aws lakeformation describe-resource --resource-arn "arn:aws:s3:::*" 2>&1)
if [[ $LF_CONFIG == *"AccessDeniedException"* ]]; then
    echo "⚠ Lake Formation needs initial setup"
else
    echo "✓ Lake Formation already initialized"
fi

# List existing Identity Center groups
echo -e "\nExisting Identity Center Groups:"
aws identitystore list-groups \
    --identity-store-id $(aws sso-admin describe-instance --instance-arn $INSTANCE_ARN --query 'IdentityStoreId' --output text) \
    --query 'Groups[].DisplayName' \
    --output table

echo -e "\nPrerequisites check complete!"
echo "INSTANCE_ARN=$INSTANCE_ARN" > .env.lakeformation
echo "ACCOUNT_ID=$ACCOUNT_ID" >> .env.lakeformation
```

### Manual Integration Command (Run Once Per Account)

```bash
#!/bin/bash
# setup-lakeformation-identity-center.sh

source .env.lakeformation

# Development Account
if [[ "$1" == "dev" ]]; then
    echo "Setting up Lake Formation Identity Center for DEVELOPMENT..."
    aws lakeformation create-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --instance-arn $INSTANCE_ARN \
        --external-filtering '{"Status": "ENABLED", "AuthorizedTargets": []}'
    echo "✓ Development Identity Center integration complete"

# Production Account
elif [[ "$1" == "prod" ]]; then
    echo "Setting up Lake Formation Identity Center for PRODUCTION..."
    aws lakeformation create-lake-formation-identity-center-configuration \
        --catalog-id $ACCOUNT_ID \
        --instance-arn $INSTANCE_ARN \
        --external-filtering '{"Status": "ENABLED", "AuthorizedTargets": []}'
    echo "✓ Production Identity Center integration complete"
else
    echo "Usage: ./setup-lakeformation-identity-center.sh [dev|prod]"
    exit 1
fi
```

## Phase 3: C# CDK Implementation


Add a new app named LakeFormation at the same level as TrialFinderV2
Add a new GroupMappings.cs file
Add a new EnvironmentConfig.vs file

Make all scripts bash or C# apps

### Core CDK Stack Implementation

```csharp
// EnvironmentConfig.cs
namespace ThirdOpinionDataLake.Core.Config
{
    public class EnvironmentConfig
    {
        public string Environment { get; set; }
        public string AccountId { get; set; }
        public string Region { get; set; }
        public bool IsProd => Environment.ToLower() == "prod";
        
        public Dictionary<string, string> GroupToRoleMapping => new()
        {
            ["data-analysts-dev"] = $"arn:aws:iam::{AccountId}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataAnalystsDev_*",
            ["data-analysts-phi"] = $"arn:aws:iam::{AccountId}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataAnalystsPHI_*",
            ["lakeformation-admins"] = $"arn:aws:iam::{AccountId}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_LakeFormationAdmins_*",
            ["data-engineers"] = $"arn:aws:iam::{AccountId}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataEngineers_*",
            ["prod-access"] = $"arn:aws:iam::{AccountId}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_ProdAccess_*"
        };
    }
}

// src/ThirdOpinionDataLake.Core/Stacks/DataLakeStack.cs
using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.IAM;
using ThirdOpinionDataLake.Core.Config;
using ThirdOpinionDataLake.Core.Constructs;
using Constructs;
using System.Collections.Generic;

namespace ThirdOpinionDataLake.Core.Stacks
{
    public class DataLakeStack : Stack
    {
        public DataLakeStack(Construct scope, string id, IStackProps props, EnvironmentConfig config) 
            : base(scope, id, props)
        {
            // Create S3 buckets for data zones
            var storage = new DataLakeStorageConstruct(this, "Storage", config);
            
            // Setup Lake Formation
            var lakeFormation = new LakeFormationSetupConstruct(this, "LakeFormation", new LakeFormationProps
            {
                Config = config,
                RawBucket = storage.RawBucket,
                CuratedBucket = storage.CuratedBucket,
                SensitiveBucket = storage.SensitiveBucket
            });
            
            // Setup permissions based on environment
            var permissions = new PermissionsConstruct(this, "Permissions", new PermissionsProps
            {
                Config = config,
                Database = lakeFormation.Database
            });
            
            // Output important ARNs
            new CfnOutput(this, "RawBucketArn", new CfnOutputProps
            {
                Value = storage.RawBucket.BucketArn,
                Description = "Raw data bucket ARN"
            });
            
            new CfnOutput(this, "DatabaseName", new CfnOutputProps
            {
                Value = lakeFormation.Database.DatabaseInput.Name,
                Description = "Glue database name"
            });
        }
    }
}

// sDataLakeStorageConstruct.cs
using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.KMS;
using Constructs;
using ThirdOpinionDataLake.Core.Config;

namespace ThirdOpinionDataLake.Core.Constructs
{
    public class DataLakeStorageConstruct : Construct
    {
        public IBucket RawBucket { get; }
        public IBucket CuratedBucket { get; }
        public IBucket SensitiveBucket { get; }
        
        public DataLakeStorageConstruct(Construct scope, string id, EnvironmentConfig config) 
            : base(scope, id)
        {
            var isProd = config.IsProd;
            var envSuffix = config.Environment.ToLower();
            
            // Raw data bucket
            RawBucket = new Bucket(this, "RawBucket", new BucketProps
            {
                BucketName = $"thirdopinion-raw-{envSuffix}-{config.Region}",
                Encryption = BucketEncryption.S3_MANAGED,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                RemovalPolicy = isProd ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY,
                LifecycleRules = new[]
                {
                    new LifecycleRule
                    {
                        Id = "DeleteOldRawData",
                        Enabled = true,
                        Expiration = Duration.Days(isProd ? 90 : 30)
                    }
                }
            });
            
            // Curated data bucket
            CuratedBucket = new Bucket(this, "CuratedBucket", new BucketProps
            {
                BucketName = $"thirdopinion-curated-{envSuffix}-{config.Region}",
                Encryption = BucketEncryption.S3_MANAGED,
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                RemovalPolicy = RemovalPolicy.RETAIN,
                Versioned = isProd
            });
            
            // Sensitive/PHI data bucket (production only)
            if (isProd)
            {
                SensitiveBucket = new Bucket(this, "SensitiveBucket", new BucketProps
                {
                    BucketName = $"thirdopinion-phi-{envSuffix}-{config.Region}",
                 
                    Encryption = BucketEncryption.KMS,
                    BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                    RemovalPolicy = RemovalPolicy.RETAIN,
                    Versioned = true,
                    ServerAccessLogsPrefix = "access-logs/"
                });
            }
        }
    }
}

// LakeFormationSetupConstruct.cs
using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.IAM;
using Constructs;
using System.Collections.Generic;
using ThirdOpinionDataLake.Core.Config;

namespace ThirdOpinionDataLake.Core.Constructs
{
    public class LakeFormationProps
    {
        public EnvironmentConfig Config { get; set; }
        public IBucket RawBucket { get; set; }
        public IBucket CuratedBucket { get; set; }
        public IBucket SensitiveBucket { get; set; }
    }
    
    public class LakeFormationSetupConstruct : Construct
    {
        public CfnDatabase Database { get; }
        
        public LakeFormationSetupConstruct(Construct scope, string id, LakeFormationProps props) 
            : base(scope, id)
        {
            var config = props.Config;
            
            // Create Lake Formation admin role
            var adminRole = new Role(this, "LakeFormationAdminRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lakeformation.amazonaws.com"),
                RoleName = $"LakeFormationAdmin-{config.Environment}",
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("AWSLakeFormationDataAdmin"),
                    ManagedPolicy.FromAwsManagedPolicyName("AWSGlueConsoleFullAccess")
                }
            });
            
            // Configure Lake Formation settings
            var dataLakeSettings = new CfnDataLakeSettings(this, "DataLakeSettings", 
                new CfnDataLakeSettingsProps
            {
                Admins = new[]
                {
                    new CfnDataLakeSettings.DataLakePrincipalProperty
                    {
                        DataLakePrincipalIdentifier = adminRole.RoleArn
                    },
                    new CfnDataLakeSettings.DataLakePrincipalProperty
                    {
                        DataLakePrincipalIdentifier = config.GroupToRoleMapping["lakeformation-admins"]
                    }
                },
                TrustedResourceOwners = new[] { config.AccountId }
            });
            
            // Register S3 locations with Lake Formation
            RegisterBucket(props.RawBucket, "RawLocation");
            RegisterBucket(props.CuratedBucket, "CuratedLocation");
            if (props.SensitiveBucket != null)
            {
                RegisterBucket(props.SensitiveBucket, "SensitiveLocation");
            }
            
            // Create Glue database
            Database = new CfnDatabase(this, "GlueDatabase", new CfnDatabaseProps
            {
                CatalogId = config.AccountId,
                DatabaseInput = new CfnDatabase.DatabaseInputProperty
                {
                    Name = $"thirdopinion_{config.Environment.ToLower()}",
                    Description = $"Third Opinion data lake database - {config.Environment}",
                    LocationUri = $"s3://{props.CuratedBucket.BucketName}/"
                }
            });
            
            // Create LF-Tags for data classification
            CreateLFTags(config);
        }
        
        private void RegisterBucket(IBucket bucket, string resourceId)
        {
            new CfnResource(this, resourceId, new CfnResourceProps
            {
                ResourceArn = bucket.BucketArn,
                UseServiceLinkedRole = true
            });
        }
        
        private void CreateLFTags(EnvironmentConfig config)
        {
            // Environment tag
            new CfnTag(this, "EnvironmentTag", new CfnTagProps
            {
                CatalogId = config.AccountId,
                TagKey = "Environment",
                TagValues = new[] { config.Environment }
            });
            
            // Data classification tag
            new CfnTag(this, "DataClassificationTag", new CfnTagProps
            {
                CatalogId = config.AccountId,
                TagKey = "DataClassification",
                TagValues = new[] { "Public", "Internal", "Confidential", "PHI" }
            });
            
            // PHI tag (production only)
            if (config.IsProd)
            {
                new CfnTag(this, "PHITag", new CfnTagProps
                {
                    CatalogId = config.AccountId,
                    TagKey = "ContainsPHI",
                    TagValues = new[] { "Yes", "No" }
                });
            }
        }
    }
}

// PermissionsConstruct.cs
using Amazon.CDK;
using Amazon.CDK.AWS.LakeFormation;
using Amazon.CDK.AWS.Glue;
using Constructs;
using ThirdOpinionDataLake.Core.Config;

namespace ThirdOpinionDataLake.Core.Constructs
{
    public class PermissionsProps
    {
        public EnvironmentConfig Config { get; set; }
        public CfnDatabase Database { get; set; }
    }
    
    public class PermissionsConstruct : Construct
    {
        public PermissionsConstruct(Construct scope, string id, PermissionsProps props) 
            : base(scope, id)
        {
            var config = props.Config;
            
            if (config.IsProd)
            {
                SetupProductionPermissions(config, props.Database);
            }
            else
            {
                SetupDevelopmentPermissions(config, props.Database);
            }
        }
        
        private void SetupProductionPermissions(EnvironmentConfig config, CfnDatabase database)
        {
            // Data analysts PHI - full access to all tables including PHI
            new CfnPrincipalPermissions(this, "DataAnalystsPHIPermissions",
                new CfnPrincipalPermissionsProps
            {
                Permissions = new[] { "SELECT", "DESCRIBE" },
                PermissionsWithGrantOption = new string[] { },
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = config.GroupToRoleMapping["data-analysts-phi"]
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Database = new CfnPrincipalPermissions.DatabaseResourceProperty
                    {
                        CatalogId = config.AccountId,
                        Name = database.DatabaseInput.Name
                    }
                }
            });
            
            // Data engineers - table management
            new CfnPrincipalPermissions(this, "DataEngineersPermissions",
                new CfnPrincipalPermissionsProps
            {
                Permissions = new[] { "CREATE_TABLE", "ALTER", "DROP", "DESCRIBE" },
                PermissionsWithGrantOption = new string[] { },
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = config.GroupToRoleMapping["data-engineers"]
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Database = new CfnPrincipalPermissions.DatabaseResourceProperty
                    {
                        CatalogId = config.AccountId,
                        Name = database.DatabaseInput.Name
                    }
                }
            });
            
            // Note: prod-access (DevOps) group gets NO data permissions
        }
        
        private void SetupDevelopmentPermissions(EnvironmentConfig config, CfnDatabase database)
        {
            // Development - broader permissions for testing
            new CfnPrincipalPermissions(this, "DataAnalystsDevPermissions",
                new CfnPrincipalPermissionsProps
            {
                Permissions = new[] { "ALL" },
                PermissionsWithGrantOption = new string[] { },
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = config.GroupToRoleMapping["data-analysts-dev"]
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Database = new CfnPrincipalPermissions.DatabaseResourceProperty
                    {
                        CatalogId = config.AccountId,
                        Name = database.DatabaseInput.Name
                    }
                }
            });
            
            // Data engineers - full access in dev
            new CfnPrincipalPermissions(this, "DataEngineersDevPermissions",
                new CfnPrincipalPermissionsProps
            {
                Permissions = new[] { "ALL" },
                PermissionsWithGrantOption = new string[] { },
                Principal = new CfnPrincipalPermissions.DataLakePrincipalProperty
                {
                    DataLakePrincipalIdentifier = config.GroupToRoleMapping["data-engineers"]
                },
                Resource = new CfnPrincipalPermissions.ResourceProperty
                {
                    Database = new CfnPrincipalPermissions.DatabaseResourceProperty
                    {
                        CatalogId = config.AccountId,
                        Name = database.DatabaseInput.Name
                    }
                }
            });
        }
    }
}



### cdk.json Configuration

```json
{
  "app": "dotnet run --project src/ThirdOpinionDataLake.Core/ThirdOpinionDataLake.Core.csproj",
  "watch": {
    "include": [
      "**"
    ],
    "exclude": [
      "README.md",
      "cdk*.json",
      "**/*.csproj",
      "**/*.sln",
      "**/*Tests*",
      "**/bin",
      "**/obj"
    ]
  },
  "context": {
    "@aws-cdk/aws-apigateway:usagePlanKeyOrderInsensitiveId": true,
    "@aws-cdk/core:stackRelativeExports": true,
    "@aws-cdk/aws-lambda:recognizeVersionProps": true,
    "@aws-cdk/core:enableStackNameDuplicates": false,
    "aws-cdk:enableDiffNoFail": true,
    "@aws-cdk/core:newStyleStackSynthesis": true,
    "@aws-cdk/aws-s3:grantWriteWithoutAcl": true,
    "@aws-cdk/aws-s3:serverAccessLogsUseBucketPolicy": true,
    "@aws-cdk/aws-route53-patters:useCertificate": true,
    "@aws-cdk/customresources:installLatestAwsSdkDefault": false
  }
}
```

## Phase 4: Deployment Scripts

### Master Deployment Script

```bash
#!/bin/bash
# deploy-data-lake.sh

set -e

ENV=${1:-dev}
ACTION=${2:-deploy}

echo "========================================="
echo "Third Opinion Data Lake Deployment"
echo "Environment: $ENV"
echo "Action: $ACTION"
echo "========================================="

# Load environment variables
if [ -f ".env.$ENV" ]; then
    export $(cat .env.$ENV | xargs)
fi

# Get AWS account info
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
REGION=${AWS_REGION:-us-east-1}

echo "Account: $ACCOUNT_ID"
echo "Region: $REGION"

case $ACTION in
    setup-identity-center)
        echo "Setting up Lake Formation Identity Center integration..."
        ./scripts/setup-lakeformation-identity-center.sh $ENV
        ;;
    
    deploy)
        echo "Deploying CDK stack..."
        npx cdk deploy \
            --context environment=$ENV \
            --context account=$ACCOUNT_ID \
            --context region=$REGION \
            --require-approval never \
            --all
        ;;
    
    grant-permissions)
        echo "Granting Lake Formation permissions..."
        python3 scripts/grant-permissions.py --environment $ENV
        ;;
    
    test)
        echo "Testing permissions..."
        python3 scripts/test-permissions.py --environment $ENV
        ;;
    
    full)
        echo "Running full deployment..."
        ./deploy-data-lake.sh $ENV setup-identity-center
        ./deploy-data-lake.sh $ENV deploy
        ./deploy-data-lake.sh $ENV grant-permissions
        ./deploy-data-lake.sh $ENV test
        ;;
    
    destroy)
        echo "WARNING: Destroying stack..."
        read -p "Are you sure? (yes/no): " confirm
        if [ "$confirm" = "yes" ]; then
            npx cdk destroy \
                --context environment=$ENV \
                --context account=$ACCOUNT_ID \
                --context region=$REGION \
                --all
        fi
        ;;
    
    *)
        echo "Usage: ./deploy-data-lake.sh [dev|prod] [setup-identity-center|deploy|grant-permissions|test|full|destroy]"
        exit 1
        ;;
esac

echo "✓ Complete!"
```

### Permissions Management Script
DO NOT USE Python, use bash or C# for consistency with the rest of the project.

```python
#!/usr/bin/env python3
# scripts/grant-permissions.py

import boto3
import json
import argparse
from typing import Dict, List

class LakeFormationPermissionManager:
    def __init__(self, environment: str):
        self.environment = environment
        self.lf = boto3.client('lakeformation')
        self.sts = boto3.client('sts')
        self.account_id = self.sts.get_caller_identity()['Account']
        
    def grant_table_permissions(self, table_name: str, contains_phi: bool = False):
        """Grant appropriate permissions based on PHI status"""
        
        database_name = f"thirdopinion_{self.environment}"
        
        if self.environment == "prod":
            # Only PHI group gets access to PHI tables in production
            if contains_phi:
                principals = [
                    f"arn:aws:iam::{self.account_id}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataAnalystsPHI_*"
                ]
            else:
                # Non-PHI tables - PHI group still gets access
                principals = [
                    f"arn:aws:iam::{self.account_id}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataAnalystsPHI_*"
                ]
        else:
            # Dev environment - broader access
            principals = [
                f"arn:aws:iam::{self.account_id}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_DataAnalystsDev_*"
            ]
        
        for principal in principals:
            try:
                self.lf.grant_permissions(
                    Principal={'DataLakePrincipalIdentifier': principal},
                    Resource={
                        'Table': {
                            'DatabaseName': database_name,
                            'Name': table_name
                        }
                    },
                    Permissions=['SELECT', 'DESCRIBE']
                )
                print(f"✓ Granted permissions on {table_name} to {principal.split('/')[-1]}")
            except Exception as e:
                print(f"⚠ Error granting permissions: {e}")
    
    def tag_table(self, table_name: str, contains_phi: bool):
        """Apply LF-Tags to tables"""
        
        database_name = f"thirdopinion_{self.environment}"
        
        tags = [
            {
                'TagKey': 'Environment',
                'TagValues': [self.environment.capitalize()]
            },
            {
                'TagKey': 'DataClassification',
                'TagValues': ['PHI' if contains_phi else 'Internal']
            }
        ]
        
        if self.environment == "prod":
            tags.append({
                'TagKey': 'ContainsPHI',
                'TagValues': ['Yes' if contains_phi else 'No']
            })
        
        try:
            self.lf.add_lf_tags_to_resource(
                Resource={
                    'Table': {
                        'DatabaseName': database_name,
                        'Name': table_name
                    }
                },
                LFTags=tags
            )
            print(f"✓ Tagged table {table_name} with PHI={contains_phi}")
        except Exception as e:
            print(f"⚠ Error tagging table: {e}")
    
    def setup_sample_tables(self):
        """Create and configure sample tables for testing"""
        
        sample_tables = [
            {'name': 'patient_demographics', 'contains_phi': True},
            {'name': 'appointment_summary', 'contains_phi': False},
            {'name': 'aggregated_metrics', 'contains_phi': False},
            {'name': 'clinical_notes', 'contains_phi': True}
        ]
        
        for table in sample_tables:
            print(f"\nConfiguring table: {table['name']}")
            self.tag_table(table['name'], table['contains_phi'])
            self.grant_table_permissions(table['name'], table['contains_phi'])

def main():
    parser = argparse.ArgumentParser(description='Manage Lake Formation Permissions')
    parser.add_argument('--environment', required=True, choices=['dev', 'prod'])
    parser.add_argument('--table', help='Specific table to configure')
    parser.add_argument('--contains-phi', action='store_true', help='Table contains PHI')
    parser.add_argument('--setup-samples', action='store_true', help='Setup sample tables')
    
    args = parser.parse_args()
    
    manager = LakeFormationPermissionManager(args.environment)
    
    if args.setup_samples:
        manager.setup_sample_tables()
    elif args.table:
        manager.tag_table(args.table, args.contains_phi)
        manager.grant_table_permissions(args.table, args.contains_phi)
    else:
        print("Specify --table or --setup-samples")

if __name__ == '__main__':
    main()
```

## Phase 5: Testing and Validation

### Permission Validation Script
DO NOT USE Python, use bash or C# for consistency with the rest of the project.

```python
#!/usr/bin/env python3
# scripts/test-permissions.py

import boto3
import argparse
from botocore.exceptions import ClientError

def test_group_permissions(environment: str):
    """Test that groups have correct Lake Formation permissions"""
    
    lf = boto3.client('lakeformation')
    account_id = boto3.client('sts').get_caller_identity()['Account']
    
    test_cases = [
        {
            'group': 'prod-access',
            'should_access_data': False,
            'description': 'DevOps group should NOT have data access'
        },
        {
            'group': 'data-analysts-phi',
            'should_access_data': True,
            'should_access_phi': True,
            'description': 'PHI analysts should access all data including PHI'
        },
        {
            'group': 'data-engineers',
            'should_access_data': True,
            'can_create_tables': True,
            'description': 'Data engineers should manage tables'
        }
    ]
    
    print("Testing Lake Formation Permissions...")
    print("=" * 50)
    
    for test in test_cases:
        # Skip production-only groups in dev environment
        if environment == 'dev' and test['group'] == 'data-analysts-phi':
            continue
            
        principal = f"arn:aws:iam::{account_id}:role/aws-reserved/sso.amazonaws.com/AWSReservedSSO_{test['group'].replace('-', '')}*"
        
        try:
            response = lf.list_permissions(
                Principal={'DataLakePrincipalIdentifier': principal}
            )
            
            has_permissions = len(response.get('PrincipalResourcePermissions', [])) > 0
            
            print(f"\n{test['description']}")
            print(f"  Group: {test['group']}")
            print(f"  Has permissions: {has_permissions}")
            
            if test['should_access_data'] != has_permissions:
                print(f"  ❌ FAILED: Expected {test['should_access_data']}, got {has_permissions}")
            else:
                print(f"  ✅ PASSED")
                
        except ClientError as e:
            print(f"  ⚠ Error testing {test['group']}: {e}")
    
    print("\n" + "=" * 50)
    print("Testing complete!")

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--environment', required=True, choices=['dev', 'prod'])
    args = parser.parse_args()
    
    test_group_permissions(args.environment)
```

## Phase 6: Integration with Existing GitHub Solution

Since this will be integrated with your existing GitHub deployment solution, here's a minimal GitHub Actions workflow snippet to add:

Review the script and ensure it fits your existing deployment structure. This example assumes you have a role to assume for deployments and uses the AWS CDK for infrastructure management.

```yaml
# .github/workflows/deploy-lakeformation.yml (or add to existing workflow)
name: Deploy Lake Formation Components

on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Target environment'
        required: true
        type: choice
        options: [dev, prod]

jobs:
  deploy-lakeformation:
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Configure AWS credentials
        uses: aws-actions/configure-aws-credentials@v2
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: us-east-1
      
      - name: Setup tools
        run: |
          npm install -g aws-cdk
          pip install boto3
      
      - name: Deploy Lake Formation Stack
        run: |
          cd ThirdOpinionDataLake
          cdk deploy --all \
            --context environment=${{ github.event.inputs.environment }} \
            --require-approval never
      
      - name: Configure permissions
        run: |
          python3 scripts/grant-permissions.py \
            --environment ${{ github.event.inputs.environment }} \
            --setup-samples
      
      - name: Validate deployment
        run: |
          python3 scripts/test-permissions.py \
            --environment ${{ github.event.inputs.environment }}
```

## Implementation Tasks Breakdown

### Phase 1: Initial Setup (Day 1)
1. **Create Google Workspace groups**
    - [ ] Add `data-analysts-dev@thirdopinion.io`
    - [ ] Add `data-analysts-phi@thirdopinion.io`
    - [ ] Add `lakeformation-admins@thirdopinion.io`
    - [ ] Add `data-engineers@thirdopinion.io`
    - [ ] Assign initial users to groups
    - [ ] Wait for Identity Center sync (up to 40 minutes)

2. **Verify Identity Center setup**
    - [ ] Run prerequisites check script
    - [ ] Confirm groups appear in Identity Center
    - [ ] Document Instance ARN and Identity Store ID

### Phase 2: Lake Formation Integration (Day 1-2)
3. **Run Identity Center integration**
    - [ ] Execute setup script for dev account
    - [ ] Execute setup script for prod account
    - [ ] Verify integration in console

### Phase 3: CDK Development (Day 2-3)
4. **Setup CDK project**
    - [ ] Create project structure
    - [ ] Implement core constructs
    - [ ] Add environment configurations
    - [ ] Write unit tests

5. **Deploy to development**
    - [ ] Run CDK deploy for dev
    - [ ] Verify S3 buckets created
    - [ ] Confirm Lake Formation settings

### Phase 4: Permissions Configuration (Day 3-4)
6. **Configure permissions**
    - [ ] Run permission grant scripts
    - [ ] Tag tables appropriately
    - [ ] Test group access

7. **Validation**
    - [ ] Run permission tests
    - [ ] Verify DevOps has no data access
    - [ ] Verify PHI access controls

### Phase 5: Production Deployment (Day 4-5)
8. **Production setup**
    - [ ] Review all configurations
    - [ ] Deploy CDK to production
    - [ ] Configure PHI-specific permissions
    - [ ] Enable CloudTrail logging

9. **Integration with existing GitHub solution**
    - [ ] Add Lake Formation deployment to existing workflow
    - [ ] Test integrated deployment
    - [ ] Document integration points

### Phase 6: Documentation and Training (Day 5)
10. **Documentation**
    - [ ] Create runbooks for common operations
    - [ ] Document access procedures
    - [ ] Create troubleshooting guide

## Success Criteria
- ✅ Identity Center groups sync with Google Workspace
- ✅ Lake Formation recognizes Identity Center principals
- ✅ DevOps team (`prod-access@`) cannot access data tables
- ✅ Data analysts can only access appropriate tables (dev or PHI)
- ✅ PHI data is properly protected in production
- ✅ All infrastructure is managed through CDK
- ✅ Integration with existing GitHub deployment works
- ✅ Audit logs capture all data access

## Maintenance Runbook

### Adding a new table
```bash
# 1. Deploy table through CDK or create manually
# 2. Tag the table
python3 scripts/grant-permissions.py \
  --environment prod \
  --table new_table_name \
  --contains-phi  # if applicable

# 3. Test permissions
python3 scripts/test-permissions.py --environment prod
```

### Adding a new group
1. Create group in Google Workspace
2. Wait for sync (up to 40 minutes)
3. Update CDK GroupMappings in EnvironmentConfig.cs
4. Deploy CDK changes
5. Grant appropriate permissions

### Troubleshooting common issues
- **"Insufficient Lake Formation permissions"**: Admin role not registered
- **"Principal not found"**: Identity Center sync pending
- **"Access Denied" for valid group**: Check LF-Tag expressions
- **CDK deployment fails**: Verify deploy role has Lake Formation admin

## Notes for Integration

Since you're integrating this into an existing GitHub solution:

1. **The CDK stack is self-contained** - can be deployed independently or as part of a larger deployment
2. **Permission scripts are idempotent** - safe to run multiple times
3. **No cross-stack dependencies** - Lake Formation stack doesn't depend on other infrastructure
4. **Environment isolation** - Dev and prod are completely separate
5. **Rollback safe** - CDK manages all resources with proper deletion policies