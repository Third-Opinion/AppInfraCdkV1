# Identity Center Permission Sets Setup for Lake Formation

This document provides step-by-step instructions for configuring AWS Identity Center Permission Sets that map to the Lake Formation IAM roles created by our CDK infrastructure.

## Overview

The Lake Formation infrastructure creates IAM roles with proper permissions and trust policies for Identity Center integration. However, AWS CDK cannot automatically create Identity Center Permission Sets due to cross-account service constraints. This document provides manual setup instructions for creating the required permission sets.

## Prerequisites

- AWS Identity Center is configured and active
- Lake Formation IAM roles have been deployed via CDK
- Administrator access to the AWS Identity Center console
- The following Identity Center groups exist:
  - `data-analysts-dev` (ID: 018b8550-9071-70ef-4204-7120281ac19b)
  - `data-engineers-dev` (ID: 613b1560-20b1-70c1-06fb-3ab507e41773)
  - `data-analysts-phi` (ID: 511be500-20f1-707b-80ca-33140a93b483)
  - `data-engineers-phi` (ID: c12b0510-8081-70d2-945d-77f64fbd73c6)
  - `data-lake-admin-prd` (ID: d14b65b0-10d1-70da-6801-3b67fa213c71)

## Permission Set Specifications

### 1. LakeFormation-DataAnalyst-Dev-PermissionSet

**Purpose**: Provides read-only access to development Lake Formation resources for data analysts

**Configuration**:
- **Name**: `LakeFormation-DataAnalyst-Dev-PermissionSet`
- **Description**: Lake Formation read-only access for development data analysts
- **Session Duration**: 8 hours
- **Relay State**: (optional) `https://console.aws.amazon.com/lakeformation/`

**Inline Policy**:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": "arn:aws:iam::615299752206:role/LakeFormation-DataAnalyst-Development"
    },
    {
      "Effect": "Allow",
      "Action": "sts:TagSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-2"
        }
      }
    }
  ]
}
```

**Session Tags** (Required):
- `Environment`: `Development`
- `AccessLevel`: `Analyst`
- `PHIAccess`: `Disabled`

**Target Groups**: `data-analysts-dev`

### 2. LakeFormation-DataEngineer-Dev-PermissionSet

**Purpose**: Provides full development Lake Formation access for data engineers with admin capabilities

**Configuration**:
- **Name**: `LakeFormation-DataEngineer-Dev-PermissionSet`
- **Description**: Lake Formation full access for development data engineers with admin privileges
- **Session Duration**: 8 hours
- **Relay State**: (optional) `https://console.aws.amazon.com/lakeformation/`

**Inline Policy**:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": [
        "arn:aws:iam::615299752206:role/LakeFormation-DataEngineer-Development",
        "arn:aws:iam::615299752206:role/LakeFormation-Admin-Development",
        "arn:aws:iam::615299752206:role/LakeFormation-CatalogCreator-Development"
      ]
    },
    {
      "Effect": "Allow",
      "Action": "sts:TagSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-2"
        }
      }
    }
  ]
}
```

**Session Tags** (Required):
- `Environment`: `Development`
- `AccessLevel`: `Engineer`
- `PHIAccess`: `Enabled`
- `AdminRole`: `true`

**Target Groups**: `data-engineers-dev`

### 3. LakeFormation-DataAnalyst-Prod-PermissionSet

**Purpose**: Provides PHI-enabled read access to production Lake Formation resources for authorized analysts

**Configuration**:
- **Name**: `LakeFormation-DataAnalyst-Prod-PermissionSet`
- **Description**: Lake Formation PHI-enabled access for production data analysts
- **Session Duration**: 4 hours (reduced for production security)
- **Relay State**: (optional) `https://console.aws.amazon.com/lakeformation/`

