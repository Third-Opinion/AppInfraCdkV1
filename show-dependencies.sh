#!/bin/bash

# Script to show CDK dependencies for all applications and stacks locally
# Usage: ./show-dependencies.sh [environment] [profile]
# Example: ./show-dependencies.sh Development to-dev-admin

set -e

# Default values
ENVIRONMENT=${1:-"Development"}
AWS_PROFILE=${2:-""}

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Function to print colored headers
print_header() {
    echo -e "\n${BLUE}=================================================${NC}"
    echo -e "${CYAN}$1${NC}"
    echo -e "${BLUE}=================================================${NC}"
}

print_subheader() {
    echo -e "\n${YELLOW}$1${NC}"
}

print_error() {
    echo -e "${RED}$1${NC}"
}

print_success() {
    echo -e "${GREEN}$1${NC}"
}

# Check if we're in the right directory
if [ ! -f "AppInfraCdkV1.Deploy/AppInfraCdkV1.Deploy.csproj" ]; then
    print_error "❌ Error: Please run this script from the AppInfraCdkV1 root directory"
    exit 1
fi

# Set AWS profile if provided
if [ ! -z "$AWS_PROFILE" ]; then
    export AWS_PROFILE="$AWS_PROFILE"
    echo -e "${GREEN}Using AWS Profile: $AWS_PROFILE${NC}"
fi

echo -e "${GREEN}Analyzing CDK Dependencies for Environment: $ENVIRONMENT${NC}"

cd AppInfraCdkV1.Deploy

# Function to set AWS credentials from SSO profile
set_aws_credentials() {
    if [ ! -z "$AWS_PROFILE" ]; then
        print_subheader "🔐 Setting up AWS credentials from profile: $AWS_PROFILE"
        # Export SSO credentials as environment variables for .NET AWS SDK
        eval $(aws configure export-credentials --profile "$AWS_PROFILE" --format env 2>/dev/null) || {
            print_error "⚠️  Could not export credentials for profile $AWS_PROFILE"
            print_error "   The script will continue but may show credential warnings"
        }
    fi
}

# Function to run CDK command and handle errors
run_cdk_command() {
    local app_name="$1"
    local command="$2"
    local description="$3"
    
    print_subheader "$description"
    
    # Set credentials before each CDK command
    set_aws_credentials
    
    if eval "$command" 2>/dev/null; then
        print_success "✅ Successfully retrieved dependencies for $app_name"
    else
        print_error "❌ No stacks found or error occurred for $app_name"
        echo "   Command: $command"
    fi
}

# Show Base Infrastructure Dependencies
print_header "🏗️  Base Infrastructure Dependencies"
run_cdk_command "Base" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_DEPLOY_BASE=true dotnet run -- --deploy-base cdk list --show-dependencies" \
    "Base Shared Infrastructure"

# Show TrialFinderV2 Dependencies
print_header "🚀 TrialFinderV2 Application Dependencies"
print_subheader "ALB Stack Dependencies"
run_cdk_command "TrialFinderV2-ALB" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialFinderV2 CDK_STACK_TYPE=ALB dotnet run -- --app=TrialFinderV2 --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialFinderV2 ALB Stack"

print_subheader "Cognito Stack Dependencies"
run_cdk_command "TrialFinderV2-COGNITO" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialFinderV2 CDK_STACK_TYPE=COGNITO dotnet run -- --app=TrialFinderV2 --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialFinderV2 Cognito Stack"

print_subheader "ECS Stack Dependencies"
run_cdk_command "TrialFinderV2-ECS" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialFinderV2 CDK_STACK_TYPE=ECS dotnet run -- --app=TrialFinderV2 --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialFinderV2 ECS Stack"

print_subheader "Data Stack Dependencies"
run_cdk_command "TrialFinderV2-DATA" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialFinderV2 CDK_STACK_TYPE=DATA dotnet run -- --app=TrialFinderV2 --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialFinderV2 Data Stack"

