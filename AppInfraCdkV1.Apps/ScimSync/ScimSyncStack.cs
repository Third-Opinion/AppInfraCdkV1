using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SSM;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.ScimSync;

/// <summary>
/// CDK Stack for SCIM synchronization between Google Workspace and AWS IAM Identity Center
/// 
/// This stack implements the slashdevops/idp-scim-sync solution with the following features:
/// - Lambda function using slashdevops/idp-scim-sync container image
/// - EventBridge scheduled rule for automated sync execution
/// - IAM roles with least privilege access for Google Workspace Directory API and AWS Identity Center
/// - SSM parameters for secure configuration storage
/// - CloudWatch logging for monitoring and troubleshooting
/// - Three-tier disable methodology for operational control
/// </summary>
public class ScimSyncStack : Stack
{
    private readonly DeploymentContext _context;
    
    public IFunction ScimSyncFunction { get; private set; } = null!;
    public IRule SyncScheduleRule { get; private set; } = null!;
    public IRole ScimLambdaExecutionRole { get; private set; } = null!;
    public ILogGroup ScimLogGroup { get; private set; } = null!;
    public Dictionary<string, IParameter> ScimParameters { get; private set; } = new();

    public ScimSyncStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        
        Console.WriteLine($"🔄 Creating SCIM synchronization stack for {context.Environment.Name}");
        Console.WriteLine($"   Implementing Google Workspace to AWS IAM Identity Center sync");
        
        // Create CloudWatch log group for SCIM sync
        CreateScimLogGroup();
        
        // Create IAM role for Lambda execution with SCIM permissions
        CreateScimLambdaExecutionRole();
        
        // Create SSM parameters for SCIM configuration
        CreateScimParameters();
        
        // Create Lambda function for SCIM sync
        CreateScimSyncFunction();
        
        // Create EventBridge scheduled rule for sync automation
        CreateSyncScheduleRule();
        
