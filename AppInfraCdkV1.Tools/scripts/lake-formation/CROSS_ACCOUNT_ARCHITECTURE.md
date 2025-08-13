# Cross-Account Identity Center Architecture for Lake Formation

## Overview

This document explains the cross-account architecture where AWS Identity Center is hosted in the production account and shared with the development account for Lake Formation access control.

## Architecture Details

### Account Configuration

| Account Type | Account ID | Identity Center | Lake Formation |
|-------------|------------|-----------------|----------------|
| Production | 442042533707 | ✅ Hosted Here | ✅ Links to local IC |
| Development | 615299752206 | ❌ Uses Production IC | ✅ Links to prod IC |

### Identity Center Setup

```
┌─────────────────────────────────────────────────────────────┐
│                  Production Account (442042533707)          │
│                                                             │
│  ┌──────────────────────────────────────────────────┐     │
│  │           AWS Identity Center                     │     │
│  │                                                   │     │
│  │  • Google Workspace SCIM Integration             │     │
│  │  • Groups: data-analysts-dev                     │     │
│  │          data-analysts-phi                       │     │
│  │          data-engineers-phi                      │     │
│  │  • Users synced from Google Workspace            │     │
│  │  • Permission Sets for cross-account access      │     │
│  └──────────────────────────────────────────────────┘     │
│                           │                                 │
│                           │ Instance ARN                    │
│                           ▼                                 │
│  ┌──────────────────────────────────────────────────┐     │
│  │         Lake Formation (Production)              │     │
│  │  • Links to local Identity Center                │     │
│  │  • Manages production data access                │     │
│  └──────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
                            │
                            │ Cross-Account Reference
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                 Development Account (615299752206)          │
│                                                             │
│  ┌──────────────────────────────────────────────────┐     │
│  │         Lake Formation (Development)             │     │
│  │  • Links to Production Identity Center           │     │
│  │  • Uses same Instance ARN from prod              │     │
│  │  • Manages development data access               │     │
│  └──────────────────────────────────────────────────┘     │
│                                                             │
│  Note: No local Identity Center instance                   │
└─────────────────────────────────────────────────────────────┘
```

## How It Works

### 1. User Authentication Flow

1. User logs into AWS via Identity Center portal (hosted in production)
2. User selects development or production account access
3. Identity Center provides temporary credentials based on permission sets
4. User accesses Lake Formation resources in the selected account

### 2. Group Management

- All groups are created and managed in production Identity Center
- Google Workspace syncs groups to production Identity Center only
- Both accounts reference the same groups for access control
- Group changes in production automatically apply to both environments

### 3. Lake Formation Integration

#### Development Account Setup
```bash
aws lakeformation create-lake-formation-identity-center-configuration \
    --catalog-id 615299752206 \  # Dev account ID
    --instance-arn arn:aws:sso:::instance/ssoins-xxxxx \  # Prod IC instance
    --profile to-dev-admin
```

#### Production Account Setup
```bash
aws lakeformation create-lake-formation-identity-center-configuration \
    --catalog-id 442042533707 \  # Prod account ID
    --instance-arn arn:aws:sso:::instance/ssoins-xxxxx \  # Same IC instance
    --profile to-prd-admin
```

## Key Considerations

### 1. Prerequisites Validation

When running prerequisites check:
- Development environment checks must connect to production to verify Identity Center
- Group verification always happens against production Identity Center
- Both accounts need to trust the same Identity Center instance

### 2. Permission Sets

Permission sets in Identity Center must be configured to allow:
- Cross-account access from production to development
- Appropriate Lake Formation permissions in each account
- Group-based access control

### 3. Security Implications

- Single point of authentication (production Identity Center)
- Centralized user and group management
- Consistent access control across environments
- Audit logs centralized in production account

## Script Behavior

### Prerequisites Script (`check-prerequisites.sh`)
- When checking dev environment, switches to production profile for Identity Center verification
- Saves Identity Center configuration with production account reference
- Validates groups exist in production Identity Center

### Development Setup Script (`setup-lakeformation-identity-center-dev.sh`)
- Links development Lake Formation to production Identity Center
- Uses production Identity Center instance ARN
- Creates cross-account trust relationship

### Verification Script (`verify-integration.sh`)
- Always checks groups in production Identity Center
- Verifies Lake Formation configuration points to production IC
- Validates cross-account permissions are working

## Troubleshooting

### Common Issues and Solutions

1. **"Identity Center not found" in development**
   - This is expected - Identity Center only exists in production
   - Scripts automatically switch to production profile when needed

2. **Groups not visible in development Lake Formation**
   - Ensure groups exist in production Identity Center first
   - Verify Lake Formation is linked to production IC instance
   - Check cross-account permissions are configured

3. **Authentication failures**
   - Ensure both `to-dev-admin` and `to-prd-admin` profiles are authenticated
   - Run `aws sso login --profile to-prd-admin` for Identity Center operations

4. **Lake Formation linking fails**
   - Verify the Identity Center instance ARN is from production
   - Ensure development account trusts production Identity Center
   - Check IAM roles have necessary cross-account permissions

## Benefits of This Architecture

1. **Centralized Management**: Single source of truth for users and groups
2. **Consistency**: Same groups and permissions across all environments
3. **Simplified Operations**: One Identity Center to maintain
4. **Cost Efficiency**: No duplicate Identity Center infrastructure
5. **Security**: Centralized audit logging and access control

## Limitations

1. **Dependency**: Development environment depends on production Identity Center availability
2. **Blast Radius**: Issues with production IC affect all environments
3. **Network**: Cross-account calls may have slight latency
4. **Complexity**: Requires understanding of cross-account permissions

## Best Practices

1. Always test group changes in development first (even though groups are in production)
2. Monitor Identity Center health in production as it affects all environments
3. Set up CloudWatch alarms for Identity Center availability
4. Document all permission sets and their cross-account implications
5. Regular audit of cross-account access patterns
6. Maintain separate Lake Formation resources per environment despite shared authentication

## Related Documentation

- [AWS Identity Center Multi-Account Access](https://docs.aws.amazon.com/singlesignon/latest/userguide/manage-your-accounts.html)
- [Lake Formation Cross-Account Access](https://docs.aws.amazon.com/lake-formation/latest/dg/cross-account-perm-overview.html)
- [Identity Center Instance Configuration](https://docs.aws.amazon.com/singlesignon/latest/userguide/instanceconcept.html)