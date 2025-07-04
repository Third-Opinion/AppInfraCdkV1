# GitHub Environment Secrets Setup

This document explains how to configure GitHub Environment Secrets for managing AWS credentials across different environments.

## Overview

The repository now uses GitHub Environment Secrets to manage AWS credentials for two separate AWS accounts:
- **dev-alpha**: Development account (used by `develop` branch)
- **prd-alpha**: Production account (used by `master` branch)

## GitHub Environment Configuration

### 1. Create Environments

Navigate to your repository's **Settings** → **Environments** and create:

#### Development Environment
- **Name**: `development`
- **Deployment branches**: `develop` (selected branches)
- **Environment secrets**:
  - `AWS_ACCESS_KEY_ID`: Your dev-alpha access key
  - `AWS_SECRET_ACCESS_KEY`: Your dev-alpha secret key

#### Production Environment  
- **Name**: `production`
- **Deployment branches**: `master` (selected branches)
- **Environment secrets**:
  - `AWS_ACCESS_KEY_ID`: Your prd-alpha access key
  - `AWS_SECRET_ACCESS_KEY`: Your prd-alpha secret key

### 2. Environment Protection Rules (Optional)

For the production environment, consider adding:
- **Required reviewers**: Team members who must approve production deployments
- **Wait timer**: Delay before deployment starts
- **Deployment protection rules**: Additional custom rules

## Workflow Mapping

| Workflow File | Branch | Environment | AWS Account |
|---------------|--------|-------------|-------------|
| `infrastructure-pr.yml` | Any | `development` | dev-alpha |
| `deploy-dev.yml` | `develop` | `development` | dev-alpha |
| `deploy-prod.yml` | `master` | `production` | prd-alpha |

## Security Benefits

- ✅ **Credential Separation**: Each environment has its own AWS credentials
- ✅ **Branch Protection**: Environments are tied to specific branches
- ✅ **Audit Trail**: GitHub tracks all environment deployments
- ✅ **Access Control**: Environment-specific permissions and approvals
- ✅ **Secret Management**: Centralized secret management per environment

## IAM User Setup

### Naming Convention

**Standard Pattern**: `{env}-g-{resource-type}-g-{purpose}`

Where:
- **{env}**: Environment prefix (`dev`, `prod`)
- **g**: Global scope indicator (not application-specific)
- **{resource-type}**: AWS resource type (`user`, `policy`, `role`)
- **g**: Global AWS resources (not region-specific)
- **{purpose}**: Specific purpose (`gh-cdk-deployer`, `gh-ecs-deployer`)

**Global Scope Indicators**:
- **First `g`**: Indicates account-wide resources (not app-specific)
- **Second `g`**: Indicates global AWS resources (not region-specific)
- **Rationale**: GitHub Actions users/policies deploy any application in the account, not just TrialFinderV2

### Required IAM Users

Create dedicated IAM users in each AWS account:

#### Development Account (dev-alpha)
- **CDK Deployer Username**: `dev-g-user-g-gh-cdk-deployer`
- **CDK Deployer Policy**: `dev-g-policy-g-gh-cdk-deploy`
- **CDK Deployer Purpose**: Full CDK infrastructure deployments for any application

- **ECS Deployer Username**: `dev-g-user-g-gh-ecs-deployer`
- **ECS Deployer Policy**: `dev-g-policy-g-gh-ecs-deploy`
- **ECS Deployer Purpose**: Application deployments to existing ECS services for any application

#### Production Account (prd-alpha)  
- **CDK Deployer Username**: `prod-g-user-g-gh-cdk-deployer`
- **CDK Deployer Policy**: `prod-g-policy-g-gh-cdk-deploy`
- **CDK Deployer Purpose**: Full CDK infrastructure deployments for any application

- **ECS Deployer Username**: `prod-g-user-g-gh-ecs-deployer`
- **ECS Deployer Policy**: `prod-g-policy-g-gh-ecs-deploy`
- **ECS Deployer Purpose**: Application deployments to existing ECS services for any application

### Custom CDK Deployment Policy

**Why Not PowerUserAccess?**
The AWS managed `PowerUserAccess` policy restricts most IAM operations, but CDK deployments require specific IAM permissions to create and manage service roles for ECS tasks, Lambda functions, and other AWS services.