# Show TrialMatch Dependencies
print_header "🎯 TrialMatch Application Dependencies"
print_subheader "ALB Stack Dependencies"
run_cdk_command "TrialMatch-ALB" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialMatch CDK_STACK_TYPE=ALB dotnet run -- --app=TrialMatch --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialMatch ALB Stack"

print_subheader "Cognito Stack Dependencies"
run_cdk_command "TrialMatch-COGNITO" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialMatch CDK_STACK_TYPE=COGNITO dotnet run -- --app=TrialMatch --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialMatch Cognito Stack"

print_subheader "ECS Stack Dependencies"
run_cdk_command "TrialMatch-ECS" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialMatch CDK_STACK_TYPE=ECS dotnet run -- --app=TrialMatch --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialMatch ECS Stack"

print_subheader "Data Stack Dependencies"
run_cdk_command "TrialMatch-DATA" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=TrialMatch CDK_STACK_TYPE=DATA dotnet run -- --app=TrialMatch --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "TrialMatch Data Stack"

# Show LakeFormation Dependencies
print_header "🏞️  LakeFormation Application Dependencies"
run_cdk_command "LakeFormation" \
    "CDK_ENVIRONMENT='$ENVIRONMENT' CDK_APPLICATION=LakeFormation dotnet run -- --app=LakeFormation --environment='$ENVIRONMENT' cdk list --show-dependencies" \
    "LakeFormation Application Stacks"


# Show Complete Dependency Summary
print_header "📊 Complete Dependency Summary for $ENVIRONMENT"

echo ""
echo -e "${YELLOW}Expected Dependency Chain:${NC}"
echo "1. Base Infrastructure (shared-stack)"
echo "   ↓"
echo "2. Application Load Balancer & Cognito (parallel)"
echo "   ↓"
echo "3. Data/Storage Stacks (databases, S3 buckets)"
echo "   ↓" 
echo "4. ECS Services (depends on ALB, Cognito & Data stores)"
echo ""
echo -e "${YELLOW}LakeFormation Chain:${NC}"
echo "1. Storage Stack"
echo "   ↓"
echo "2. Setup Stack (depends on Storage)"
echo "   ↓"
echo "3. Permissions Stack (depends on Setup)"
echo "   ↓"
echo "4. HealthLake Test Instance (depends on Storage)"
echo ""
echo -e "${YELLOW}Individual Commands:${NC}"
echo "For Base Infrastructure:"
echo "  CDK_DEPLOY_BASE=true dotnet run -- --deploy-base cdk list --show-dependencies"
echo ""
echo "For TrialFinderV2 stacks (requires CDK_STACK_TYPE):"
echo "  CDK_STACK_TYPE=ALB dotnet run -- --app=TrialFinderV2 --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=COGNITO dotnet run -- --app=TrialFinderV2 --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=ECS dotnet run -- --app=TrialFinderV2 --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=DATA dotnet run -- --app=TrialFinderV2 --environment=$ENVIRONMENT cdk list --show-dependencies"
echo ""
echo "For TrialMatch stacks (requires CDK_STACK_TYPE):"
echo "  CDK_STACK_TYPE=ALB dotnet run -- --app=TrialMatch --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=COGNITO dotnet run -- --app=TrialMatch --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=ECS dotnet run -- --app=TrialMatch --environment=$ENVIRONMENT cdk list --show-dependencies"
echo "  CDK_STACK_TYPE=DATA dotnet run -- --app=TrialMatch --environment=$ENVIRONMENT cdk list --show-dependencies"
echo ""
echo "For LakeFormation stacks:"
echo "  dotnet run -- --app=LakeFormation --environment=$ENVIRONMENT cdk list --show-dependencies"
echo ""
echo -e "${YELLOW}Additional Useful Commands:${NC}"
echo "  cdk ls                              # List all stacks"
echo "  cdk diff <stack-name>              # Show differences"
echo "  cdk synth <stack-name>             # Synthesize CloudFormation"
echo ""

print_success "🎉 Dependency analysis complete!"