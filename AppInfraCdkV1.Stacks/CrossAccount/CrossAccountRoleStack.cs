using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using AppInfraCdkV1.Core.Models;
using Constructs;

namespace AppInfraCdkV1.Stacks.CrossAccount;

public class CrossAccountRoleStack : Stack
{
    public CrossAccountRoleStack(Construct scope,
        string id,
        IStackProps props,
        DeploymentContext context)
        : base(scope, id, props)
    {
        CreateGitHubOidcProvider();
        CreateDeploymentRole(context);
    }

    private void CreateGitHubOidcProvider()
    {
        // This is like creating a secure keycard system for GitHub to access your building
        new OpenIdConnectProvider(this, "GitHubOidcProvider", new OpenIdConnectProviderProps
        {
            Url = "https://token.actions.githubusercontent.com",
            ClientIds = new[] { "sts.amazonaws.com" },
            Thumbprints = new[] { "6938fd4d98bab03faadb97b34396831e3780aea1" }
        });
    }

    private void CreateDeploymentRole(DeploymentContext context)
    {
        var githubOrg = "your-github-org"; // TODO Replace with your actual GitHub org
        var githubRepo = "your-repo-name"; // TODO eplace with your actual repo name

        var role = new Role(this, "GitHubActionsRole", new RoleProps
        {
            RoleName = $"GitHubActions-{context.Environment.Name}-Role",
            AssumedBy = new WebIdentityPrincipal(
                $"arn:aws:iam::{context.Environment.AccountId}:oidc-provider/token.actions.githubusercontent.com",
                new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com",
                        ["token.actions.githubusercontent.com:sub"] = new object[]
                        {
                            $"repo:{githubOrg}/{githubRepo}:ref:refs/heads/main",
                            $"repo:{githubOrg}/{githubRepo}:ref:refs/heads/develop",
                            $"repo:{githubOrg}/{githubRepo}:pull_request"
                        }
                    }
                }
            ),
            Description = $"Role for GitHub Actions to deploy to {context.Environment.Name}",
            MaxSessionDuration = Duration.Hours(1)
        });

        // Attach necessary policies TODO GH deployer
        role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("PowerUserAccess"));

        // Add specific CDK permissions TODO review and adjust as needed
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "iam:CreateRole",
                "iam:DeleteRole",
                "iam:PutRolePolicy",
                "iam:DeleteRolePolicy",
                "iam:GetRole",
                "iam:PassRole",
                "iam:AttachRolePolicy",
                "iam:DetachRolePolicy",
                "sts:AssumeRole"
            },
            Resources = new[] { "*" }
        }));

        // Output the role ARN for GitHub secrets
        new CfnOutput(this, "GitHubActionsRoleArn", new CfnOutputProps
        {
            Value = role.RoleArn,
            Description = $"ARN of the GitHub Actions role for {context.Environment.Name}",
            ExportName = $"GitHubActionsRole-{context.Environment.Name}-Arn"
        });
    }
}