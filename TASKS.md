# Adress Each of These Issues One by One. Commit after each item is complete.
## Upon completion, update this file marking the item as complete

## [x] 1.Overly Permissive IAM Policy (iam-policies/cdk-deploy-policy-fixed.json)
Recommendation: Restrict resources to specific patterns or use condition keys
For example, modify the policy to limit all resources by the environment prefixs currentlly in use

"Resource": [
"arn:aws:s3:::dev-*",
"arn:aws:s3:::prod-*"
]
Use the aws cli to update the policy:

## [x] 2. Missing AWS Action Version Pin (deploy-prod.yml:97)
Using @v1 which is deprecated and has security vulnerabilities
Fix: Update to aws-actions/configure-aws-credentials@v4

## [x] 3. Production Region Inconsistency
deploy-prod.yml:23 sets region to us-east-1
appsettings.json:66 configures Production environment for us-east-2

## [x] 4. Improve code coverage
- Exclude infrastructure code from coverage requirements
- Include the itegration testing code in the coverage report

## [x] 5. Propose a solution to move to using roles vs users. Do not make any changes just propose a solution.
Recommendation: Use IAM roles for service accounts (IRSA) or AWS Identity Center for managing access
to AWS resources instead of IAM users. This allows for better security and management of permissions.