**Recommended Custom Policy:**
Create a custom policy named `{env}-g-policy-g-gh-cdk-deploy` with the following permissions. Think of this policy as a "construction permit" that allows building almost anything except the security foundations (IAM, KMS, Certificates).

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "CDKBootstrapAndMetadata",
      "Effect": "Allow",
      "Action": [
        "cloudformation:*",
        "ssm:GetParameter",
        "ssm:PutParameter",
        "ssm:DeleteParameter",
        "ssm:DescribeParameters"
      ],
      "Resource": [
        "arn:aws:cloudformation:*:*:stack/CDKToolkit/*",
        "arn:aws:cloudformation:*:*:stack/*",
        "arn:aws:ssm:*:*:parameter/cdk-bootstrap/*"
      ]
    },
    {
      "Sid": "S3ForCDKAssets",
      "Effect": "Allow",
      "Action": [
        "s3:CreateBucket",
        "s3:DeleteBucket",
        "s3:PutBucketPolicy",
        "s3:DeleteBucketPolicy",
        "s3:PutBucketVersioning",
        "s3:PutBucketPublicAccessBlock",
        "s3:PutBucketEncryption",
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket",
        "s3:GetBucketLocation",
        "s3:GetBucketVersioning"
      ],
      "Resource": [
        "arn:aws:s3:::cdk-*",
        "arn:aws:s3:::*"
      ]
    },
    {
      "Sid": "ECRForContainerAssets",
      "Effect": "Allow",
      "Action": [
        "ecr:CreateRepository",
        "ecr:DeleteRepository",
        "ecr:DescribeRepositories",
        "ecr:PutLifecyclePolicy",
        "ecr:GetAuthorizationToken",
        "ecr:InitiateLayerUpload",
        "ecr:UploadLayerPart",
        "ecr:CompleteLayerUpload",
        "ecr:BatchCheckLayerAvailability",
        "ecr:PutImage"
      ],
      "Resource": "*"
    },
    {
      "Sid": "CoreComputeServices",
      "Effect": "Allow",
      "Action": [
        "ec2:*",
        "autoscaling:*",
        "elasticloadbalancing:*",
        "ecs:*",
        "lambda:*",
        "states:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "DatabaseServices",
      "Effect": "Allow",
      "Action": [
        "rds:*",
        "dynamodb:*",
        "elasticache:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "NetworkingServices",
      "Effect": "Allow",
      "Action": [
        "apigateway:*",
        "cloudfront:*",
        "route53:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "AIAndMLServices",
      "Effect": "Allow",
      "Action": [
        "bedrock:*",
        "sagemaker:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "MessagingAndQueuing",
      "Effect": "Allow",
      "Action": [
        "sqs:*",
        "sns:*",
        "events:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "MonitoringAndLogging",
      "Effect": "Allow",
      "Action": [
        "logs:*",
        "cloudwatch:*",
        "xray:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "ApplicationServices",
      "Effect": "Allow",
      "Action": [
        "cognito-idp:*",
        "cognito-identity:*",
        "ses:*",
        "amplify:*"
      ],
      "Resource": "*"
    },
    {
      "Sid": "SecretsManagerRestrictedAccess",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret"
      ],
      "Resource": "arn:aws:secretsmanager:*:*:secret:*",
      "Condition": {
        "StringEquals": {
          "secretsmanager:ResourceTag/CDKManaged": "true"
        }
      }
    },
    {
      "Sid": "SecretsManagerCreateWithTag",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:CreateSecret",
        "secretsmanager:UpdateSecret",
        "secretsmanager:DeleteSecret",
        "secretsmanager:TagResource"
      ],
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestTag/CDKManaged": "true"
        }
      }
    },
    {
      "Sid": "IAMServiceLinkedRolesOnly",
      "Effect": "Allow",
      "Action": [
        "iam:CreateServiceLinkedRole",
        "iam:PassRole"
      ],
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "iam:AWSServiceName": [
            "elasticloadbalancing.amazonaws.com",
            "autoscaling.amazonaws.com",
            "rds.amazonaws.com",
            "ecs.amazonaws.com",
            "lambda.amazonaws.com"
          ]
        }
      }
    },
    {
      "Sid": "ReadOnlyForDebugging",
      "Effect": "Allow",
      "Action": [
        "iam:GetRole",
        "iam:GetRolePolicy",
        "iam:ListRolePolicies",
        "iam:ListAttachedRolePolicies",
        "kms:DescribeKey",
        "kms:ListAliases",
        "acm:ListCertificates",
        "acm:DescribeCertificate"
      ],
      "Resource": "*"
    },
    {
      "Sid": "TaggingPermissions",
      "Effect": "Allow",
      "Action": [
        "tag:TagResources",
        "tag:UntagResources",
        "tag:GetResources"
      ],
      "Resource": "*"
    },
    {
      "Sid": "ExplicitDenyDangerousActions",
      "Effect": "Deny",
      "Action": [
        "iam:CreateUser",
        "iam:CreateRole",
        "iam:CreatePolicy",
        "iam:DeleteRole",
        "iam:DeleteUser",
        "iam:DeletePolicy",
        "iam:AttachRolePolicy",
        "iam:DetachRolePolicy",
        "iam:PutRolePolicy",
        "iam:DeleteRolePolicy",
        "kms:CreateKey",
        "kms:ScheduleKeyDeletion",
        "kms:DisableKey",
        "acm:RequestCertificate",
        "acm:DeleteCertificate"
      ],
      "Resource": "*"
    }
  ]
}
```

### Key Design Decisions

**1. Service Coverage**
- Includes all major services for modern web applications: compute (EC2, Lambda, ECS), databases (RDS, DynamoDB), AI (Bedrock), networking (API Gateway, CloudFront), and application services (Cognito, SES)

**2. Security Boundaries**
- **Explicit Deny** at the bottom prevents IAM/KMS/ACM modifications even if other policies might allow it
- **SecretsManager** requires resources to be tagged with `CDKManaged=true`
- Can only create **service-linked roles** (not custom IAM roles)

**3. CDK-Specific Permissions**
- CloudFormation full access for stack operations
- S3 and ECR for CDK asset uploads
- SSM parameters for CDK bootstrap values

**4. Practical Security Limits**
Think of these restrictions like safety rails on a construction site:
- ❌ Can't modify the building's foundation (IAM roles)
- ❌ Can't change the master keys (KMS)
- ❌ Can't issue security badges (certificates)
- ✅ Can only access specific storage lockers (tagged secrets)

### Usage Tips

**For Secrets Management:**
When creating secrets in your CDK code, ensure they're tagged as CDK-managed:

```csharp
new Secret(this, "ApiKey", new SecretProps
{
    Tags = new Dictionary<string, string> 
    { 
        { "CDKManaged", "true" } 
    }
});
```

**For Debugging:**
- The policy allows read-only access to IAM/KMS/ACM for debugging purposes
- Service-linked roles are automatically created when needed (e.g., when creating an ALB or RDS cluster)

### Security Benefits

- ✅ **CDK Compatible**: Allows all necessary operations for modern infrastructure
- ✅ **Defense in Depth**: Multiple layers of security controls
- ✅ **Conditional Access**: Tagged resources and service-linked roles only  
- ✅ **Explicit Denies**: Prevents dangerous actions even if other policies allow them
- ✅ **Comprehensive Coverage**: Supports all AWS services needed for infrastructure

### ECS Deployment Policy

**Purpose**: For application deployments to existing ECS services (not infrastructure changes)

**ECS Deployment Policy:**
Create a custom policy named `{env}-g-policy-g-gh-ecs-deploy` with the following limited permissions:

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "ecs:DescribeServices",
                "ecs:UpdateService",
                "ecs:DescribeTaskDefinition",
                "ecs:RegisterTaskDefinition",
                "ecs:ListTaskDefinitions",
                "ecs:DescribeTasks",
                "ecs:ListTasks"
            ],
            "Resource": [
                "arn:aws:ecs:*:*:service/*",
                "arn:aws:ecs:*:*:task-definition/*",
                "arn:aws:ecs:*:*:task/*"
            ]
        },
        {
            "Effect": "Allow",
            "Action": [
                "ecr:GetAuthorizationToken"
            ],
            "Resource": "*"
        },
        {
            "Effect": "Allow",
            "Action": [
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage",
                "ecr:PutImage",
                "ecr:InitiateLayerUpload",
                "ecr:UploadLayerPart",
                "ecr:CompleteLayerUpload"
            ],
            "Resource": "arn:aws:ecr:*:*:repository/*"
        },
        {
            "Effect": "Allow",
            "Action": [
                "iam:PassRole"
            ],
            "Resource": [
                "arn:aws:iam::*:role/ecsTaskExecutionRole",
                "arn:aws:iam::*:role/*-service-role"
            ]
        }
    ]
}
```

