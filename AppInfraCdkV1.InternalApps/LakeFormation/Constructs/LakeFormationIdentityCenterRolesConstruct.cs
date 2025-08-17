using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;
using System.Collections.Generic;

namespace AppInfraCdkV1.InternalApps.LakeFormation.Constructs
{
    public interface ILakeFormationIdentityCenterRolesConstructProps
    {
        string Environment { get; }
        string AccountId { get; }
        string IdentityCenterInstanceArn { get; }
        Dictionary<string, string> IdentityCenterGroupIds { get; }
        bool CreateProductionRoles { get; }
    }

    public class LakeFormationIdentityCenterRolesConstructProps : ILakeFormationIdentityCenterRolesConstructProps
    {
        public string Environment { get; set; } = "Development";
        public string AccountId { get; set; } = string.Empty;
        public string IdentityCenterInstanceArn { get; set; } = "arn:aws:sso:::instance/ssoins-66849025a110d385";
        public Dictionary<string, string> IdentityCenterGroupIds { get; set; } = new Dictionary<string, string>();
        public bool CreateProductionRoles { get; set; } = false;
    }

    /// <summary>
    /// CDK Construct for creating IAM roles that map to Identity Center groups for Lake Formation access
    /// Resolves permissions stack deployment failures by providing IAM role ARNs instead of group names
    /// </summary>
    public class LakeFormationIdentityCenterRolesConstruct : Construct
    {
        // Development Environment Roles
        public Role DataAnalystDevelopmentRole { get; private set; }
        public Role DataEngineerDevelopmentRole { get; private set; }
        public Role AdminDevelopmentRole { get; private set; }
        public Role CatalogCreatorDevelopmentRole { get; private set; }

        // Production Environment Roles (only created if CreateProductionRoles = true)
        public Role DataAnalystProductionRole { get; private set; }
        public Role DataEngineerProductionRole { get; private set; }
        public Role AdminProductionRole { get; private set; }
        public Role CatalogCreatorProductionRole { get; private set; }

        private readonly ILakeFormationIdentityCenterRolesConstructProps _props;

        public LakeFormationIdentityCenterRolesConstruct(
            Construct scope,
            string id,
            ILakeFormationIdentityCenterRolesConstructProps props)
            : base(scope, id)
        {
            _props = props;

            // Always create development roles
            CreateDevelopmentRoles();

            // Only create production roles if explicitly requested
            if (_props.CreateProductionRoles)
            {
                CreateProductionRoles();
            }

            // Add construct tags
            Amazon.CDK.Tags.Of(this).Add("Component", "LakeFormationIdentityCenterRoles");
            Amazon.CDK.Tags.Of(this).Add("Environment", _props.Environment);
            Amazon.CDK.Tags.Of(this).Add("ManagedBy", "CDK");
        }

        /// <summary>
        /// Creates IAM roles for development environment Lake Formation access
        /// </summary>
        private void CreateDevelopmentRoles()
        {
            // LakeFormation-DataAnalyst-Development
            DataAnalystDevelopmentRole = CreateRole(
                "DataAnalystDevelopment",
                "LakeFormation-DataAnalyst-Development",
                "Lake Formation read-only access for development data analysts",
                new[] { "data-analysts-dev" },
                false // excludePHI: true
            );
            AttachDataAnalystPolicies(DataAnalystDevelopmentRole, "Development", false);

            // LakeFormation-DataEngineer-Development
            DataEngineerDevelopmentRole = CreateRole(
                "DataEngineerDevelopment", 
                "LakeFormation-DataEngineer-Development",
                "Lake Formation full access for development data engineers",
                new[] { "data-engineers-dev" },
                true // PHI access allowed
            );
            AttachDataEngineerPolicies(DataEngineerDevelopmentRole, "Development", true);

            // LakeFormation-Admin-Development
            AdminDevelopmentRole = CreateRole(
                "AdminDevelopment",
                "LakeFormation-Admin-Development", 
                "Lake Formation administrative access for development",
                new[] { "data-engineers-dev" }, // data-engineers-dev has isDataLakeAdmin: true
                true
            );
            AttachAdminPolicies(AdminDevelopmentRole, "Development");

            // LakeFormation-CatalogCreator-Development
            CatalogCreatorDevelopmentRole = CreateRole(
                "CatalogCreatorDevelopment",
                "LakeFormation-CatalogCreator-Development",
                "Lake Formation catalog creator access for development",
                new[] { "data-engineers-dev" },
                true
            );
            AttachCatalogCreatorPolicies(CatalogCreatorDevelopmentRole, "Development");
        }

