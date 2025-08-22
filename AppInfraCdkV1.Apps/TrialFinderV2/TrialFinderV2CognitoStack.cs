using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.IAM;
using AppInfraCdkV1.Apps.TrialFinderV2.Configuration;
using AppInfraCdkV1.Core.Enums;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Apps.TrialFinderV2;

/// <summary>
/// Cognito Stack for TrialFinder V2 application
/// 
/// This stack manages Cognito User Pool and App Client with the following features:
/// - User Pool with email-based authentication
/// - App Client with OAuth 2.0 configuration
/// - Managed login domain with branding version for sign-in/sign-out
/// - Environment-specific configuration
/// - Comprehensive IAM roles and permissions
/// </summary>
public class TrialFinderV2CognitoStack : Stack
{
    private readonly DeploymentContext _context;
    private readonly ConfigurationLoader _configLoader;
    
    public TrialFinderV2CognitoStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        _context = context;
        _configLoader = new ConfigurationLoader();

        // Load configuration
        var fullConfig = _configLoader.LoadFullConfig(context.Environment.Name);

        // Create Cognito User Pool
        var userPool = CreateUserPool(context);
        
        // Create Cognito App Client
        var appClient = CreateAppClient(userPool, context);
        
        // Create managed login domain
        var domain = CreateManagedDomain(userPool, context);
        
