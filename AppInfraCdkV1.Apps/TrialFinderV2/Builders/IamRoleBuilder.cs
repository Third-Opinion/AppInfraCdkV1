using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2.Builders;

/// <summary>
/// Builder for creating IAM roles for ECS tasks and services
/// </summary>
public class IamRoleBuilder : Construct
{
    private readonly DeploymentContext _context;

    public IamRoleBuilder(Construct scope, string id, DeploymentContext context) : base(scope, id)
    {
        _context = context;
    }

    /// <summary>
    /// Create task execution role for ECS tasks
    /// </summary>
    public IRole CreateTaskExecutionRole(ILogGroup logGroup, string? uniqueId = null)
    {
        // Follow naming convention: {environment}-{service}-execution-role
        var roleName = string.IsNullOrEmpty(uniqueId) 
            ? $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-execution-role"
            : $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-{uniqueId.ToLowerInvariant()}-execution-role";
        
        // Create unique construct ID to avoid duplicates
        var constructId = string.IsNullOrEmpty(uniqueId) ? "TrialFinderExecutionRole" : $"TrialFinderExecutionRole{uniqueId}";
        
        var executionRole = new Role(this, constructId, new RoleProps
        {
            RoleName = roleName,
            Description = $"Execution role for {_context.Application.Name} ECS tasks in {_context.Environment.Name}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName(
                    "service-role/AmazonECSTaskExecutionRolePolicy")
            }
        });

        // Add CloudWatch Logs permissions with specific log group
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogStream",
                "logs:PutLogEvents"
            },
            Resources = new[] 
            { 
                logGroup.LogGroupArn,
                $"{logGroup.LogGroupArn}:*"
            }
        }));

        // Add ECR permissions for pulling images from specific repositories
        var ecrRepoArns = new[]
        {
            $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/thirdopinion/*",
            $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/{_context.Application.Name.ToLowerInvariant()}/*"
        };
        
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecr:GetAuthorizationToken",
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage"
            },
            Resources = ecrRepoArns
        }));

        // Add ECR authorization token permission (account-wide)
        executionRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[] { "ecr:GetAuthorizationToken" },
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for container startup
        AddSecretsManagerPermissions(executionRole);

        // Add tag for identification
        Tags.Of(executionRole).Add("ManagedBy", "CDK");
        Tags.Of(executionRole).Add("Purpose", "ECS-Execution");
        Tags.Of(executionRole).Add("Service", _context.Application.Name);

        return executionRole;
    }

    /// <summary>
    /// Create task role for ECS tasks
    /// </summary>
    public IRole CreateTaskRole(string? uniqueId = null)
    {
        // Follow naming convention: {environment}-{service}-task-role
        var roleName = string.IsNullOrEmpty(uniqueId) 
            ? $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-task-role"
            : $"{_context.Environment.Name}-{_context.Application.Name.ToLowerInvariant()}-{uniqueId.ToLowerInvariant()}-task-role";
        
        // Create unique construct ID to avoid duplicates
        var constructId = string.IsNullOrEmpty(uniqueId) ? "TrialFinderTaskRole" : $"TrialFinderTaskRole{uniqueId}";
        
        var taskRole = new Role(this, constructId, new RoleProps
        {
            RoleName = roleName,
            Description = $"Task role for {_context.Application.Name} ECS tasks in {_context.Environment.Name}",
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            // Task role should not have the execution policy
            ManagedPolicies = Array.Empty<IManagedPolicy>()
        });

        // Add permissions for Session Manager (ECS Exec)
        taskRole.AddManagedPolicy(
            ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore"));

        // Add SSM permissions for ECS Exec
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ssmmessages:CreateControlChannel",
                "ssmmessages:CreateDataChannel",
                "ssmmessages:OpenControlChannel",
                "ssmmessages:OpenDataChannel"
            },
            Resources = new[] { "*" }
        }));

        // Add CloudWatch Logs permissions for application logging
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogGroup",
                "logs:CreateLogStream",
                "logs:PutLogEvents",
                "logs:DescribeLogStreams"
            },
            Resources = new[] 
            { 
                $"arn:aws:logs:{_context.Environment.Region}:{_context.Environment.AccountId}:log-group:{_context.Namer.LogGroup("*", ResourcePurpose.Web)}:*"
            }
        }));

        // Add Secrets Manager permissions for environment-specific secrets
        AddSecretsManagerPermissions(taskRole);

        // Add QuickSight permissions for embedding functionality
        AddQuickSightPermissions(taskRole);

        // Add Bedrock permissions for AI model access
        AddBedrockPermissions(taskRole);

        // Add tag for identification
        Tags.Of(taskRole).Add("ManagedBy", "CDK");
        Tags.Of(taskRole).Add("Purpose", "ECS-Task");
        Tags.Of(taskRole).Add("Service", _context.Application.Name);

        return taskRole;
    }

    /// <summary>
    /// Create GitHub Actions deployment role
    /// </summary>
    public IRole CreateGitHubActionsRole()
    {
        // Follow naming convention: {environment}-{service}-github-actions-role
        var roleName = _context.Namer.IamRole(IamPurpose.GithubActionsDeploy);
        
        var deploymentRole = new Role(this, "GithubActionsDeploymentRole", new RoleProps
        {
            RoleName = roleName,
            Description = $"Role for GitHub Actions to deploy to ECS in {_context.Environment.Name}",
            AssumedBy = new FederatedPrincipal($"arn:aws:iam::{_context.Environment.AccountId}:oidc-provider/token.actions.githubusercontent.com", new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                },
                ["StringLike"] = new Dictionary<string, string>
                {
                    ["token.actions.githubusercontent.com:sub"] = $"repo:Third-Opinion/TrialFinder:*"
                }
            }, "sts:AssumeRoleWithWebIdentity"),
            ManagedPolicies = Array.Empty<IManagedPolicy>()
        });

        // Add ECS permissions for task definition management
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECSDeployment",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeTaskDefinition",
                "ecs:RegisterTaskDefinition",
                "ecs:DescribeServices",
                "ecs:UpdateService",
                "ecs:DescribeClusters",
                "ecs:ListTasks",
                "ecs:DescribeTasks",
                "ecs:StopTask",
                "ecs:RunTask"
            },
            Resources = new[]
            {
                $"arn:aws:ecs:{_context.Environment.Region}:{_context.Environment.AccountId}:task-definition/*",
                $"arn:aws:ecs:{_context.Environment.Region}:{_context.Environment.AccountId}:service/*",
                $"arn:aws:ecs:{_context.Environment.Region}:{_context.Environment.AccountId}:cluster/*",
                $"arn:aws:ecs:{_context.Environment.Region}:{_context.Environment.AccountId}:task/*"
            }
        }));

        // Add specific permission for DescribeTaskDefinition with * resource
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowDescribeTaskDefinition",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeTaskDefinition"
            },
            Resources = new[]
            {
                "*"
            }
        }));
        
        // Add ECR permissions for image access
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECRAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecr:GetAuthorizationToken",
                "ecr:BatchCheckLayerAvailability",
                "ecr:GetDownloadUrlForLayer",
                "ecr:BatchGetImage",
                "ecr:PutImage",
                "ecr:InitiateLayerUpload",
                "ecr:UploadLayerPart",
                "ecr:CompleteLayerUpload"
            },
            Resources = new[]
            {
                $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/thirdopinion/*",
                $"arn:aws:ecr:{_context.Environment.Region}:{_context.Environment.AccountId}:repository/{_context.Application.Name.ToLowerInvariant()}/*"
            }
        }));

        // Add ECR authorization token permission (account-wide)
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowECRAuthorizationToken",
            Effect = Effect.ALLOW,
            Actions = new[] { "ecr:GetAuthorizationToken" },
            Resources = new[] { "*" }
        }));

        // Add CloudFormation permissions to read stack outputs
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowCloudFormationRead",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "cloudformation:DescribeStacks",
                "cloudformation:ListStacks",
                "cloudformation:GetTemplate"
            },
            Resources = new[]
            {
                $"arn:aws:cloudformation:{_context.Environment.Region}:{_context.Environment.AccountId}:stack/*"
            }
        }));

        // Add IAM permissions for role assumption (needed for OIDC)
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowIAMRoleAssumption",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole"
            },
            Resources = new[]
            {
                // Allow passing the application-specific roles using the naming convention
                $"arn:aws:iam::{_context.Environment.AccountId}:role/{_context.Namer.IamRole(IamPurpose.EcsTask)}",
                $"arn:aws:iam::{_context.Environment.AccountId}:role/{_context.Namer.IamRole(IamPurpose.EcsExecution)}",
                // Allow passing ECS service factory roles (needed for EventBridge targets)
                $"arn:aws:iam::{_context.Environment.AccountId}:role/{_context.Namer.IamRole(IamPurpose.Service)}",
                // Allow passing roles that follow the naming convention pattern
                $"arn:aws:iam::{_context.Environment.AccountId}:role/{_context.Namer.IamRole(IamPurpose.GithubActionsDeploy).Replace("github-actions-deploy", "*")}"
            }
        }));

        // Add CloudWatch Events (EventBridge) permissions for deployment
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowCloudWatchEvents",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "events:ListRules",
                "events:DescribeRule",
                "events:ListTargetsByRule",
                "events:ListEventBuses",
                "events:DescribeEventBus",
                "events:PutTargets",
                "events:RemoveTargets",
                "events:DescribeRule"
            },
            Resources = new[]
            {
                $"arn:aws:events:{_context.Environment.Region}:{_context.Environment.AccountId}:rule/*",
                $"arn:aws:events:{_context.Environment.Region}:{_context.Environment.AccountId}:event-bus/*"
            }
        }));

        // Add Secrets Manager permissions for secret existence checking
        deploymentRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowSecretsManagerRead",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:DescribeSecret",
                "secretsmanager:GetSecretValue",
                "secretsmanager:ListSecrets"
            },
            Resources = new[]
            {
                // Allow access to secrets in both path and ARN formats
                $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{_context.Environment.Name.ToLowerInvariant()}/{_context.Application.Name.ToLowerInvariant()}/*"
            }
        }));

        // Add tag for identification
        Tags.Of(deploymentRole).Add("ManagedBy", "CDK");
        Tags.Of(deploymentRole).Add("Purpose", "GitHub-Actions-Deployment");
        Tags.Of(deploymentRole).Add("Service", _context.Application.Name);

        return deploymentRole;
    }



    /// <summary>
    /// Add Secrets Manager permissions to IAM role with environment-specific scoping
    /// </summary>
    public void AddSecretsManagerPermissions(IRole role)
    {
        // Cast to Role to access AddToPolicy method
        var concreteRole = role as Role;
        if (concreteRole == null) return;

        // Environment-specific secret path patterns
        var environmentPrefix = _context.Environment.Name.ToLowerInvariant();
        var applicationName = _context.Application.Name.ToLowerInvariant();
        
        // Define specific secret ARNs that the task can access
        var allowedSecretArns = new[]
        {
            // Allow access to application-specific secrets
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/{applicationName}/*",
            // Allow access to shared secrets if needed
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/shared/*"
        };
        
        // Add Secrets Manager permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowSecretsManagerAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = allowedSecretArns
        }));

        // Add KMS permissions for secret decryption
        // Use wildcard for KMS keys as they might be customer-managed
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowKMSDecrypt",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "kms:Decrypt",
                "kms:DescribeKey",
                "kms:GenerateDataKey"
            },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    ["kms:ViaService"] = $"secretsmanager.{_context.Environment.Region}.amazonaws.com"
                }
            }
        }));

        // Add explicit deny for cross-environment access
        var deniedPrefixes = new[]
        {
            "production", "staging", "integration", "development"
        }.Where(env => env != environmentPrefix).ToArray();
        
        if (deniedPrefixes.Length > 0)
        {
            var deniedPatterns = deniedPrefixes.Select(prefix => $"/{prefix}/*").ToArray();
            
            concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "DenyCrossEnvironmentSecrets",
                Effect = Effect.DENY,
                Actions = new[]
                {
                    "secretsmanager:GetSecretValue",
                    "secretsmanager:DescribeSecret"
                },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["ForAnyValue:StringLike"] = new Dictionary<string, object>
                    {
                        ["secretsmanager:SecretId"] = deniedPatterns
                    }
                }
            }));
        }
    }

    /// <summary>
    /// Add QuickSight permissions to IAM role for embedding functionality and database access
    /// </summary>
    public void AddQuickSightPermissions(IRole role)
    {
        // Cast to Role to access AddToPolicy method
        var concreteRole = role as Role;
        if (concreteRole == null) return;

        // QuickSight user management permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightUserManagement",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:RegisterUser",
                "quicksight:UnregisterUser",
                "quicksight:DescribeUser",
                "quicksight:ListUsers",
                "quicksight:UpdateUser"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:user/*"
            }
        }));

        // QuickSight embedding permissions (includes Generative Q&A)
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightEmbedding",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:GenerateEmbedUrlForRegisteredUser", // For Generative Q&A and dashboard embedding
                "quicksight:GetDashboardEmbedUrl",
                "quicksight:GetSessionEmbedUrl",
                "quicksight:DescribeDashboard",
                "quicksight:ListDashboards",
                "quicksight:DescribeDataSet",
                "quicksight:ListDataSets"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:user/*",
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:dashboard/*",
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:dataset/*"
            }
        }));

        // QuickSight namespace permissions (for multi-tenant scenarios)
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightNamespaceAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeNamespace",
                "quicksight:ListNamespaces"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:namespace/*"
            }
        }));

        // QuickSight data source permissions for database access
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightDataSourceAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeDataSource",
                "quicksight:ListDataSources",
                "quicksight:CreateDataSource",
                "quicksight:UpdateDataSource",
                "quicksight:DeleteDataSource",
                "quicksight:DescribeDataSourcePermissions",
                "quicksight:UpdateDataSourcePermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:datasource/*"
            }
        }));

        // QuickSight analysis permissions for creating and managing analyses
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightAnalysisAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeAnalysis",
                "quicksight:ListAnalyses",
                "quicksight:CreateAnalysis",
                "quicksight:UpdateAnalysis",
                "quicksight:DeleteAnalysis",
                "quicksight:DescribeAnalysisPermissions",
                "quicksight:UpdateAnalysisPermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:analysis/*"
            }
        }));

        // QuickSight theme permissions for consistent styling
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowQuickSightThemeAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "quicksight:DescribeTheme",
                "quicksight:ListThemes",
                "quicksight:CreateTheme",
                "quicksight:UpdateTheme",
                "quicksight:DeleteTheme",
                "quicksight:DescribeThemePermissions",
                "quicksight:UpdateThemePermissions"
            },
            Resources = new[] 
            { 
                $"arn:aws:quicksight:{_context.Environment.Region}:{_context.Environment.AccountId}:theme/*"
            }
        }));
    }

    /// <summary>
    /// Add Bedrock permissions to IAM role for AI model access
    /// </summary>
    public void AddBedrockPermissions(IRole role)
    {
        // Cast to Role to access AddToPolicy method
        var concreteRole = role as Role;
        if (concreteRole == null) return;

        // Bedrock model invocation permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowBedrockModelInvocation",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "bedrock:InvokeModel",
                "bedrock:InvokeModelWithResponseStream"
            },
            Resources = new[] 
            { 
                // Allow access to all foundation models (AWS managed models)
                $"arn:aws:bedrock:*::foundation-model/*",
                // Allow access to account-specific inference profiles
                $"arn:aws:bedrock:*:{_context.Environment.AccountId}:inference-profile/*"
            }
        }));

        // Bedrock model information permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowBedrockModelInformation",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "bedrock:GetFoundationModel",
                "bedrock:ListFoundationModels",
                "bedrock:GetModelInvocationLoggingConfiguration",
                "bedrock:GetModelCustomizationJob",
                "bedrock:ListModelCustomizationJobs",
                "bedrock:GetProvisionedModelThroughput",
                "bedrock:ListProvisionedModelThroughputs",
                "bedrock:GetModelEvaluationJob",
                "bedrock:ListModelEvaluationJobs"
            },
            Resources = new[] { "*" }
        }));

        // Bedrock inference profile permissions
        concreteRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowBedrockInferenceProfiles",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "bedrock:GetInferenceProfile",
                "bedrock:ListInferenceProfiles"
            },
            Resources = new[] 
            { 
                $"arn:aws:bedrock:{_context.Environment.Region}:{_context.Environment.AccountId}:inference-profile/*"
            }
        }));
    }
}
