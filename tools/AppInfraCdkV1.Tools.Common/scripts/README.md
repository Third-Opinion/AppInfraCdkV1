# AppInfraCdkV1.Tools Scripts

This directory contains utility scripts for managing and configuring the AppInfraCdkV1 infrastructure.

## Script Categories

### Lake Formation (`lake-formation/`)
Scripts for setting up and managing AWS Lake Formation Identity Center integration:
- Prerequisites validation
- Development and production environment setup
- Integration verification and testing
- Cross-account Identity Center configuration

See [lake-formation/README.md](lake-formation/README.md) for detailed documentation.

### CDK Policy Management (`update-cdk-policy.sh`)
Script for updating CDK bootstrap policies and permissions.

## Usage

All scripts should be executed from the project root directory:

```bash
# From project root
cd AppInfraCdkV1.Tools/scripts

# Run a specific script
./lake-formation/check-prerequisites.sh dev
```

## Prerequisites

- AWS CLI v2.x or later
- Appropriate AWS credentials configured
- `jq` for JSON processing
- Bash 4.x or later

## Project Structure

```
AppInfraCdkV1.Tools/
├── scripts/
│   ├── lake-formation/        # Lake Formation Identity Center integration
│   │   ├── check-prerequisites.sh
│   │   ├── setup-lakeformation-identity-center-dev.sh
│   │   ├── setup-lakeformation-identity-center-prod.sh
│   │   ├── verify-integration.sh
│   │   ├── test-integration.sh
│   │   ├── README.md
│   │   └── CROSS_ACCOUNT_ARCHITECTURE.md
│   ├── update-cdk-policy.sh   # CDK policy management
│   └── README.md              # This file
└── ... (other Tools project files)
```

## Contributing

When adding new scripts:
1. Place them in appropriate subdirectories by category
2. Include comprehensive error handling
3. Add logging capabilities
4. Document usage in a README
5. Follow existing naming conventions
6. Make scripts executable (`chmod +x`)

## Security

- Never hardcode credentials in scripts
- Use AWS profiles or IAM roles for authentication
- Implement confirmation prompts for destructive operations
- Log all operations for audit purposes
- Follow the principle of least privilege