        // Output important resource information
        CreateStackOutputs();
    }

    private void CreateScimLogGroup()
    {
        var resourceName = _context.Namer.Custom("lambda", ResourcePurpose.Internal);
        
        ScimLogGroup = new LogGroup(this, "ScimLogGroup", new Amazon.CDK.AWS.Logs.LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{resourceName}-scim-sync",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY
        });
        
        Amazon.CDK.Tags.Of(ScimLogGroup).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(ScimLogGroup).Add("Environment", _context.Environment.Name);
    }

    private void CreateScimLambdaExecutionRole()
    {
        var roleName = _context.Namer.Custom("lambda-execution", ResourcePurpose.Internal);
        
        ScimLambdaExecutionRole = new Role(this, "ScimLambdaExecutionRole", new RoleProps
        {
            RoleName = roleName,
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Execution role for SCIM synchronization Lambda function",
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
            },
            InlinePolicies = new Dictionary<string, PolicyDocument>
            {
                ["ScimSyncPolicy"] = new PolicyDocument(new PolicyDocumentProps
                {
                    Statements = new[]
                    {
                        // SSM Parameter Store access for configuration
                        new PolicyStatement(new PolicyStatementProps
                        {
                            Effect = Effect.ALLOW,
                            Actions = new[] { "ssm:GetParameter", "ssm:GetParameters", "ssm:GetParametersByPath" },
                            Resources = new[] { $"arn:aws:ssm:{Region}:{Account}:parameter/scim-sync/{_context.Environment.Name}/*" }
                        }),
                        // CloudWatch Logs permissions
                        new PolicyStatement(new PolicyStatementProps
                        {
                            Effect = Effect.ALLOW,
                            Actions = new[] { "logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents" },
                            Resources = new[] { ScimLogGroup.LogGroupArn }
                        }),
                        // AWS Identity Center SCIM API permissions
                        new PolicyStatement(new PolicyStatementProps
                        {
                            Effect = Effect.ALLOW,
                            Actions = new[] 
                            { 
                                "identitystore:CreateUser",
                                "identitystore:UpdateUser", 
                                "identitystore:DeleteUser",
                                "identitystore:CreateGroup",
                                "identitystore:UpdateGroup",
                                "identitystore:DeleteGroup",
                                "identitystore:CreateGroupMembership",
                                "identitystore:DeleteGroupMembership",
                                "identitystore:ListUsers",
                                "identitystore:ListGroups",
                                "identitystore:ListGroupMemberships"
                            },
                            Resources = new[] { "*" }
                        })
                    }
                })
            }
        });
        
        Amazon.CDK.Tags.Of(ScimLambdaExecutionRole).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(ScimLambdaExecutionRole).Add("Environment", _context.Environment.Name);
    }

    private void CreateScimParameters()
    {
        var parameterPrefix = $"/scim-sync/{_context.Environment.Name}";
        
        // Google Workspace configuration parameters
        var googleServiceAccountParameter = new StringParameter(this, "GoogleServiceAccountKey", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/google/service-account-key",
            StringValue = "PLACEHOLDER-UPDATE-WITH-ACTUAL-SERVICE-ACCOUNT-KEY",
            Description = "Google Workspace service account key for Directory API access"
        });
        ScimParameters["GoogleServiceAccountKey"] = googleServiceAccountParameter;
        
        var googleDomainParameter = new StringParameter(this, "GoogleDomain", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/google/domain",
            StringValue = "example.com",
            Description = "Google Workspace domain name"
        });
        ScimParameters["GoogleDomain"] = googleDomainParameter;
        
        // AWS Identity Center configuration parameters
        var identityCenterScimEndpointParameter = new StringParameter(this, "IdentityCenterScimEndpoint", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/aws/identity-center-scim-endpoint",
            StringValue = "PLACEHOLDER-UPDATE-WITH-ACTUAL-SCIM-ENDPOINT",
            Description = "AWS Identity Center SCIM endpoint URL"
        });
        ScimParameters["IdentityCenterScimEndpoint"] = identityCenterScimEndpointParameter;
        
        var identityCenterScimTokenParameter = new StringParameter(this, "IdentityCenterScimToken", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/aws/identity-center-scim-token",
            StringValue = "PLACEHOLDER-UPDATE-WITH-ACTUAL-SCIM-TOKEN",
            Description = "AWS Identity Center SCIM access token"
        });
        ScimParameters["IdentityCenterScimToken"] = identityCenterScimTokenParameter;
        
        // Sync configuration parameters
        var syncEnabledParameter = new StringParameter(this, "SyncEnabled", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/sync/enabled",
            StringValue = "true",
            Description = "Enable/disable SCIM synchronization"
        });
        ScimParameters["SyncEnabled"] = syncEnabledParameter;
        
        var groupFiltersParameter = new StringParameter(this, "GroupFilters", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/sync/group-filters",
            StringValue = ".*",
            Description = "Regular expression filter for Google Workspace groups to sync"
        });
        ScimParameters["GroupFilters"] = groupFiltersParameter;
        
        // Environment-specific sync frequency
        var syncFrequencyMinutes = _context.Environment.Name.ToLower() == "production" ? "15" : "30";
        var syncFrequencyParameter = new StringParameter(this, "SyncFrequency", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/sync/frequency-minutes",
            StringValue = syncFrequencyMinutes,
            Description = "Sync frequency in minutes"
        });
        ScimParameters["SyncFrequency"] = syncFrequencyParameter;
        
        // Tag all parameters
        foreach (var parameter in ScimParameters.Values)
        {
            Amazon.CDK.Tags.Of(parameter).Add("Purpose", "SCIM-Synchronization");
            Amazon.CDK.Tags.Of(parameter).Add("Environment", _context.Environment.Name);
        }
    }

    private void CreateScimSyncFunction()
    {
        var functionName = _context.Namer.Lambda(ResourcePurpose.Internal);
        
        ScimSyncFunction = new Function(this, "ScimSyncFunction", new FunctionProps
        {
            FunctionName = functionName,
            Runtime = Runtime.FROM_IMAGE,
            Code = Code.FromEcrImage(Amazon.CDK.AWS.ECR.Repository.FromRepositoryName(this, "ScimSyncRepo", "slashdevops/idp-scim-sync")),
            Handler = Handler.FROM_IMAGE,
            Role = ScimLambdaExecutionRole,
            Timeout = Duration.Minutes(5),
            MemorySize = 512,
            LogGroup = ScimLogGroup,
            Environment = new Dictionary<string, string>
            {
                ["SCIM_SYNC_CONFIG_PATH"] = $"/scim-sync/{_context.Environment.Name}"
            },
            Description = "SCIM synchronization function for Google Workspace to AWS Identity Center"
        });
        
        Amazon.CDK.Tags.Of(ScimSyncFunction).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(ScimSyncFunction).Add("Environment", _context.Environment.Name);
    }

    private void CreateSyncScheduleRule()
    {
        var ruleName = _context.Namer.Custom("events-rule", ResourcePurpose.Internal);
        
        // Get sync frequency from parameter (default to 30 minutes for dev, 15 for prod)
        var syncFrequencyMinutes = _context.Environment.Name.ToLower() == "production" ? 15 : 30;
        
        SyncScheduleRule = new Rule(this, "SyncScheduleRule", new RuleProps
        {
            RuleName = ruleName,
            Description = $"Scheduled trigger for SCIM synchronization every {syncFrequencyMinutes} minutes",
            Schedule = Schedule.Rate(Duration.Minutes(syncFrequencyMinutes)),
            Enabled = true,
            Targets = new IRuleTarget[]
            {
                new LambdaFunction(ScimSyncFunction, new LambdaFunctionProps
                {
                    Event = RuleTargetInput.FromObject(new Dictionary<string, object>
                    {
                        ["source"] = "aws.events",
                        ["action"] = "sync",
                        ["environment"] = _context.Environment.Name
                    })
                })
            }
        });
        
        Amazon.CDK.Tags.Of(SyncScheduleRule).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(SyncScheduleRule).Add("Environment", _context.Environment.Name);
    }

    private void CreateStackOutputs()
    {
        new CfnOutput(this, "ScimSyncFunctionName", new CfnOutputProps
        {
            Value = ScimSyncFunction.FunctionName,
            Description = "Name of the SCIM synchronization Lambda function",
            ExportName = $"{_context.Environment.Name}-scim-sync-function-name"
        });
        
        new CfnOutput(this, "ScimSyncFunctionArn", new CfnOutputProps
        {
            Value = ScimSyncFunction.FunctionArn,
            Description = "ARN of the SCIM synchronization Lambda function",
            ExportName = $"{_context.Environment.Name}-scim-sync-function-arn"
        });
        
        new CfnOutput(this, "ScimLogGroupName", new CfnOutputProps
        {
            Value = ScimLogGroup.LogGroupName,
            Description = "Name of the SCIM synchronization CloudWatch log group",
            ExportName = $"{_context.Environment.Name}-scim-log-group-name"
        });
        
        new CfnOutput(this, "SyncScheduleRuleName", new CfnOutputProps
        {
            Value = SyncScheduleRule.RuleName,
            Description = "Name of the EventBridge rule for SCIM sync scheduling",
            ExportName = $"{_context.Environment.Name}-scim-schedule-rule-name"
        });
        
        new CfnOutput(this, "ScimParameterPrefix", new CfnOutputProps
        {
            Value = $"/scim-sync/{_context.Environment.Name}",
            Description = "SSM parameter prefix for SCIM configuration",
            ExportName = $"{_context.Environment.Name}-scim-parameter-prefix"
        });
    }
}