        // Export outputs for other stacks
        ExportStackOutputs(userPool, appClient, domain, context);
    }

    /// <summary>
    /// Create Cognito User Pool with email-based authentication
    /// </summary>
    private IUserPool CreateUserPool(DeploymentContext context)
    {
        var userPool = new UserPool(this, "TrialFinderUserPool", new UserPoolProps
        {
            UserPoolName = context.Namer.CognitoUserPool(ResourcePurpose.Auth),
            SelfSignUpEnabled = true,
            UserVerification = new UserVerificationConfig
            {
                EmailSubject = "Verify your email for TrialFinder",
                EmailBody = "Thanks for signing up! Your verification code is {####}",
                EmailStyle = VerificationEmailStyle.CODE
            },
            SignInAliases = new SignInAliases
            {
                Email = true
            },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute
                {
                    Required = true,
                    Mutable = true
                },
                GivenName = new StandardAttribute
                {
                    Required = true,
                    Mutable = true
                },
                FamilyName = new StandardAttribute
                {
                    Required = true,
                    Mutable = true
                }
            },
            CustomAttributes = new Dictionary<string, ICustomAttribute>
            {
                ["tenantGuid"] = new StringAttribute(new StringAttributeProps
                {
                    MinLen = 36,
                    MaxLen = 36,
                    Mutable = true
                }),
                ["tenantName"] = new StringAttribute(new StringAttributeProps
                {
                    MaxLen = 24,
                    Mutable = true
                })
            },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 8,
                RequireLowercase = true,
                RequireUppercase = true,
                RequireDigits = true,
                RequireSymbols = true,
                TempPasswordValidity = Duration.Days(7)
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            Mfa = Mfa.OPTIONAL,
            MfaSecondFactor = new MfaSecondFactor
            {
                Otp = true,
                Sms = true
            },
            DeviceTracking = new DeviceTracking
            {
                ChallengeRequiredOnNewDevice = true,
                DeviceOnlyRememberedOnUserPrompt = false
            },
            Email = UserPoolEmail.WithCognito("griffin@thirdopinion.io"),
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        return userPool;
    }

    /// <summary>
    /// Create Cognito App Client with OAuth 2.0 configuration
    /// </summary>
    private IUserPoolClient CreateAppClient(IUserPool userPool, DeploymentContext context)
    {
        var appClient = new UserPoolClient(this, "TrialFinderAppClient", new UserPoolClientProps
        {
            UserPool = userPool,
            UserPoolClientName = context.Namer.CognitoAppClient(ResourcePurpose.Auth),
            GenerateSecret = true,
            // AuthFlows configuration - using default flows
            OAuth = new OAuthSettings
            {
                Flows = new OAuthFlows
                {
                    AuthorizationCodeGrant = true
                },
                Scopes = new[]
                {
                    OAuthScope.EMAIL,
                    OAuthScope.OPENID,
                    OAuthScope.PHONE,
                    OAuthScope.PROFILE
                },
                CallbackUrls = GetCallbackUrls(context),
                LogoutUrls = GetLogoutUrls(context)
            },
            PreventUserExistenceErrors = true,
            EnableTokenRevocation = true,
            AccessTokenValidity = Duration.Minutes(60),
            IdTokenValidity = Duration.Minutes(60),
            RefreshTokenValidity = Duration.Days(5),
            AuthSessionValidity = Duration.Minutes(3)
        });

        return appClient;
    }

    /// <summary>
    /// Create managed login domain for Cognito User Pool with branding version
    /// </summary>
    private IUserPoolDomain CreateManagedDomain(IUserPool userPool, DeploymentContext context)
    {
        var domain = userPool.AddDomain("TrialFinderManagedDomain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions
            {
                DomainPrefix = GetDomainPrefix(context)
            },
            ManagedLoginVersion = ManagedLoginVersion.NEWER_MANAGED_LOGIN
        });

        return domain;
    }

    /// <summary>
    /// Get callback URLs based on environment
    /// </summary>
    private string[] GetCallbackUrls(DeploymentContext context)
    {
        return context.Environment.Name.ToLowerInvariant() switch
        {
            "production" => new[]
            {
                "https://tf.thirdopinion.io/signin-oidc"
            },
            "staging" => new[]
            {
                "https://stg-tf.thirdopinion.io/signin-oidc"
            },
            "development" => new[]
            {
                "https://dev-tf.thirdopinion.io/signin-oidc",
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            },
            "integration" => new[]
            {
                "https://int-tf.thirdopinion.io/signin-oidc",
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            },
            _ => new[]
            {
                "https://localhost:7015/signin-oidc",
                "https://localhost:7243/signin-oidc"
            }
        };
    }

    /// <summary>
    /// Get logout URLs based on environment
    /// </summary>
    private string[] GetLogoutUrls(DeploymentContext context)
    {
        return context.Environment.Name.ToLowerInvariant() switch
        {
            "production" => new[]
            {
                "https://tf.thirdopinion.io",
                "https://tf.thirdopinion.io/logout"
            },
            "staging" => new[]
            {
                "https://stg-tf.thirdopinion.io",
                "https://stg-tf.thirdopinion.io/logout"
            },
            "development" => new[]
            {
                "https://dev-tf.thirdopinion.io",
                "https://dev-tf.thirdopinion.io/logout",
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            },
            "integration" => new[]
            {
                "https://int-tf.thirdopinion.io",
                "https://int-tf.thirdopinion.io/logout",
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            },
            _ => new[]
            {
                "https://localhost:7015",
                "https://localhost:7015/logout",
                "https://localhost:7243",
                "https://localhost:7243/logout"
            }
        };
    }

    /// <summary>
    /// Get domain prefix based on environment
    /// </summary>
    private string GetDomainPrefix(DeploymentContext context)
    {
        var envPrefix = context.Environment.Name.ToLowerInvariant();
        var appCode = context.Application.Name.ToLowerInvariant();
        return $"{envPrefix}-{appCode}-auth";
    }

    /// <summary>
    /// Export stack outputs for consumption by other stacks
    /// </summary>
    private void ExportStackOutputs(IUserPool userPool, IUserPoolClient appClient, 
        IUserPoolDomain domain, DeploymentContext context)
    {
        // Export User Pool ID
        new CfnOutput(this, "UserPoolId", new CfnOutputProps
        {
            Value = userPool.UserPoolId,
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-user-pool-id",
            Description = "ID of the Cognito User Pool"
        });

        // Export User Pool ARN
        new CfnOutput(this, "UserPoolArn", new CfnOutputProps
        {
            Value = userPool.UserPoolArn,
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-user-pool-arn",
            Description = "ARN of the Cognito User Pool"
        });

        // Export App Client ID
        new CfnOutput(this, "AppClientId", new CfnOutputProps
        {
            Value = appClient.UserPoolClientId,
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-app-client-id",
            Description = "ID of the Cognito App Client"
        });

        // Export App Client Secret (actual value for secret creation)
        new CfnOutput(this, "AppClientSecret", new CfnOutputProps
        {
            Value = appClient.UserPoolClientSecret.ToString(),
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-app-client-secret",
            Description = "Secret of the Cognito App Client (for secure secret creation)"
        });

        // Export Domain URL
        new CfnOutput(this, "DomainUrl", new CfnOutputProps
        {
            Value = $"https://{domain.DomainName}.auth.{context.Environment.Region}.amazoncognito.com",
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-cognito-domain-url",
            Description = "URL of the Cognito managed login domain"
        });

        // Export Domain Name
        new CfnOutput(this, "DomainName", new CfnOutputProps
        {
            Value = domain.DomainName,
            ExportName = $"{context.Environment.Name}-{context.Application.Name}-cognito-domain-name",
            Description = "Name of the Cognito managed login domain"
        });
    }
} 