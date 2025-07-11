# AppInfraCdkV1 - AWS CDK Infrastructure as Code

## Status

### Production (master)
[![Deploy to Production](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/deploy-prod.yml/badge.svg?branch=master)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/deploy-prod.yml)
[![Infrastructure Validation](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml/badge.svg?branch=master)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml)

### Development (develop)
[![Deploy to Development](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/deploy-dev.yml/badge.svg?branch=develop)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/deploy-dev.yml)
[![Infrastructure Validation](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml/badge.svg?branch=develop)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml)

### Code Quality (all branches)
[![Claude Code Review](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/claude-code-review.yml/badge.svg)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/claude-code-review.yml)
[![Infrastructure Validation - Overall](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml/badge.svg)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml)
[![Coverage Status](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FThird-Opinion%2FAppInfraCdkV1%2Fmaster%2Fbadges%2Fcoverage.json&query=message&label=coverage&color=brightgreen)](https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/infrastructure-pr.yml)

## Overview

AppInfraCdkV1 is a comprehensive Infrastructure as Code (IaC) solution built with AWS CDK v2 (.NET) that provides a modular, scalable approach to deploying AWS infrastructure. This framework is designed to support multiple applications and environments with a focus on security, consistency, and maintainability.

## Background

This project addresses common challenges in AWS infrastructure management:
- **Multi-Environment Support**: Seamlessly deploy to Development, Integration, Staging, and Production environments
- **Naming Consistency**: Enforces standardized resource naming conventions across all AWS resources
- **Security by Default**: Implements security best practices with VPC isolation and security group management
- **Modular Architecture**: Allows easy extension for new applications while maintaining consistency
- **Type Safety**: Leverages C# and .NET's strong typing for safer infrastructure code

## Architecture

### Project Structure

```
AppInfraCdkV1/
├── AppInfraCdkV1.Core/           # Core abstractions and models
│   ├── Abstractions/             # Interfaces and base classes
│   ├── Extensions/               # Extension methods
│   ├── Models/                   # Data models (DeploymentContext, etc.)
│   └── Naming/                   # Naming convention logic
│       ├── NamingConvention.cs   # Resource naming rules
│       ├── EnvironmentType.cs    # Environment enumeration
│       ├── ApplicationType.cs    # Application enumeration
│       └── AwsRegion.cs          # AWS region enumeration
├── AppInfraCdkV1.Stacks/         # Reusable CDK stack definitions
│   ├── CrossAccount/             # Cross-account access stacks
│   └── WebApp/                   # Web application infrastructure
│       ├── WebApplicationStack.cs # Base web app stack
│       ├── S3BucketBundle.cs     # S3 bucket configurations
│       └── SecurityGroupBundle.cs # Security group configurations
├── AppInfraCdkV1.Apps/           # Application-specific stacks
│   └── TrialFinderV2/            # TrialFinder V2 application
│       └── TrialFinderV2Stack.cs # App-specific resources
├── AppInfraCdkV1.Deploy/         # CDK deployment entry point
│   ├── Program.cs                # Main deployment program
│   └── appsettings.json          # Deployment configuration
└── AppInfraCdkV1.Tests/          # Unit and integration tests
```

### Key Components

1. **DeploymentContext**: Central configuration model that carries environment, application, and deployment settings
2. **NamingConvention**: Enforces consistent resource naming across all AWS resources
3. **WebApplicationStack**: Base stack providing common web application infrastructure (VPC, ECS, RDS, etc.)
4. **Security Groups**: Modular security group management with proper ingress/egress rules

## Prerequisites

- .NET 8.0 SDK or later
- AWS CLI v2 (authentication handled via GitHub Actions OIDC)
- AWS CDK CLI v2 (`npm install -g aws-cdk`)
- Docker (for ECS container deployments)
- Node.js 18.x or later (for CDK CLI)

## Installation

1. Clone the repository:
```bash
git clone https://github.com/yourorg/AppInfraCdkV1.git
cd AppInfraCdkV1
```

2. Restore .NET dependencies:
```bash
dotnet restore
```

3. Build the solution:
```bash
dotnet build
```

## Configuration

### Environment Configuration

Edit `AppInfraCdkV1.Deploy/appsettings.json` to configure your deployment:

```json
{
  "Environment": {
    "Name": "Development",
    "AccountId": "123456789012",
    "Region": "us-east-2"
  },
  "Application": {
    "Name": "TrialFinderV2"
  }
}
```

### Supported Environments

- **Development** (`dev`): Non-production
- **Integration** (`int`): Non-production
- **Staging** (`stg`): Production account
- **Production** (`prod`): Production account

## Usage

### Two-Stage Deployment Process

This project uses a two-stage deployment approach for proper separation of shared and application-specific resources:

1. **Base Stack**: Shared infrastructure (VPC, security groups, shared resources)
2. **Application Stack**: Application-specific resources (ECS services, databases, etc.)

### Prerequisites

1. Bootstrap your AWS environment (first time only):
```bash
cd AppInfraCdkV1.Deploy
cdk bootstrap aws://ACCOUNT-ID/REGION
```

2. Ensure OIDC authentication is set up (see [GitHub Environment Setup](GITHUB_ENVIRONMENTS_SETUP.md))

### Deploy Base Stack First

```bash
cd AppInfraCdkV1.Deploy

# Deploy shared infrastructure for the environment
dotnet run -- --deploy-base

# Or using CDK directly with environment variable
CDK_DEPLOY_BASE=true cdk deploy
```

