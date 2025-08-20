using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using AppInfraCdkV1.Core.Models;
using AppInfraCdkV1.Core.Naming;
using AppInfraCdkV1.Core.Enums;
using System.Collections.Generic;

namespace AppInfraCdkV1.PublicThirdOpinion.Constructs
{
    public class JwkLambdaConstruct : Construct
    {
        public IFunction JwkGeneratorFunction { get; private set; }
        public IRole LambdaExecutionRole { get; private set; }
        public ILogGroup LogGroup { get; private set; }

        public JwkLambdaConstruct(Construct scope, string id, DeploymentContext context, IBucket targetBucket)
            : base(scope, id)
        {
            var resourceNamer = new ResourceNamer(context);

            // Create CloudWatch Log Group using ResourceNamer
            var logGroupName = resourceNamer.LogGroup("lambda", ResourcePurpose.JwkGenerator);
            LogGroup = new LogGroup(this, "LogGroup", new LogGroupProps
            {
                LogGroupName = logGroupName,
                Retention = RetentionDays.ONE_MONTH,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // Create IAM execution role using ResourceNamer
            var roleName = resourceNamer.IamRole(IamPurpose.LambdaExecution);
            LambdaExecutionRole = new Role(this, "ExecutionRole", new RoleProps
            {
                RoleName = roleName,
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "Execution role for JWK Generator Lambda",
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            // Add S3 permissions for writing JWK files
            targetBucket.GrantReadWrite(LambdaExecutionRole, "jwks/*");

            // Add Secrets Manager permissions for storing private keys
            var secretsPath = $"{context.Environment.Name.ToLower()}/jwk/*";
            (LambdaExecutionRole as Role)?.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "secretsmanager:CreateSecret",
                    "secretsmanager:UpdateSecret",
                    "secretsmanager:PutSecretValue",
                    "secretsmanager:GetSecretValue",
                    "secretsmanager:DescribeSecret",
                    "secretsmanager:TagResource"
                },
                Resources = new[]
                {
                    $"arn:aws:secretsmanager:{context.Environment.Region}:{context.Environment.AccountId}:secret:{secretsPath}"
                }
            }));

            // Add KMS permissions for Secrets Manager encryption
            (LambdaExecutionRole as Role)?.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "kms:Decrypt",
                    "kms:Encrypt",
                    "kms:GenerateDataKey",
                    "kms:DescribeKey"
                },
                Resources = new[]
                {
                    $"arn:aws:kms:{context.Environment.Region}:{context.Environment.AccountId}:key/*"
                },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["kms:ViaService"] = $"secretsmanager.{context.Environment.Region}.amazonaws.com"
                    }
                }
            }));

            // Create Lambda function using ResourceNamer
            var functionName = resourceNamer.Lambda(ResourcePurpose.JwkGenerator);
            JwkGeneratorFunction = new Function(this, "Function", new FunctionProps
            {
                FunctionName = functionName,
                Runtime = Runtime.DOTNET_8,
                Handler = "JwkGenerator::AppInfraCdkV1.PublicThirdOpinion.Lambda.JwkGenerator::Handler",
                Code = Code.FromAsset(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "AppInfraCdkV1.PublicThirdOpinion/Lambda/publish")),
                Role = LambdaExecutionRole,
                Timeout = Duration.Minutes(5),
                MemorySize = 512,
                LogGroup = LogGroup,
                Environment = new Dictionary<string, string>
                {
                    ["BUCKET_NAME"] = targetBucket.BucketName,
                    ["ENVIRONMENT"] = context.Environment.Name,
                    ["SECRETS_PREFIX"] = $"{context.Environment.Name.ToLower()}/jwk"
                },
                Description = "Lambda function to generate JWK key pairs for FHIR R4 authentication"
            });

            // Create IAM policy for admin-only access to private keys
            var adminSecretPolicy = new Policy(this, "AdminSecretPolicy", new PolicyProps
            {
                PolicyName = resourceNamer.IamPolicy(IamPurpose.SecretsAccess),
                Statements = new[]
                {
                    new PolicyStatement(new PolicyStatementProps
                    {
                        Effect = Effect.ALLOW,
                        Actions = new[]
                        {
                            "secretsmanager:GetSecretValue",
                            "secretsmanager:DescribeSecret"
                        },
                        Resources = new[]
                        {
                            $"arn:aws:secretsmanager:{context.Environment.Region}:{context.Environment.AccountId}:secret:{secretsPath}"
                        },
                        Principals = new[]
                        {
                            new ArnPrincipal($"arn:aws:iam::{context.Environment.AccountId}:role/AWSReservedSSO_AdministratorAccess_*")
                        }
                    })
                }
            });

            // Output Lambda function ARN
            new CfnOutput(this, "FunctionArn", new CfnOutputProps
            {
                Value = JwkGeneratorFunction.FunctionArn,
                Description = "JWK Generator Lambda Function ARN"
            });

            new CfnOutput(this, "FunctionName", new CfnOutputProps
            {
                Value = JwkGeneratorFunction.FunctionName,
                Description = "JWK Generator Lambda Function Name"
            });
        }
    }
}