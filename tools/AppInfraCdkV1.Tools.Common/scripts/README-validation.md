# Lake Formation Permission Validation Tools

This directory contains comprehensive validation tools for Lake Formation permissions, PHI access controls, and compliance verification.

## 🎯 Why Run These Scripts?

These validation scripts are critical for ensuring your Lake Formation data lake operates securely and compliantly. They verify that:

1. **HIPAA Compliance**: PHI (Protected Health Information) is properly protected
2. **Access Controls**: Only authorized groups can access sensitive data
3. **Tenant Isolation**: Multi-tenant data is properly segregated
4. **Security Boundaries**: Infrastructure roles (DevOps/CDK) cannot access data
5. **Audit Readiness**: All permissions are properly configured for compliance audits

## ⏰ When to Run These Scripts

### Before Deployment (Pre-Deployment Validation)
**When:** Before any CDK deployment or infrastructure change
**Why:** Catch configuration errors before they reach production
```bash
./validate-lakeformation-permissions.sh Development to-dev-admin
```

### After Deployment (Post-Deployment Verification)
**When:** Immediately after deploying Lake Formation stacks
**Why:** Confirm all resources were created correctly with proper permissions
```bash
./validate-lakeformation-permissions.sh Production to-prd-admin
```

### During Compliance Audits
**When:** Monthly, quarterly, or before regulatory audits
**Why:** Generate compliance reports showing HIPAA and security compliance
```bash
GENERATE_COMPLIANCE_REPORT=true ./validate-lakeformation-permissions.sh Production to-prd-admin
```

### After Security Incidents
**When:** After any suspected security breach or misconfiguration
**Why:** Verify permissions haven't been compromised or incorrectly modified
```bash
./validate-lakeformation-permissions.sh Production to-prd-admin --detailed
```

### When Adding New Groups or Users
**When:** After modifying Identity Center groups or permissions
**Why:** Ensure new groups have correct permissions and don't violate security policies
```bash
./validate-lakeformation-permissions.sh Development to-dev-admin
```

### As Part of CI/CD Pipeline
**When:** On every pull request or deployment
**Why:** Automated validation prevents security misconfigurations from being deployed
```yaml
# In GitHub Actions
- name: Validate Lake Formation Security
  run: ./validate-lakeformation-permissions.sh ${{ env.ENVIRONMENT }} ${{ env.AWS_PROFILE }}
```

### During Development
**When:** While developing new Lake Formation features
**Why:** Test permission changes in real-time during development
```bash
# Run with verbose output for debugging
./validate-lakeformation-permissions.sh Development to-dev-admin --verbose
```

### Scheduled Health Checks
**When:** Daily or weekly via cron job
**Why:** Proactive monitoring catches drift or unauthorized changes
```bash
# Daily at 9 AM
0 9 * * * /path/to/validate-lakeformation-permissions.sh Production to-prd-admin
```

## 🛠️ Available Tools

### 1. Bash Script (Cross-Platform)
**File:** `validate-lakeformation-permissions.sh`

Comprehensive bash script for Lake Formation permission validation with detailed logging and reporting.

```bash
# Make executable
chmod +x validate-lakeformation-permissions.sh

# Development environment
./validate-lakeformation-permissions.sh Development to-dev-admin

# Production environment  
./validate-lakeformation-permissions.sh Production to-prd-admin
```

**Features:**
- ✅ Lake Formation setup validation
- ✅ LF-Tags verification (PHI, TenantID, DataType, etc.)
- ✅ Group permission validation
- ✅ PHI access control testing
- ✅ DevOps access denial verification
- ✅ Tenant isolation validation
- ✅ Database access pattern testing
- ✅ Compliance report generation

### 2. PowerShell Script (Windows)
**File:** `validate-lakeformation-permissions.ps1`

PowerShell version for Windows environments with the same functionality as the bash script.

```powershell
# Development environment
.\validate-lakeformation-permissions.ps1 -Environment "Development" -Profile "to-dev-admin"

# Production environment
.\validate-lakeformation-permissions.ps1 -Environment "Production" -Profile "to-prd-admin"

# Skip certain validations
.\validate-lakeformation-permissions.ps1 -Environment "Development" -Profile "to-dev-admin" -SkipPHIValidation
```

### 3. Future C# Validation Tool
**Status:** Planned for future implementation

Advanced C# tool integration is planned for future versions to provide deeper AWS SDK integration and detailed permission analysis.

## 📋 Validation Categories

### 1. Lake Formation Setup
- Data Lake Settings configuration
- Lake Formation admin roles
- Service role permissions

### 2. LF-Tags Validation
Verifies all required LF-Tags exist with correct values:

| LF-Tag | Purpose | Values |
|--------|---------|--------|
| **Environment** | Environment isolation | Development, Production |
| **PHI** | HIPAA compliance | true, false |
| **TenantID** | Multi-tenant isolation | tenant-a, tenant-b, tenant-c, shared, multi-tenant |
| **DataType** | Data classification | clinical, research, operational, administrative, reference |
| **Sensitivity** | Access control | public, internal, confidential, restricted |
| **SourceSystem** | Data lineage | epic, cerner, allscripts, healthlake, external-api |

### 3. Group Permissions
Validates permissions for Identity Center groups:

**Development Environment:**
- `data-analysts-dev`: Non-PHI data access
- `data-engineers-dev`: Full development access

