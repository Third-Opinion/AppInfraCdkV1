# Tasks for the GitHub Actions and CDK Codebase Refactor
## Address Each of These Issues One by One.
## Commit after each item is complete if there were code changes.
## Store all temporary files in the `temp` directory. and do not commit them.
## Upon completion, update this file marking the item as complete. Do not commit for changes to this file.
## Don't ask for permission before running commands - just execute them

# Tasks List

## [x] 1. Update the workflow files to get the AWS account ID from the GitHub environment variable.
It should be based in the environment name (e.g. `development`, `production`).
You made need to update the trust policies for the roles to allow the GitHub environment to assume the roles.

### Detailed Steps:
1. Create GitHub environment variables for AWS account IDs:
   - Set `AWS_ACCOUNT_ID` in the `development` environment to `615299752206`
   - Set `AWS_ACCOUNT_ID` in the `production` environment to `442042533707`
2. Update workflow files to use environment variables:
   - In `deploy-dev.yml`: Change hardcoded account ID to `${{ vars.AWS_ACCOUNT_ID }}`
   - In `deploy-prod.yml`: Change hardcoded account ID to `${{ vars.AWS_ACCOUNT_ID }}`
   - In `infrastructure-pr.yml`: Change hardcoded account ID to use development account
3. Update IAM role trust policies to include environment-based subject claims:
   - Add `"repo:Third-Opinion/AppInfraCdkV1:environment:development"` to dev role trust policy
   - Add `"repo:Third-Opinion/AppInfraCdkV1:environment:production"` to prod role trust policy
4. Test workflows to ensure they can authenticate with the new configuration

## [x] 2. Rename the github-actions-dev-deploy and github-actions-prod-deploy to match the new naming convention outlined in this project.
You will need to recrate the roles with the new names and update the trust policies accordingly.
You should also update the workflow files to use the new role names.
Delete the old roles after the new ones are created and the workflows are updated.

### Detailed Steps:
1. Identify the new naming convention from the CDK codebase:
   - Search for naming patterns in `AppInfraCdkV1.Core/Constructs/Naming.cs`
   - Determine the pattern for IAM roles (likely: `{app}-{env}-github-actions-role`)
2. Create new IAM roles with proper names:
   - Development: `trialfinder-dev-github-actions-role` (or similar based on naming convention)
   - Production: `trialfinder-prd-github-actions-role`
3. Copy trust policies from existing roles to new roles:
   - Ensure all repository and branch patterns are included
   - Add environment-based claims if using GitHub environments
4. Update workflow files:
   - Replace old role ARNs with new role ARNs in all workflow files
   - Update `deploy-dev.yml`, `deploy-prod.yml`, and `infrastructure-pr.yml`
5. Test workflows with new roles
6. Delete old roles after confirming new roles work

## [x] 3. Complete remove the concept to shared resource between environments for the codebase.

### Detailed Steps:
1. Search for shared resource references:
   - Look for "shared", "common", or cross-environment resource references
   - Check for any S3 buckets, DynamoDB tables, or other resources used across environments
2. Identify shared resource patterns in CDK code:
   - Search in `AppInfraCdkV1.Core` for shared resource constructs
   - Check stack dependencies for cross-environment references
3. Refactor each shared resource:
   - Create environment-specific versions of each resource
   - Update references to use environment-specific resources
   - Ensure proper isolation between environments
4. Update any configuration or parameter files
5. Remove any shared resource stacks or constructs

## [x] 4. Simplify the resource size configuration in the CDK codebase. 
Sizing should be based on the environment name (e.g. `development`, `production`).
Sizing should be done in the CDK codebase, not in the workflow files.
Sizing should be done in a way that is easy to understand and maintain.
Sizing should be simple and not specific to any particular resource. So development should be smaller than production, but not specific to any resource type.

### Detailed Steps:
1. Create a centralized sizing configuration:
   - Add a new file `AppInfraCdkV1.Core/Configuration/EnvironmentSizing.cs`
   - Define size profiles for each environment (dev = small, prod = large)
2. Define standard size mappings:
   - EC2 instance types: dev = t3.micro/small, prod = t3.medium/large
   - RDS instance types: dev = db.t3.micro, prod = db.t3.medium
   - Lambda memory: dev = 512MB, prod = 1024MB
   - ECS task sizes: dev = 0.5 vCPU/1GB, prod = 2 vCPU/4GB
3. Create helper methods:
   - `GetInstanceSize(environment)`: Returns appropriate EC2 size
   - `GetDatabaseSize(environment)`: Returns appropriate RDS size
   - `GetComputeSize(environment)`: Returns vCPU/memory for containers
4. Update all CDK constructs:
   - Replace hardcoded sizes with calls to sizing helpers
   - Remove any size-related parameters from workflow files
5. Add unit tests for sizing logic

## [] 5. Update all the documentation to reflect the change to move from users to roles for GitHub Actions.

### Detailed Steps:
1. Update README files:
   - Search for references to IAM users in README.md files
   - Replace with information about IAM roles and OIDC authentication
2. Update CLAUDE.md:
   - Remove references to AWS CLI profiles if they're user-based
   - Add information about GitHub Actions role authentication
3. Create/update deployment documentation:
   - Document the OIDC setup process
   - Include troubleshooting guide for authentication issues
   - Add diagrams showing the authentication flow
4. Update any setup guides:
   - Remove steps for creating IAM users
   - Add steps for setting up OIDC providers and roles
5. Add security best practices documentation

## [] 6. Update the GitHub Actions job names to be a bit shorter and more descriptive.
For example change validate-infrastructure to validate-infra or validate-naming-conventions to validate-naming.

### Detailed Steps:
1. Update job names in all workflow files:
   - `validate-naming-conventions` → `validate-naming`
   - `validate-infrastructure` → `validate-infra`
   - `comment-naming-examples` → `comment-naming`
   - `run-tests` → `test`
2. Update any job dependencies:
   - Fix `needs:` arrays to use new job names
   - Ensure workflow DAG remains intact
3. Update status badge references if any
4. Test all workflows to ensure they still run correctly

## [] 7. Create a PR to develop with all the changes.

### Detailed Steps:
1. Ensure all changes are committed to the feature branch
2. Run all tests locally to verify nothing is broken
3. Create a comprehensive PR description:
   - List all changes made
   - Include testing instructions
   - Add screenshots if UI changes
   - Reference any related issues
4. Use GitHub CLI to create the PR:
   ```bash
   gh pr create --base develop --title "Refactor GitHub Actions and CDK infrastructure" --body "..."
   ```
5. Request reviews from appropriate team members
6. Monitor PR checks and address any failures