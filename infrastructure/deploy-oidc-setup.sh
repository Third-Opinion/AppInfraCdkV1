#!/bin/bash

# Deploy OIDC setup for GitHub Actions
# This script deploys the CloudFormation templates to set up GitHub OIDC authentication

set -e

echo "🚀 Deploying GitHub OIDC setup..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to check if AWS CLI is installed
check_aws_cli() {
    if ! command -v aws &> /dev/null; then
        echo -e "${RED}Error: AWS CLI is not installed${NC}"
        exit 1
    fi
}

# Function to check AWS credentials
check_aws_credentials() {
    if ! aws sts get-caller-identity &> /dev/null; then
        echo -e "${RED}Error: AWS credentials not configured or invalid${NC}"
        exit 1
    fi
}

# Function to deploy CloudFormation stack
deploy_stack() {
    local stack_name=$1
    local template_file=$2
    local account_type=$3
    
    echo -e "${YELLOW}Deploying $stack_name...${NC}"
    
    aws cloudformation deploy \
        --template-file "$template_file" \
        --stack-name "$stack_name" \
        --capabilities CAPABILITY_NAMED_IAM \
        --parameter-overrides \
            GitHubOrg=Third-Opinion \
            GitHubRepo=AppInfraCdkV1 \
        --tags \
            Purpose=GitHubActionsOIDC \
            AccountType="$account_type" \
            ManagedBy=CloudFormation
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Successfully deployed $stack_name${NC}"
    else
        echo -e "${RED}❌ Failed to deploy $stack_name${NC}"
        exit 1
    fi
}

# Main execution
main() {
    check_aws_cli
    check_aws_credentials
    
    # Get current AWS account ID
    ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
    
    echo "Current AWS Account ID: $ACCOUNT_ID"
    
    # Determine which template to deploy based on account ID
    if [ "$ACCOUNT_ID" == "615299752206" ]; then
        echo -e "${GREEN}Detected Development Account${NC}"
        deploy_stack "github-oidc-dev-setup" "dev-account-oidc-setup.yaml" "Development"
        
        # Output the role ARN for use in GitHub workflows
        ROLE_ARN=$(aws cloudformation describe-stacks \
            --stack-name github-oidc-dev-setup \
            --query 'Stacks[0].Outputs[?OutputKey==`DevDeployRoleArn`].OutputValue' \
            --output text)
        
        echo -e "\n${GREEN}Development Role ARN:${NC}"
        echo "$ROLE_ARN"
        echo -e "\n${YELLOW}Add this to your GitHub workflow (deploy-dev.yml):${NC}"
        echo "role-to-assume: $ROLE_ARN"
        
    elif [ "$ACCOUNT_ID" == "442042533707" ]; then
        echo -e "${GREEN}Detected Production Account${NC}"
        deploy_stack "github-oidc-prod-setup" "prod-account-oidc-setup.yaml" "Production"
        
        # Output the role ARN for use in GitHub workflows
        ROLE_ARN=$(aws cloudformation describe-stacks \
            --stack-name github-oidc-prod-setup \
            --query 'Stacks[0].Outputs[?OutputKey==`ProdDeployRoleArn`].OutputValue' \
            --output text)
        
        echo -e "\n${GREEN}Production Role ARN:${NC}"
        echo "$ROLE_ARN"
        echo -e "\n${YELLOW}Add this to your GitHub workflow (deploy-prod.yml):${NC}"
        echo "role-to-assume: $ROLE_ARN"
        
    else
        echo -e "${RED}Unknown AWS Account ID: $ACCOUNT_ID${NC}"
        echo "This script is configured for:"
        echo "  - Development Account: 615299752206"
        echo "  - Production Account: 442042533707"
        exit 1
    fi
    
    echo -e "\n${GREEN}✅ OIDC setup complete!${NC}"
    echo -e "${YELLOW}Next steps:${NC}"
    echo "1. Update your GitHub workflows to use OIDC authentication"
    echo "2. Add 'permissions: id-token: write' to your workflow jobs"
    echo "3. Remove AWS access keys from GitHub secrets after testing"
}

# Run main function
main "$@"