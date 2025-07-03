# Required GitHub Secrets Setup Instructions:

1. In your GitHub repository, go to Settings > Secrets and variables > Actions

2. Add the following secrets:
    - DEV_AWS_ROLE_ARN: arn:aws:iam::111111111111:role/GitHubActions-Development-Role
    - PROD_AWS_ROLE_ARN: arn:aws:iam::222222222222:role/GitHubActions-Production-Role

3. Make sure your AWS accounts have the OIDC provider and roles set up by deploying
   the CrossAccountRoleStack to each account first.

4. The workflow files will automatically assume these roles when deploying.