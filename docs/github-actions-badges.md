# GitHub Actions Status Badges

This document explains the GitHub Actions status badges displayed in the README.md file.

## Badge Organization

### Production (main branch)
These badges show the status of workflows running against the `main` branch, which represents the production-ready code.

- **Deploy to Production**: Shows whether the latest production deployment succeeded
- **Infrastructure Validation**: Shows whether infrastructure validation passed on main

### Development (develop branch)  
These badges show the status of workflows running against the `develop` branch, which represents the latest development code.

- **Deploy to Development**: Shows whether the latest development deployment succeeded
- **Infrastructure Validation**: Shows whether infrastructure validation passed on develop

### Code Quality (all branches)
These badges show the overall status of code quality workflows across all branches.

- **Claude Code Review**: Shows the status of automated code review workflows
- **Infrastructure Validation**: Shows the overall status of infrastructure validation

## Badge Status Meanings

| Badge Color | Status | Meaning |
|------------|--------|---------|
| 🟢 Green | passing | The workflow completed successfully |
| 🔴 Red | failing | The workflow failed |
| 🟡 Yellow | pending | The workflow is currently running |
| ⚫ Gray | no runs | No recent workflow runs |

## Workflow Details

### Deploy to Production
- **Trigger**: Push to `main` branch or manual dispatch
- **Purpose**: Deploys infrastructure to production AWS account
- **Requirements**: Manual approval for production deployments
- **Environment**: production GitHub environment with production AWS credentials

### Deploy to Development  
- **Trigger**: Push to `develop` branch
- **Purpose**: Deploys infrastructure to development AWS account
- **Environment**: development GitHub environment with development AWS credentials

### Infrastructure Validation
- **Trigger**: Push to any branch, pull requests
- **Purpose**: Validates naming conventions, runs tests, performs CDK synthesis
- **Matrix**: Tests multiple environments (Development, Production) and applications (TrialFinderV2)

### Claude Code Review
- **Trigger**: Pull requests
- **Purpose**: Automated code review and quality analysis
- **Features**: Security scanning, code quality checks, best practices validation

## Troubleshooting Badges

### Badge Not Updating
If a badge shows outdated information:
1. Check if the workflow file name in the badge URL matches the actual workflow file
2. Verify the branch name in the badge URL is correct
3. GitHub badges may have caching - wait a few minutes for updates

### Badge Shows "workflow not found"
This indicates:
- The workflow file name in the URL doesn't match the actual file
- The workflow file may have been renamed or moved
- Check `.github/workflows/` directory for correct filenames

### Badge Shows "no runs"
This means:
- No workflow runs have occurred for that specific branch
- The workflow may not be triggered by the specified branch
- Check the workflow's `on:` triggers in the YAML file

## Badge URL Format

GitHub Actions badges follow this format:
```
https://github.com/{owner}/{repo}/actions/workflows/{workflow-file}/badge.svg?branch={branch}
```

Example:
```
https://github.com/Third-Opinion/AppInfraCdkV1/actions/workflows/deploy-dev.yml/badge.svg?branch=develop
```

## Customizing Badges

To add new badges or modify existing ones:

1. Identify the workflow file name in `.github/workflows/`
2. Determine the appropriate branch to monitor
3. Add the badge using this markdown format:
```markdown
[![Badge Name](badge-url)](workflow-url)
```

## Related Files

- `README.md`: Contains the badge display
- `.github/workflows/`: Contains all workflow definitions
- This file: Documents the badge system