        /// <summary>
        /// Creates IAM roles for production environment Lake Formation access
        /// </summary>
        private void CreateProductionRoles()
        {
            // LakeFormation-DataAnalyst-Production
            DataAnalystProductionRole = CreateRole(
                "DataAnalystProduction",
                "LakeFormation-DataAnalyst-Production",
                "Lake Formation PHI-enabled access for production data analysts",
                new[] { "data-analysts-phi" },
                true // PHI access in production
            );
            AttachDataAnalystPolicies(DataAnalystProductionRole, "Production", true);

            // LakeFormation-DataEngineer-Production
            DataEngineerProductionRole = CreateRole(
                "DataEngineerProduction",
                "LakeFormation-DataEngineer-Production",
                "Lake Formation full access for production data engineers", 
                new[] { "data-engineers-phi" },
                true
            );
            AttachDataEngineerPolicies(DataEngineerProductionRole, "Production", true);

            // LakeFormation-Admin-Production
            AdminProductionRole = CreateRole(
                "AdminProduction",
                "LakeFormation-Admin-Production",
                "Lake Formation administrative access for production",
                new[] { "data-lake-admin-prd" },
                true
            );
            AttachAdminPolicies(AdminProductionRole, "Production");

            // LakeFormation-CatalogCreator-Production
            CatalogCreatorProductionRole = CreateRole(
                "CatalogCreatorProduction",
                "LakeFormation-CatalogCreator-Production",
                "Lake Formation catalog creator access for production",
                new[] { "data-engineers-phi", "data-lake-admin-prd" }, // Multiple groups
                true
            );
            AttachCatalogCreatorPolicies(CatalogCreatorProductionRole, "Production");
        }