### Deploy Application Stack

```bash
# Deploy application stack (requires base stack to be deployed first)
dotnet run

# Or using CDK directly
cdk deploy
```

### Environment-Specific Deployment

The deployment automatically detects the environment based on environment variables:

```bash
# Set environment variables (if not using defaults)
export CDK_ENVIRONMENT=Development  # or Production
export CDK_APPLICATION=TrialFinderV2

# Deploy base stack
dotnet run -- --deploy-base

# Deploy application stack
dotnet run
```

### Validation and Testing

```bash
# Validate naming conventions only
dotnet run -- --validate-only

# Show resource names that would be created
dotnet run -- --show-names-only

# List available environments
dotnet run -- --list-environments
```

### Review Changes

```bash
# Review base stack changes
CDK_DEPLOY_BASE=true cdk diff

# Review application stack changes  
cdk diff
```

### Destroy Infrastructure

⚠️ **Important**: Destroy in reverse order to avoid dependency issues.

```bash
# 1. Destroy application stack first
cdk destroy

# 2. Then destroy base stack
CDK_DEPLOY_BASE=true cdk destroy
```

## Resource Naming Convention

All resources follow a standardized naming pattern:

```
{env-prefix}-{app-code}-{resource-type}-{region-code}-{specific-name}
```

Examples:
- VPC: `dev-tfv2-vpc-ue1-main`
- S3 Bucket: `thirdopinion.io-dev-tfv2-documents-ue1`
- Security Group: `dev-tfv2-sg-alb-web-ue1`

## Security Features

- **VPC Isolation**: Each environment gets its own VPC in production accounts
- **Security Groups**: Least-privilege security group rules
- **Encryption**: All data at rest is encrypted by default
- **IAM Roles**: Task-specific IAM roles with minimal permissions
- **OIDC Authentication**: GitHub Actions authenticate using OIDC (no stored credentials)
- **Secrets Management**: Integration with AWS Secrets Manager for sensitive data

## Documentation

### Setup and Configuration
- [GitHub Environment Setup](GITHUB_ENVIRONMENTS_SETUP.md) - OIDC authentication and environment configuration
- [GitHub OIDC Setup Guide](docs/github-oidc-setup-guide.md) - Detailed OIDC setup instructions
- [Base Stack Deployment](docs/BaseStackDeployment.md) - Shared infrastructure deployment guide

### Development and Testing
- [Integration Testing Plan](docs/integration-testing-plan.md) - Testing strategy and plans
- [Active Tasks](docs/active-tasks.md) - Current development tasks
- [Completed Tasks](docs/completed-tasks.md) - Historical task completion record

### Historical Reference
- [Historical IAM Users Setup](docs/historical-iam-users-setup.md) - Deprecated IAM user setup (pre-OIDC)
- [IAM Roles Migration Proposal](docs/iam-roles-migration-proposal.md) - Migration planning document

### Additional Resources
- [Branch Protection Setup](docs/branch-protection-setup.md) - GitHub branch protection configuration
- [GitHub Actions Badges](docs/github-actions-badges.md) - Badge setup and configuration

## Extending the Framework

### Adding a New Application

1. Create a new folder under `AppInfraCdkV1.Apps/YourAppName/`
2. Create a stack class inheriting from `WebApplicationStack`
3. Add application-specific resources in the constructor
4. Register the application in `NamingConvention.cs`

Example:
```csharp
public class YourAppStack : WebApplicationStack
{
    public YourAppStack(Construct scope, string id, IStackProps props, DeploymentContext context)
        : base(scope, id, props, context)
    {
        // Add your app-specific resources
    }
}
```

### Adding a New Environment

1. Add the environment to `EnvironmentType.cs` enum
2. Update `NamingConvention.cs` with the environment prefix and account type
3. Configure VPC CIDR ranges if needed

## Testing

Run the test suite:

```bash
dotnet test
```

Run with coverage:
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## CI/CD Integration

The project includes comprehensive GitHub Actions workflows with status badges shown at the top of this README:

### Deployment Workflows
- **Deploy to Production**: Automated deployment to production environment on `main` branch
- **Deploy to Development**: Automated deployment to development environment on `develop` branch

### Validation Workflows  
- **Infrastructure Validation**: Validates naming conventions, runs tests, and performs CDK synthesis
- **Claude Code Review**: Automated code review and quality checks
- **Claude Code**: Interactive code assistance and review

### Workflow Features
- Pull request validation
- Automated testing with comprehensive unit and integration tests
- CDK synthesis validation
- Naming convention enforcement
- Multi-environment deployment support
- Security scanning and validation

### Authentication
Deployments use GitHub Actions with OpenID Connect (OIDC) for secure, keyless authentication to AWS:
- **Development**: Uses role `dev-tfv2-role-ue2-github-actions` in account 615299752206
- **Production**: Uses role `prod-tfv2-role-ue2-github-actions` in account 442042533707
- No AWS access keys or secrets are stored in GitHub
- Authentication is handled automatically by GitHub Actions workflows

## Troubleshooting

### Common Issues

1. **CDK Bootstrap Error**: Ensure you've run `cdk bootstrap` for your account/region
2. **Permission Denied**: Verify your AWS credentials have sufficient permissions
3. **Resource Name Conflicts**: Check for existing resources with conflicting names
4. **Build Errors**: Ensure you have .NET 8.0 SDK installed

### Debug Mode

Enable detailed CDK logging:
```bash
export CDK_DEBUG=true
cdk deploy
```