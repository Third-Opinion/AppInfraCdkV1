using System;
using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Apps.TrialMatch.Configuration;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialMatch.Builders;

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
    /// Create task role with necessary permissions
    /// </summary>
    public IRole CreateTaskRole(string serviceName)
    {
        var role = new Role(this, $"TrialMatchTaskRole-{serviceName}", new RoleProps
        {
            RoleName = _context.Namer.IamRole(IamPurpose.EcsTask),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = $"IAM role for TrialMatch {serviceName} ECS task execution"
        });

        // Add Secrets Manager permissions
        AddSecretsManagerPermissions(role);

        // Add other necessary permissions
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonS3ReadOnlyAccess"));
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonSQSFullAccess"));
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AmazonSNSFullAccess"));

        return role;
    }

    /// <summary>
    /// Create execution role with necessary permissions
    /// </summary>
    public IRole CreateExecutionRole(ILogGroup logGroup, string serviceName)
    {
        var role = new Role(this, $"TrialMatchExecutionRole-{serviceName}", new RoleProps
        {
            RoleName = _context.Namer.IamRole(IamPurpose.EcsExecution),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
            Description = $"IAM role for TrialMatch {serviceName} ECS task execution"
        });

        // Add ECS task execution policy
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonECSTaskExecutionRolePolicy"));

        // Add Secrets Manager permissions
        AddSecretsManagerPermissions(role);

        // Add CloudWatch Logs permissions
        logGroup.GrantWrite(role);

        return role;
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
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/{applicationName}/*",
            // Allow access to shared secrets if needed
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:/{environmentPrefix}/shared/*",
            // Allow access to specific API keys
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:OpenAiOptions__ApiKey*",
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:GoogleMaps__ApiKey*",
            $"arn:aws:secretsmanager:{_context.Environment.Region}:{_context.Environment.AccountId}:secret:ZipCodeApi__ApiKey*"
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
        // Note: We use "*" for KMS resources but constrain access via the kms:ViaService condition
        // This ensures the role can only use KMS keys when accessed through Secrets Manager
        // The condition prevents direct KMS access and ensures the principle of least privilege
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
    }

    /// <summary>
    /// Create GitHub Actions ECS deployment role
    /// </summary>
    public IRole CreateGitHubActionsEcsDeployRole(Construct scope)
    {
        var role = new Role(this, "GitHubActionsEcsDeployRole", new RoleProps
        {
            RoleName = $"dev-tm-role-g-ecsdeploy-github-actions",
            AssumedBy = new CompositePrincipal(
                new ServicePrincipal("ecs.amazonaws.com"),
                new WebIdentityPrincipal(
                    $"arn:aws:iam::{_context.Environment.AccountId}:oidc-provider/token.actions.githubusercontent.com",
                    new Dictionary<string, object>
                    {
                        ["StringEquals"] = new Dictionary<string, object>
                        {
                            ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                        },
                        ["StringLike"] = new Dictionary<string, object>
                        {
                            ["token.actions.githubusercontent.com:sub"] = new object[]
                            {
                                // AppInfraCdkV1 repository conditions
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/develop",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/development",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/master",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/main",
                                $"repo:Third-Opinion/AppInfraCdkV1:pull_request",
                                $"repo:Third-Opinion/AppInfraCdkV1:ref:refs/heads/*",
                                $"repo:Third-Opinion/AppInfraCdkV1:environment:development",
                                // TrialMatch Frontend repository conditions
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/develop",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/development",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/master",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/main",
                                $"repo:Third-Opinion/TrialMatch-FE:pull_request",
                                $"repo:Third-Opinion/TrialMatch-FE:ref:refs/heads/*",
                                $"repo:Third-Opinion/TrialMatch-FE:environment:development",
                                // TrialMatch Backend repository conditions
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/develop",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/development",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/feature/*",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/master",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/main",
                                $"repo:Third-Opinion/TrialMatch-BE:pull_request",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/*",
                                $"repo:Third-Opinion/TrialMatch-BE:ref:refs/heads/*",
                                $"repo:Third-Opinion/TrialMatch-BE:environment:development"
                            }
                        }
                    }
                )
            ),
            Description = $"Allows ECS to create and manage AWS resources on your behalf for TrialMatch in {_context.Environment.Name} environment",
            MaxSessionDuration = Duration.Hours(ConfigurationConstants.Timeouts.DefaultSessionDurationHours)
        });

        // Add ECS permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "ecs:DescribeServices",
                "ecs:DescribeTaskDefinition",
                "ecs:DescribeTasks",
                "ecs:ListTasks",
                "ecs:UpdateService",
                "ecs:RegisterTaskDefinition",
                "ecs:CreateService",
                "ecs:DeleteService",
                "ecs:StopTask",
                "ecs:RunTask",
                "ecs:StartTask",
                "ecs:DescribeClusters",
                "ecs:ListServices",
                "ecs:ListTaskDefinitions"
            },
            Resources = new[] { "*" }
        }));

        // Add IAM PassRole permissions for ECS task roles
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole"
            },
            Resources = new[]
            {
                _context.Namer.IamRoleArn(IamPurpose.EcsTask),
                _context.Namer.IamRoleArn(IamPurpose.EcsExecution)
            }
        }));

        // Add ECR permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
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
            Resources = new[] { "*" }
        }));

        // Add Secrets Manager permissions for tagged secrets
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "secretsmanager:GetSecretValue",
                "secretsmanager:DescribeSecret"
            },
            Resources = new[] { "*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    ["secretsmanager:ResourceTag/CDKManaged"] = "true"
                }
            }
        }));

        // Add CloudWatch Logs permissions
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "logs:CreateLogGroup",
                "logs:CreateLogStream",
                "logs:PutLogEvents",
                "logs:DescribeLogGroups",
                "logs:DescribeLogStreams"
            },
            Resources = new[] { "*" }
        }));

        // Add IAM permissions for ECS task roles
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:PassRole",
                "iam:GetRole"
            },
            Resources = new[]
            {
                $"arn:aws:iam::{_context.Environment.AccountId}:role/dev-tm-role-ue2-ecs-task-*",
                $"arn:aws:iam::{_context.Environment.AccountId}:role/dev-tm-role-ue2-ecs-exec-*"
            }
        }));

        // Attach the same managed policies as TrialFinderV2 role
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonEC2ContainerServiceRole"));
        
        // Try to attach the custom ECS deploy policy if it exists
        try
        {
            role.AddManagedPolicy(ManagedPolicy.FromManagedPolicyArn(this, "DevGPolicyGhEcsDeploy", 
                $"arn:aws:iam::{_context.Environment.AccountId}:policy/dev-g-policy-g-gh-ecs-deploy"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Warning: Could not attach dev-g-policy-g-gh-ecs-deploy policy: {ex.Message}");
            Console.WriteLine("The role will still work with the inline policies defined above.");
        }

        // Add tags
        Amazon.CDK.Tags.Of(role).Add("Purpose", "GitHubActionsECSDeploy");
        Amazon.CDK.Tags.Of(role).Add("Environment", _context.Environment.Name);
        Amazon.CDK.Tags.Of(role).Add("Application", _context.Application.Name);
        Amazon.CDK.Tags.Of(role).Add("ManagedBy", "CDK");

        return role;
    }
} 