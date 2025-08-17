# Lake Formation Identity Center Integration

This document provides an overview of the Identity Center integration for AWS Lake Formation, including the IAM roles, permission sets, and validation tools created as part of Task 39.

## Overview

The Lake Formation infrastructure now includes comprehensive Identity Center integration that maps existing groups to specialized IAM roles with appropriate Lake Formation permissions. This resolves the "Not a supported datalake principal identifier format" errors by using IAM role ARNs instead of Identity Center group references directly in Lake Formation permissions.

## Architecture

```
Identity Center Groups → Permission Sets → IAM Roles → Lake Formation Permissions
```

### Identity Center Groups
- `data-analysts-dev` (018b8550-9071-70ef-4204-7120281ac19b)
- `data-engineers-dev` (613b1560-20b1-70c1-06fb-3ab507e41773)  
- `data-analysts-phi` (511be500-20f1-707b-80ca-33140a93b483)
- `data-engineers-phi` (c12b0510-8081-70d2-945d-77f64fbd73c6)
- `data-lake-admin-prd` (d14b65b0-10d1-70da-6801-3b67fa213c71)

### IAM Roles Created
- **Development Environment:**
  - `LakeFormation-DataAnalyst-Development` (read-only, no PHI)
  - `LakeFormation-DataEngineer-Development` (full access + admin)
  - `LakeFormation-Admin-Development` (administrative access)
  - `LakeFormation-CatalogCreator-Development` (database creation)

- **Production Environment:**
  - `LakeFormation-DataAnalyst-Production` (read-only, PHI enabled)
  - `LakeFormation-DataEngineer-Production` (full access)
  - `LakeFormation-Admin-Production` (administrative access)
  - `LakeFormation-CatalogCreator-Production` (database creation)

## Files and Components

### CDK Constructs
- **`LakeFormationIdentityCenterRolesConstruct.cs`**: Creates IAM roles with Identity Center trust policies
- **`LakeFormationSetupStack.cs`**: Updated to use IAM role ARNs in data lake settings
- **`LakeFormationPermissionsStack.cs`**: Modified to grant permissions to IAM roles instead of groups

### Documentation
- **`docs/identity-center-permission-sets-setup.md`**: Comprehensive setup guide for Identity Center permission sets
- **`README-IdentityCenter-Integration.md`**: This overview document

### Validation Scripts
- **`scripts/verify-permission-sets.sh`**: Validates that all required permission sets exist
- **`scripts/verify-group-assignments.sh`**: Checks group-to-permission-set assignments
- **`scripts/test-role-assumption.sh`**: Tests IAM role accessibility and permissions

## Setup Process

### 1. Deploy CDK Infrastructure
```bash
# Deploy base infrastructure
AWS_PROFILE=to-dev-admin npx cdk deploy dev-lf-storage-ue2 --require-approval never
AWS_PROFILE=to-dev-admin npx cdk deploy dev-lf-setup-ue2 --require-approval never
AWS_PROFILE=to-dev-admin npx cdk deploy dev-lf-permissions-ue2 --require-approval never
```

### 2. Configure Identity Center Permission Sets
Since CDK cannot automatically create Identity Center permission sets, follow the manual setup guide:

```bash
# Review the setup documentation
cat docs/identity-center-permission-sets-setup.md
```

Key steps:
1. Create 5 permission sets in Identity Center console
2. Configure inline policies for role assumption
3. Set required session tags for audit trails
4. Assign permission sets to appropriate groups

### 3. Validate Configuration
```bash
# Test all components
./scripts/verify-permission-sets.sh
./scripts/verify-group-assignments.sh  
./scripts/test-role-assumption.sh
```

## Permission Matrix

| Group | Environment | Role(s) | PHI Access | Admin Rights | Session Duration |
|-------|-------------|---------|------------|--------------|------------------|
| data-analysts-dev | Development | DataAnalyst | No | No | 8 hours |
| data-engineers-dev | Development | DataEngineer, Admin, CatalogCreator | Yes | Yes | 8 hours |
| data-analysts-phi | Production | DataAnalyst | Yes | No | 4 hours |
| data-engineers-phi | Production | DataEngineer, CatalogCreator | Yes | No | 4 hours |
| data-lake-admin-prd | Production | Admin, CatalogCreator | Yes | Yes | 2 hours |

## Security Features

### Session Tags
All roles enforce session tagging for audit trails:
- `Environment`: Development/Production
- `AccessLevel`: Analyst/Engineer/Admin  
- `PHIAccess`: Enabled/Disabled
- `AdminRole`: true/false (where applicable)

### Trust Policies
- All roles trust Identity Center SAML federation
- Session duration limits enforced
- Regional restrictions to us-east-2

### Least Privilege
- Development analysts cannot access PHI data
- Production sessions have shorter durations
- Administrative access limited to 2-hour sessions
- Environment-specific role separation

## Troubleshooting

### Common Issues

1. **"Not a supported datalake principal identifier format"**
   - **Cause**: Using Identity Center group ARNs directly in Lake Formation
   - **Solution**: Ensure permissions stack uses IAM role ARNs from the roles construct

2. **Role assumption fails**
   - **Cause**: Permission set not properly configured or assigned
   - **Solution**: Run validation scripts and check Identity Center console

3. **Lake Formation access denied**
   - **Cause**: Role not configured as data lake admin or missing permissions
   - **Solution**: Verify data lake settings include role ARNs

### Validation Commands
```bash
# Check role existence
aws iam get-role --role-name LakeFormation-DataAnalyst-Development

# Check Lake Formation settings
aws lakeformation get-data-lake-settings

# Check Identity Center assignments
aws sso-admin list-account-assignments --instance-arn <INSTANCE_ARN> --account-id <ACCOUNT_ID>
```

## Implementation Status

✅ **Completed (Task 39)**:
- [x] 39.1: Created LakeFormationIdentityCenterRolesConstruct
- [x] 39.2: Attached comprehensive role policies  
- [x] 39.3: Updated LakeFormationSetupStack to use role ARNs
- [x] 39.4: Modified LakeFormationPermissionsStack for role-based permissions
- [x] 39.5: Created Identity Center permission set documentation and validation tools

## Next Steps

1. **Deploy Updated Stacks**: Deploy the modified Lake Formation stacks to test role-based permissions
2. **Configure Permission Sets**: Follow the manual setup guide to create Identity Center permission sets
3. **Test End-to-End**: Validate that users can access Lake Formation through their assigned roles
4. **Monitor and Audit**: Review CloudTrail logs to ensure session tags are properly recorded

## Compliance Notes

- All access is logged through CloudTrail with session tags
- PHI access is explicitly controlled and documented
- Session durations follow least-privilege principles
- Role separation prevents cross-environment access
- Administrative access has enhanced security controls