**Production Environment:**
- `data-analysts-phi`: PHI data analysis access
- `data-engineers-phi`: Full PHI data engineering access

### 4. PHI Access Controls
- Validates PHI=true/false LF-Tag conditions
- Ensures proper PHI exclusion in development
- Verifies controlled PHI access in production
- Tests LF-Tag policy enforcement

### 5. DevOps Access Denial
Ensures DevOps/infrastructure roles cannot access data:
- GitHub Actions deployment roles
- CDK execution roles
- Administrative roles

### 6. Tenant Isolation
- TenantID LF-Tag configuration
- Tenant-based query filtering capabilities
- Cross-tenant access prevention

### 7. Database Access Patterns
- Glue catalog database validation
- Expected database presence verification
- Access pattern compliance

## 🚨 Environment-Specific Validations

### Development Environment
- **Account:** 615299752206
- **Groups:** data-analysts-dev, data-engineers-dev
- **PHI Policy:** Excluded (PHI=false only)
- **Access Level:** Broader for development/testing

### Production Environment  
- **Account:** 442042533707
- **Groups:** data-analysts-phi, data-engineers-phi
- **PHI Policy:** Controlled access (PHI=true allowed for authorized groups)
- **Access Level:** Strict HIPAA-compliant controls

## 📊 Report Generation

All tools generate detailed compliance reports in JSON format:

```json
{
  "validation_report": {
    "timestamp": "2025-01-15T10:30:00Z",
    "environment": "Production",
    "account_id": "442042533707",
    "summary": {
      "total_tests": 25,
      "passed_tests": 23,
      "failed_tests": 2,
      "success_rate": "92%"
    },
    "compliance_status": {
      "hipaa_ready": false,
      "multi_tenant_ready": true,
      "recommendations": [
        "Review PHI access controls",
        "Ensure DevOps roles have no data access"
      ]
    }
  }
}
```

## 🔧 Configuration

### Environment Variables
- `VALIDATE_PHI_ACCESS=true` - Enable PHI validation
- `VALIDATE_DEVOPS_DENIAL=true` - Enable DevOps access denial tests
- `GENERATE_COMPLIANCE_REPORT=true` - Generate detailed reports

### Prerequisites
1. **AWS CLI** installed and configured
2. **jq** (for bash script JSON parsing)
3. **Valid AWS credentials** with Lake Formation permissions
4. **SSO login** completed for the target profile

```bash
# Login before validation
aws sso login --profile to-dev-admin
aws sso login --profile to-prd-admin
```

## 🎯 Usage Scenarios

### 1. Pre-Deployment Validation
```bash
# Validate before deploying Lake Formation changes
./validate-lakeformation-permissions.sh Development to-dev-admin
```

### 2. Compliance Auditing
```bash
# Generate compliance report for auditing
GENERATE_COMPLIANCE_REPORT=true ./validate-lakeformation-permissions.sh Production to-prd-admin
```

### 3. CI/CD Integration
```yaml
# GitHub Actions example
- name: Validate Lake Formation Permissions
  run: |
    ./tools/AppInfraCdkV1.Tools.Common/scripts/validate-lakeformation-permissions.sh \
      Production to-prd-admin
```

### 4. Development Testing
```bash
# Test during development with detailed output
./validate-lakeformation-permissions.sh Development to-dev-admin 2>&1 | tee validation.log
```

## 🚀 Integration with CDK

The validation tools integrate with the Lake Formation CDK infrastructure:

1. **Configuration Loading**: Uses the same `lakeformation-config.json`
2. **Environment Detection**: Matches CDK environment naming
3. **Resource Validation**: Validates deployed CDK resources
4. **Group Mapping**: Uses CDK group configuration

## 🔒 Security Best Practices

1. **Least Privilege**: Validation tools use read-only permissions
2. **Audit Logging**: All validation activities are logged
3. **Secure Credentials**: Uses AWS SSO/IAM roles, no hardcoded keys
4. **Environment Isolation**: Separate validation for dev/prod
5. **PHI Protection**: Validates but doesn't expose PHI data

## 📈 Success Criteria

### Development Environment
- ✅ All LF-Tags created with correct values
- ✅ Groups have appropriate development permissions
- ✅ PHI access properly excluded
- ✅ DevOps roles denied data access
- ✅ Databases accessible for development

### Production Environment
- ✅ HIPAA-compliant PHI access controls
- ✅ Strict group-based permissions
- ✅ Multi-tenant isolation working
- ✅ Audit logging enabled
- ✅ Zero DevOps data access
- ✅ Compliance report shows 100% pass rate

## 🔄 Continuous Validation

Set up regular validation runs:

```bash
# Daily validation cron job
0 9 * * * /path/to/validate-lakeformation-permissions.sh Production to-prd-admin >> /var/log/lf-validation.log 2>&1
```

## 📞 Support

For issues with validation tools:
1. Check AWS credentials and permissions
2. Verify Lake Formation is properly deployed
3. Review validation logs for specific errors
4. Ensure all prerequisites are installed

## 🎉 Quick Start

```bash
# 1. Ensure AWS CLI is configured
aws sso login --profile to-dev-admin

# 2. Run validation
./validate-lakeformation-permissions.sh Development to-dev-admin

# 3. Review results
echo "Success Rate: $(grep 'Success Rate' /tmp/lakeformation-validation-*.log | tail -1)"

# 4. Check compliance
cat /tmp/lakeformation-validation-report-*.json | jq '.validation_report.compliance_status'
```