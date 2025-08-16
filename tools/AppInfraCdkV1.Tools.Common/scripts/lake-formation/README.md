# Lake Formation Identity Center Integration Scripts

This directory (`AppInfraCdkV1.Tools/scripts/lake-formation/`) contains scripts for setting up and managing the integration between AWS Lake Formation and AWS Identity Center (formerly AWS SSO) for synchronizing Google Workspace groups.

## Architecture Overview

**Important:** AWS Identity Center is only enabled in the production account (442042533707). The development account (615299752206) uses cross-account access from the production Identity Center instance. This means:

- All Identity Center groups and users are managed in the production account
- Development account references the production Identity Center for authentication
- Lake Formation in both accounts links to the same Identity Center instance
- Google Workspace sync happens only to the production Identity Center

## Overview

These scripts enable automated identity synchronization between Google Workspace and AWS IAM Identity Center, specifically for Lake Formation access control. The setup handles the cross-account Identity Center architecture where development uses production's Identity Center.

## Directory Structure

```
lake-formation/
├── run-once/                    # One-time setup scripts
│   ├── check-prerequisites.sh
│   ├── setup-lakeformation-identity-center-dev.sh
│   ├── setup-lakeformation-identity-center-prod.sh
│   ├── deploy-data-lake.sh
│   ├── lake-formation-identity-center-config-dev.json
│   ├── lake-formation-identity-center-config-prod.json
│   └── README.md
├── verify-integration.sh        # Regular validation script
├── test-integration.sh         # Testing script
├── backups/                    # Backup directory
├── SCRIPTS_STATUS.md          # Script status documentation
├── CROSS_ACCOUNT_ARCHITECTURE.md
└── README.md                   # This file
```

## Scripts

### One-Time Setup Scripts (in `run-once/` directory)

These scripts are for initial setup only. See [run-once/README.md](run-once/README.md) for details.

1. **check-prerequisites.sh** - Validates prerequisites before setup
2. **setup-lakeformation-identity-center-dev.sh** - Development Identity Center setup
3. **setup-lakeformation-identity-center-prod.sh** - Production Identity Center setup
4. **deploy-data-lake.sh** - CDK deployment orchestration (consider using GitHub Actions instead)

### Regular Use Scripts

#### Integration Verification (`verify-integration.sh`)

Verifies that Lake Formation Identity Center integration is properly configured.

**Usage:**
```bash
./verify-integration.sh [dev|prod]
```

**Verification Steps:**
1. Lake Formation configuration status
2. Identity Center instance validation
3. Group synchronization status
4. Lake Formation permissions check
5. Generates verification report

**Output:**
- Console output with color-coded status
- JSON report: `lake-formation-verification-{env}-{timestamp}.json`

### 5. Test Suite (`test-integration.sh`)

Comprehensive test suite for validating the entire integration setup.

**Usage:**
```bash
./test-integration.sh
```

**Test Coverage:**
1. Prerequisites validation
2. AWS authentication
3. Identity Center configuration
4. Lake Formation configuration
5. Group synchronization
6. Rollback capability
7. Setup script functionality
8. Verification script execution
9. Configuration files
10. Log file generation

**Output:**
- Real-time test results with pass/fail status
- Test report: `test-report-{timestamp}.txt`
- Summary with pass rate calculation

## Setup Workflow

### Initial Setup (Development)

1. **Check Prerequisites:**
   ```bash
   ./check-prerequisites.sh dev
   ```

2. **Run Development Setup:**
   ```bash
   ./setup-lakeformation-identity-center-dev.sh
   ```

3. **Verify Integration:**
   ```bash
   ./verify-integration.sh dev
   ```

4. **Run Tests:**
   ```bash
   ./test-integration.sh
   ```

### Production Deployment

1. **Ensure Development is Working:**
   - Verify development setup is complete and tested
   - Review development configuration

2. **Check Production Prerequisites:**
   ```bash
   ./check-prerequisites.sh prod
   ```

3. **Run Production Setup (with caution):**
   ```bash
   ./setup-lakeformation-identity-center-prod.sh
   ```

4. **Verify Production Integration:**
   ```bash
   ./verify-integration.sh prod
   ```

## Configuration Files

### Generated Files

- `.identity-center-env` - Identity Center configuration (auto-generated)
- `lake-formation-identity-center-config-dev.json` - Development configuration
- `lake-formation-identity-center-config-prod.json` - Production configuration
- `lake-formation-setup-{env}-{timestamp}.log` - Setup logs
- `lake-formation-verification-{env}-{timestamp}.json` - Verification reports
- `test-report-{timestamp}.txt` - Test execution reports

### Backup Files

Production setup creates comprehensive backups in:
```
backups/prod-{timestamp}/
├── data-lake-settings.json
├── identity-center-config.json
├── permissions.json
├── resources.json
└── backup-summary.txt
```

## Environment Variables

The scripts use AWS profiles for authentication:
- Development: `to-dev-admin`
- Production: `to-prd-admin`

Optional environment variables:
- `AWS_REGION` - Override default region (us-east-2)

## Troubleshooting

### Common Issues

1. **Authentication Failed:**
   ```bash
   aws sso login --profile to-dev-admin
   # or
   aws sso login --profile to-prd-admin
   ```

2. **Groups Not Found (Expected Behavior):**
   - **This is normal for newly created groups**
   - Groups are created in Google Workspace first
   - Sync to AWS Identity Center takes 15-40 minutes
   - Scripts will show warnings (not errors) for missing groups
   - You can proceed with Lake Formation setup - groups will work once synced
   - To check sync status:
     - Wait 15-40 minutes after creating groups in Google Workspace
     - Re-run `./check-prerequisites.sh dev` to verify sync completion
   - Remember: Groups are only in production Identity Center (442042533707)

3. **Lake Formation Access Denied:**
   - Ensure IAM role has Lake Formation admin permissions
   - Verify account ID matches expected value
   - Check that cross-account permissions are set up correctly

4. **Identity Center Not Found:**
   - Identity Center is only in production account (442042533707)
   - Development account should reference production's Identity Center
   - Check correct region is being used (us-east-2)

5. **Cross-Account Issues:**
   - Verify production Identity Center allows development account access
   - Check that the Identity Center instance ARN is from production
   - Ensure Lake Formation trusts the production Identity Center

### Rollback Procedures

If issues occur during setup:

1. **Development Environment:**
   - Review logs in `lake-formation-setup-dev-*.log`
   - Manually remove configuration if needed
   - Re-run setup script after fixing issues

2. **Production Environment:**
   - Use backup files in `backups/prod-{timestamp}/`
   - Contact the team before making manual changes
   - Consider running restore procedures (manual process)

## Security Considerations

- All scripts use AWS SSO profiles for authentication
- No credentials are stored in scripts
- Production changes require explicit confirmation
- All operations are logged for audit purposes
- Sensitive information is not displayed in console output

## Requirements

- AWS CLI v2.x or later
- `jq` for JSON processing
- Bash 4.x or later
- Appropriate AWS IAM permissions
- Access to Google Workspace Admin (for group creation)

## Support

For issues or questions:
1. Check the test suite output for specific failures
2. Review log files for detailed error messages
3. Verify prerequisites are met
4. Contact the infrastructure team for assistance

## Related Documentation

- [AWS Lake Formation Documentation](https://docs.aws.amazon.com/lake-formation/)
- [AWS Identity Center Documentation](https://docs.aws.amazon.com/singlesignon/)
- [Google Workspace Admin SDK](https://developers.google.com/admin-sdk)
- [slashdevops/idp-scim-sync](https://github.com/slashdevops/idp-scim-sync) (for SCIM synchronization)