### ECS Policy Key Features

**1. Limited Scope**
- Only ECS service and task definition operations
- No infrastructure creation or modification capabilities
- No access to other AWS services

**2. ECR Access**
- Container image push/pull operations
- Authentication token retrieval
- Layer management for Docker images

**3. IAM PassRole**
- Can only pass existing ECS execution roles
- Pattern-based role access (`ecsTaskExecutionRole` and `*-service-role`)

**4. Security Benefits**
- ✅ **Minimal Permissions**: Only what's needed for application deployments
- ✅ **No Infrastructure Changes**: Cannot modify VPC, databases, or other resources
- ✅ **Role-Based Access**: Can only use predefined execution roles
- ✅ **Container-Focused**: Specifically designed for containerized application deployments

### When to Use Each User Type

**Use CDK Deployer for:**
- Infrastructure changes (VPC, RDS, Load Balancers)
- New service creation
- Security group modifications
- Resource provisioning

**Use ECS Deployer for:**
- Application code deployments
- Container image updates
- Service scaling changes
- Task definition updates

## Migration from Repository Secrets

If you previously used repository-level secrets (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`), you can now remove them as they're replaced by environment-specific secrets.

## Troubleshooting

If deployments fail with credential errors:
1. Verify the environment secrets are correctly configured
2. Check that branch protection rules match your environment settings
3. Ensure the AWS credentials have the necessary permissions for their respective accounts