**Inline Policy**:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": "arn:aws:iam::442042533707:role/LakeFormation-DataAnalyst-Production"
    },
    {
      "Effect": "Allow",
      "Action": "sts:TagSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-2"
        }
      }
    }
  ]
}
```

**Session Tags** (Required):
- `Environment`: `Production`
- `AccessLevel`: `Analyst`
- `PHIAccess`: `Enabled`

**Target Groups**: `data-analysts-phi`

### 4. LakeFormation-DataEngineer-Prod-PermissionSet

**Purpose**: Provides production data engineering access with catalog creation capabilities

**Configuration**:
- **Name**: `LakeFormation-DataEngineer-Prod-PermissionSet`
- **Description**: Lake Formation full access for production data engineers
- **Session Duration**: 4 hours
- **Relay State**: (optional) `https://console.aws.amazon.com/lakeformation/`

**Inline Policy**:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": [
        "arn:aws:iam::442042533707:role/LakeFormation-DataEngineer-Production",
        "arn:aws:iam::442042533707:role/LakeFormation-CatalogCreator-Production"
      ]
    },
    {
      "Effect": "Allow",
      "Action": "sts:TagSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-2"
        }
      }
    }
  ]
}
```

**Session Tags** (Required):
- `Environment`: `Production`
- `AccessLevel`: `Engineer`
- `PHIAccess`: `Enabled`

**Target Groups**: `data-engineers-phi`

### 5. LakeFormation-Admin-Prod-PermissionSet

**Purpose**: Provides administrative access to production Lake Formation with minimal session time

**Configuration**:
- **Name**: `LakeFormation-Admin-Prod-PermissionSet`
- **Description**: Lake Formation administrative access for production
- **Session Duration**: 2 hours (minimal admin session time)
- **Relay State**: (optional) `https://console.aws.amazon.com/lakeformation/`

**Inline Policy**:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": [
        "arn:aws:iam::442042533707:role/LakeFormation-Admin-Production",
        "arn:aws:iam::442042533707:role/LakeFormation-CatalogCreator-Production"
      ]
    },
    {
      "Effect": "Allow",
      "Action": "sts:TagSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-2"
        }
      }
    }
  ]
}
```

**Session Tags** (Required):
- `Environment`: `Production`
- `AccessLevel`: `Admin`
- `PHIAccess`: `Enabled`
- `AdminRole`: `true`

**Target Groups**: `data-lake-admin-prd`

## Setup Instructions

### Step 1: Access AWS Identity Center Console

1. Log in to the AWS Management Console with administrator privileges
2. Navigate to **AWS IAM Identity Center** (successor to AWS SSO)
3. Select **Permission sets** from the left navigation menu

### Step 2: Create Each Permission Set

For each of the 5 permission sets listed above:

1. Click **Create permission set**
2. Select **Custom permission set**
3. Enter the **Name** and **Description** as specified
4. Set the **Session duration** as specified
5. Click **Next**

### Step 3: Configure Inline Policies

1. In the **Inline policy** section, click **Create a custom permissions policy**
2. Copy and paste the JSON policy from the specifications above
3. Click **Next**

### Step 4: Configure Session Tags (Important!)

1. In the **Tags** section, click **Add tag** for each required session tag
2. Enter the **Key** and **Value** exactly as specified in the requirements
3. **Important**: Mark each tag as **Required for session** by checking the appropriate checkbox

### Step 5: Review and Create

1. Review all settings carefully
2. Click **Create**

### Step 6: Assign Permission Sets to Groups

For each permission set created:

1. Go to **AWS accounts** in Identity Center
2. Select the appropriate AWS account (Development: 615299752206, Production: 442042533707)
3. Click **Assign users or groups**
4. Select **Groups** tab
5. Find and select the target group(s) as specified
6. Select the appropriate permission set
7. Click **Submit**

## Validation Scripts

### Script 1: Verify Permission Set Creation

```bash
#!/bin/bash
# verify-permission-sets.sh

INSTANCE_ARN="arn:aws:sso:::instance/ssoins-66849025a110d385"

echo "Checking for Lake Formation permission sets..."