        /// <summary>
        /// Creates an IAM role with Identity Center trust policy for Lake Formation access
        /// </summary>
        private Role CreateRole(
            string constructId,
            string roleName, 
            string description,
            string[] identityCenterGroups,
            bool allowPHIAccess)
        {
            // Create the trust policy for Identity Center
            var trustPolicy = CreateIdentityCenterTrustPolicy(identityCenterGroups);

            // Use the correct SAML provider ARN for Identity Center
            var accountId = _props.AccountId;
            var samlProviderArn = $"arn:aws:iam::{accountId}:saml-provider/AWSSSO_a3bf03f788e071e7_DO_NOT_DELETE";
            
            var role = new Role(this, constructId, new RoleProps
            {
                RoleName = roleName,
                Description = description,
                AssumedBy = new FederatedPrincipal(
                    samlProviderArn,
                    new Dictionary<string, object>
                    {
                        ["StringEquals"] = new Dictionary<string, object>
                        {
                            ["SAML:aud"] = "https://signin.aws.amazon.com/saml"
                        }
                    },
                    "sts:AssumeRoleWithSAML"
                ),
                MaxSessionDuration = Duration.Hours(12), // 12 hour session duration
                // Additional inline policies will be added in subtask 39.2
            });

            // Add session tags for audit trails
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[] { "sts:TagSession" },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["aws:RequestedRegion"] = "us-east-2"
                    }
                }
            }));

            return role;
        }

        /// <summary>
        /// Creates trust policy conditions for specific Identity Center groups
        /// </summary>
        private Dictionary<string, object> CreateIdentityCenterTrustPolicy(string[] groupNames)
        {
            var conditions = new Dictionary<string, object>();
            
            // Add group ID conditions if group IDs are provided
            if (_props.IdentityCenterGroupIds.Count > 0)
            {
                var allowedGroupIds = new List<string>();
                foreach (var groupName in groupNames)
                {
                    if (_props.IdentityCenterGroupIds.ContainsKey(groupName))
                    {
                        allowedGroupIds.Add(_props.IdentityCenterGroupIds[groupName]);
                    }
                }

                if (allowedGroupIds.Count > 0)
                {
                    conditions["ForAnyValue:StringEquals"] = new Dictionary<string, object>
                    {
                        ["saml:groups"] = allowedGroupIds.ToArray()
                    };
                }
            }

            return conditions;
        }

        /// <summary>
        /// Gets all created development roles as a dictionary for easy access
        /// </summary>
        public Dictionary<string, Role> GetDevelopmentRoles()
        {
            return new Dictionary<string, Role>
            {
                ["DataAnalyst"] = DataAnalystDevelopmentRole,
                ["DataEngineer"] = DataEngineerDevelopmentRole,
                ["Admin"] = AdminDevelopmentRole,
                ["CatalogCreator"] = CatalogCreatorDevelopmentRole
            };
        }

        /// <summary>
        /// Gets all created production roles as a dictionary for easy access
        /// </summary>
        public Dictionary<string, Role> GetProductionRoles()
        {
            if (!_props.CreateProductionRoles)
                return new Dictionary<string, Role>();

            return new Dictionary<string, Role>
            {
                ["DataAnalyst"] = DataAnalystProductionRole,
                ["DataEngineer"] = DataEngineerProductionRole,
                ["Admin"] = AdminProductionRole,
                ["CatalogCreator"] = CatalogCreatorProductionRole
            };
        }

        /// <summary>
        /// Attaches policies for Data Analyst roles (SELECT, DESCRIBE permissions)
        /// </summary>
        private void AttachDataAnalystPolicies(Role role, string environment, bool allowPHI)
        {
            // Basic Lake Formation read permissions
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "lakeformation:GetDataAccess",
                    "lakeformation:DescribeResource",
                    "lakeformation:ListResources",
                    "lakeformation:ListPermissions",
                    "glue:GetDatabase",
                    "glue:GetDatabases", 
                    "glue:GetTable",
                    "glue:GetTables",
                    "glue:GetPartition",
                    "glue:GetPartitions"
                },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["aws:RequestedRegion"] = "us-east-2"
                    }
                }
            }));

            // S3 read permissions with PHI restrictions
            var s3Resources = new List<string>
            {
                $"arn:aws:s3:::thirdopinion-raw-{environment.ToLower()}-us-east-2",
                $"arn:aws:s3:::thirdopinion-raw-{environment.ToLower()}-us-east-2/*",
                $"arn:aws:s3:::thirdopinion-curated-{environment.ToLower()}-us-east-2",
                $"arn:aws:s3:::thirdopinion-curated-{environment.ToLower()}-us-east-2/*"
            };

            var s3Policy = new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:GetObject",
                    "s3:ListBucket",
                    "s3:GetBucketLocation"
                },
                Resources = s3Resources.ToArray()
            });

            // Add PHI restrictions for development data analysts
            if (!allowPHI)
            {
                s3Policy.AddCondition("StringNotLike", new Dictionary<string, object>
                {
                    ["s3:prefix"] = new[] { "*/phi/*", "phi/*" }
                });
            }

            role.AddToPolicy(s3Policy);

            // Athena query permissions
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "athena:GetQueryExecution",
                    "athena:GetQueryResults",
                    "athena:StartQueryExecution",
                    "athena:StopQueryExecution",
                    "athena:GetWorkGroup",
                    "athena:ListQueryExecutions"
                },
                Resources = new[]
                {
                    $"arn:aws:athena:us-east-2:{_props.AccountId}:workgroup/primary",
                    $"arn:aws:athena:us-east-2:{_props.AccountId}:workgroup/*{environment.ToLower()}*"
                }
            }));
        }

        /// <summary>
        /// Attaches policies for Data Engineer roles (ALL permissions in their environment)
        /// </summary>
        private void AttachDataEngineerPolicies(Role role, string environment, bool allowPHI)
        {
            // Full Lake Formation permissions for their environment
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "lakeformation:*",
                    "glue:*",
                    "athena:*"
                },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["aws:RequestedRegion"] = "us-east-2"
                    }
                }
            }));

            // Full S3 permissions for their environment buckets
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:*"
                },
                Resources = new[]
                {
                    $"arn:aws:s3:::thirdopinion-raw-{environment.ToLower()}-us-east-2",
                    $"arn:aws:s3:::thirdopinion-raw-{environment.ToLower()}-us-east-2/*",
                    $"arn:aws:s3:::thirdopinion-curated-{environment.ToLower()}-us-east-2",
                    $"arn:aws:s3:::thirdopinion-curated-{environment.ToLower()}-us-east-2/*"
                }
            }));

            // CloudWatch Logs permissions for query monitoring
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
                Resources = new[]
                {
                    $"arn:aws:logs:us-east-2:{_props.AccountId}:log-group:/aws/athena/*",
                    $"arn:aws:logs:us-east-2:{_props.AccountId}:log-group:/aws/lakeformation/*"
                }
            }));
        }

        /// <summary>
        /// Attaches policies for Admin roles (full Lake Formation administrative access)
        /// </summary>
        private void AttachAdminPolicies(Role role, string environment)
        {
            // Attach AWS managed Lake Formation admin policy
            role.AddManagedPolicy(ManagedPolicy.FromAwsManagedPolicyName("AWSLakeFormationDataAdmin"));

            // Additional administrative permissions
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "lakeformation:*",
                    "iam:ListRoles",
                    "iam:ListUsers",
                    "iam:ListGroups",
                    "iam:GetRole",
                    "iam:GetUser",
                    "iam:GetGroup",
                    "organizations:DescribeOrganization",
                    "organizations:ListAccounts",
                    "cloudtrail:DescribeTrails",
                    "cloudtrail:LookupEvents"
                },
                Resources = new[] { "*" }
            }));

            // Environment-specific resource management
            if (environment == "Development")
            {
                role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Effect = Effect.DENY,
                    Actions = new[] { "lakeformation:*" },
                    Resources = new[] { "*" },
                    Conditions = new Dictionary<string, object>
                    {
                        ["StringLike"] = new Dictionary<string, object>
                        {
                            ["aws:userid"] = "*:*production*"
                        }
                    }
                }));
            }
        }

        /// <summary>
        /// Attaches policies for Catalog Creator roles (CREATE_DATABASE, DESCRIBE, ALTER)
        /// </summary>
        private void AttachCatalogCreatorPolicies(Role role, string environment)
        {
            // Database creation and management permissions
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "glue:CreateDatabase",
                    "glue:UpdateDatabase",
                    "glue:DeleteDatabase",
                    "glue:GetDatabase",
                    "glue:GetDatabases",
                    "glue:CreateTable",
                    "glue:UpdateTable",
                    "glue:DeleteTable", 
                    "glue:GetTable",
                    "glue:GetTables",
                    "lakeformation:GrantPermissions",
                    "lakeformation:RevokePermissions",
                    "lakeformation:ListPermissions",
                    "lakeformation:BatchGrantPermissions",
                    "lakeformation:BatchRevokePermissions"
                },
                Resources = new[] { "*" },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["aws:RequestedRegion"] = "us-east-2"
                    }
                }
            }));

            // S3 location registration for new databases
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "lakeformation:RegisterResource",
                    "lakeformation:DeregisterResource",
                    "lakeformation:DescribeResource",
                    "lakeformation:ListResources"
                },
                Resources = new[] { "*" }
            }));

            // Limited S3 permissions for catalog metadata
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Effect = Effect.ALLOW,
                Actions = new[]
                {
                    "s3:ListBucket",
                    "s3:GetBucketLocation"
                },
                Resources = new[]
                {
                    $"arn:aws:s3:::thirdopinion-raw-{environment.ToLower()}-us-east-2",
                    $"arn:aws:s3:::thirdopinion-curated-{environment.ToLower()}-us-east-2"
                }
            }));
        }
    }
}