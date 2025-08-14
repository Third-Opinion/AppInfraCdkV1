using Amazon.CDK;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SSM;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.InternalApps.ScimSync;

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
                        }),
                        // EventBridge permissions for Tier 1 disable methodology
                        new PolicyStatement(new PolicyStatementProps
                        {
                            Effect = Effect.ALLOW,
                            Actions = new[] 
                            { 
                                "events:EnableRule",
                                "events:DisableRule",
                                "events:DescribeRule",
                                "events:PutRule"
                            },
                            Resources = new[] { $"arn:aws:events:{Region}:{Account}:rule/*scim*" }
                        }),
                        // SSM Parameter Store write permissions for status updates
                        new PolicyStatement(new PolicyStatementProps
                        {
                            Effect = Effect.ALLOW,
                            Actions = new[] { "ssm:PutParameter" },
                            Resources = new[] 
                            { 
                                $"arn:aws:ssm:{Region}:{Account}:parameter/scim-sync/{_context.Environment.Name}/status/*",
                                $"arn:aws:ssm:{Region}:{Account}:parameter/scim-sync/{_context.Environment.Name}/disable-controls/*"
                            }
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
        
        // Three-tier disable methodology parameters
        
        // Tier 1: EventBridge rule disable control
        var eventBridgeRuleEnabledParameter = new StringParameter(this, "EventBridgeRuleEnabled", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/disable-controls/eventbridge-enabled",
            StringValue = "true",
            Description = "Enable/disable EventBridge schedule rule - Tier 1 disable control"
        });
        ScimParameters["EventBridgeRuleEnabled"] = eventBridgeRuleEnabledParameter;
        
        // Tier 2: Group filter disable control
        var groupFilterDisableParameter = new StringParameter(this, "GroupFilterDisable", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/disable-controls/group-filter-disabled",
            StringValue = "false",
            Description = "Emergency group filter to exclude ALL groups - Tier 2 disable control"
        });
        ScimParameters["GroupFilterDisable"] = groupFilterDisableParameter;
        
        // Tier 3: Identity Center SCIM endpoint disable control
        var identityCenterScimDisableParameter = new StringParameter(this, "IdentityCenterScimDisable", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/disable-controls/identity-center-disabled",
            StringValue = "false",
            Description = "Disable Identity Center SCIM operations - Tier 3 disable control"
        });
        ScimParameters["IdentityCenterScimDisable"] = identityCenterScimDisableParameter;
        
        // Operational status tracking
        var lastSyncStatusParameter = new StringParameter(this, "LastSyncStatus", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/status/last-sync-status",
            StringValue = "never-run",
            Description = "Status of the last SCIM synchronization run"
        });
        ScimParameters["LastSyncStatus"] = lastSyncStatusParameter;
        
        var lastSyncTimestampParameter = new StringParameter(this, "LastSyncTimestamp", new StringParameterProps
        {
            ParameterName = $"{parameterPrefix}/status/last-sync-timestamp",
            StringValue = "never",
            Description = "Timestamp of the last SCIM synchronization attempt"
        });
        ScimParameters["LastSyncTimestamp"] = lastSyncTimestampParameter;
        
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
        
        // Use slashdevops/idp-scim-sync container image from public ECR
        var dockerfilePath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), 
            "..", 
            "tools", 
            "AppInfraCdkV1.Tools.Common", 
            "docker-lambda"
        );
        
        ScimSyncFunction = new Function(this, "ScimSyncFunction", new FunctionProps
        {
            FunctionName = functionName,
            Runtime = Runtime.FROM_IMAGE,
            Code = Code.FromDockerBuild(dockerfilePath),
            Handler = "index.handler", // Required by CDK but overridden by container CMD
            Role = ScimLambdaExecutionRole,
            Timeout = Duration.Minutes(5),
            MemorySize = 512,
            LogGroup = ScimLogGroup,
            Environment = new Dictionary<string, string>
            {
                // Google Workspace configuration
                ["GOOGLE_ADMIN"] = "scim-service@thirdopinion.io",
                ["GOOGLE_CREDENTIALS"] = "/tmp/google-credentials.json", // Will be loaded from SSM
                
                // AWS Identity Center SCIM configuration  
                ["SCIM_ENDPOINT"] = "from-ssm", // Will be loaded from SSM
                ["SCIM_ACCESS_TOKEN"] = "from-ssm", // Will be loaded from SSM
                
                // Sync configuration optimized for Google Workspace
                ["SYNC_METHOD"] = "groups",
                ["GROUP_MATCH"] = "data-analysts-.*|data-engineers-.*", // Lake Formation groups
                ["USER_MATCH"] = ".*",
                ["INCLUDE_GROUPS"] = "true",
                ["LOG_LEVEL"] = "info",
                ["LOG_FORMAT"] = "json",
                
                // AWS configuration for parameter loading
                ["SSM_PARAMETER_PREFIX"] = $"/scim-sync/{_context.Environment.Name}"
            },
            Description = "SCIM synchronization function for Google Workspace to AWS Identity Center using slashdevops/idp-scim-sync"
        });
        
        Amazon.CDK.Tags.Of(ScimSyncFunction).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(ScimSyncFunction).Add("Environment", _context.Environment.Name);
    }

    private void CreateSyncScheduleRule()
    {
        var ruleName = _context.Namer.Custom("events-rule", ResourcePurpose.Internal);
        
        // Get sync frequency from CDK context or use environment defaults
        var contextKey = $"scim-sync.{_context.Environment.Name.ToLower()}";
        var syncConfig = Node.TryGetContext(contextKey) as Dictionary<string, object>;
        
        var syncFrequencyMinutes = _context.Environment.Name.ToLower() == "production" ? 15 : 30;
        var ruleEnabled = true;
        
        if (syncConfig != null)
        {
            if (syncConfig.TryGetValue("sync-frequency-minutes", out var frequency))
            {
                syncFrequencyMinutes = Convert.ToInt32(frequency);
            }
            if (syncConfig.TryGetValue("enabled", out var enabled))
            {
                ruleEnabled = Convert.ToBoolean(enabled);
            }
        }
        
        SyncScheduleRule = new Rule(this, "SyncScheduleRule", new RuleProps
        {
            RuleName = ruleName,
            Description = $"Scheduled trigger for SCIM synchronization every {syncFrequencyMinutes} minutes",
            Schedule = Schedule.Rate(Duration.Minutes(syncFrequencyMinutes)),
            Enabled = ruleEnabled, // Support for Tier 1 disable methodology
            Targets = new IRuleTarget[]
            {
                new LambdaFunction(ScimSyncFunction, new LambdaFunctionProps
                {
                    Event = RuleTargetInput.FromObject(new Dictionary<string, object>
                    {
                        ["source"] = "aws.events",
                        ["action"] = "sync",
                        ["environment"] = _context.Environment.Name,
                        ["syncFrequencyMinutes"] = syncFrequencyMinutes,
                        ["tier1DisableSupport"] = true
                    })
                })
            }
        });
        
        Amazon.CDK.Tags.Of(SyncScheduleRule).Add("Purpose", "SCIM-Synchronization");
        Amazon.CDK.Tags.Of(SyncScheduleRule).Add("Environment", _context.Environment.Name);
        Amazon.CDK.Tags.Of(SyncScheduleRule).Add("DisableMethodology", "Three-Tier");
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
        
        // Three-tier disable methodology outputs
        new CfnOutput(this, "Tier1DisableParameter", new CfnOutputProps
        {
            Value = $"/scim-sync/{_context.Environment.Name}/disable-controls/eventbridge-enabled",
            Description = "SSM parameter for Tier 1 disable control (EventBridge rule)",
            ExportName = $"{_context.Environment.Name}-scim-tier1-disable-param"
        });
        
        new CfnOutput(this, "Tier2DisableParameter", new CfnOutputProps
        {
            Value = $"/scim-sync/{_context.Environment.Name}/disable-controls/group-filter-disabled",
            Description = "SSM parameter for Tier 2 disable control (Group filter)",
            ExportName = $"{_context.Environment.Name}-scim-tier2-disable-param"
        });
        
        new CfnOutput(this, "Tier3DisableParameter", new CfnOutputProps
        {
            Value = $"/scim-sync/{_context.Environment.Name}/disable-controls/identity-center-disabled",
            Description = "SSM parameter for Tier 3 disable control (Identity Center)",
            ExportName = $"{_context.Environment.Name}-scim-tier3-disable-param"
        });
    }
}