aws sso-admin list-permission-sets --instance-arn "$INSTANCE_ARN" \
  --query 'PermissionSets' --output text | while read permission_set_arn; do
  
  name=$(aws sso-admin describe-permission-set \
    --instance-arn "$INSTANCE_ARN" \
    --permission-set-arn "$permission_set_arn" \
    --query 'PermissionSet.Name' --output text)
  
  if [[ "$name" == *"LakeFormation"* ]]; then
    echo "✓ Found: $name"
    echo "  ARN: $permission_set_arn"
  fi
done
```

### Script 2: Verify Group Assignments

```bash
#!/bin/bash
# verify-group-assignments.sh

INSTANCE_ARN="arn:aws:sso:::instance/ssoins-66849025a110d385"
DEV_ACCOUNT="615299752206"
PROD_ACCOUNT="442042533707"

echo "Checking group assignments for development account ($DEV_ACCOUNT)..."
aws sso-admin list-account-assignments \
  --instance-arn "$INSTANCE_ARN" \
  --account-id "$DEV_ACCOUNT" \
  --query 'AccountAssignments[?PrincipalType==`GROUP`].[PermissionSetArn,PrincipalId]' \
  --output table

echo "Checking group assignments for production account ($PROD_ACCOUNT)..."
aws sso-admin list-account-assignments \
  --instance-arn "$INSTANCE_ARN" \
  --account-id "$PROD_ACCOUNT" \
  --query 'AccountAssignments[?PrincipalType==`GROUP`].[PermissionSetArn,PrincipalId]' \
  --output table
```

### Script 3: Test Role Assumption

```bash
#!/bin/bash
# test-role-assumption.sh

echo "Testing role assumption from Identity Center..."
echo "Please ensure you are logged in via aws sso login first"

# Test development role assumption
echo "Testing LakeFormation-DataAnalyst-Development role..."
aws sts assume-role-with-saml \
  --role-arn "arn:aws:iam::615299752206:role/LakeFormation-DataAnalyst-Development" \
  --principal-arn "arn:aws:sso:::instance/ssoins-66849025a110d385" \
  --saml-assertion "$(echo 'test' | base64)" \
  --dry-run 2>/dev/null && echo "✓ Role exists" || echo "✗ Role not accessible"

# Add similar tests for other roles...
```

## Security Considerations

1. **Session Duration**: Production environments have shorter session durations to minimize exposure
2. **Session Tags**: Required session tags ensure proper audit trails in CloudTrail
3. **Regional Restrictions**: All roles are restricted to us-east-2 region
4. **Least Privilege**: Each permission set only grants access to required roles
5. **PHI Access Control**: PHI access is explicitly tagged and controlled

## Troubleshooting

### Common Issues

1. **Permission Set Creation Fails**
   - Verify IAM roles exist in target accounts
   - Check JSON policy syntax
   - Ensure session tags are properly configured

2. **Role Assumption Fails**
   - Verify trust policy in IAM role includes Identity Center
   - Check that user is member of correct group
   - Ensure permission set is assigned to correct account

3. **Access Denied in Lake Formation**
   - Verify Lake Formation permissions are granted to the role
   - Check that session tags match permission requirements
   - Ensure data lake settings include the role as admin (if applicable)

### Support Commands

```bash
# Check Identity Center instance
aws sso-admin list-instances

# List all permission sets
aws sso-admin list-permission-sets --instance-arn "<INSTANCE_ARN>"

# Check specific permission set details
aws sso-admin describe-permission-set \
  --instance-arn "<INSTANCE_ARN>" \
  --permission-set-arn "<PERMISSION_SET_ARN>"

# List account assignments
aws sso-admin list-account-assignments \
  --instance-arn "<INSTANCE_ARN>" \
  --account-id "<ACCOUNT_ID>"
```

## Compliance and Audit

- All permission sets enforce session tagging for CloudTrail audit trails
- Session durations follow least-privilege principles
- PHI access is explicitly controlled and logged
- Role mappings maintain clear separation between development and production environments
- Administrative access has minimal session duration (2 hours) for enhanced security

## Maintenance

- Review permission sets quarterly for compliance
- Update session durations based on security requirements
- Monitor CloudTrail logs for unusual access patterns
- Validate group memberships regularly
- Test role assumption periodically to ensure functionality