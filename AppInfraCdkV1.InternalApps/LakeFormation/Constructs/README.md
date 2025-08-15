# Lake Formation CDK Constructs

This directory contains comprehensive CDK constructs for AWS Lake Formation permission management, implementing multi-tenant data access control with PHI protection and comprehensive data classification.

## 🚧 Current Status: CDK API Compatibility Issues

**Note**: The advanced CDK constructs are temporarily excluded from compilation due to AWS CDK API version compatibility issues. The property names and structure for Lake Formation CloudFormation resources have changed between CDK versions.

### Working Implementation
- ✅ **LakeFormationSetupStack**: Updated with comprehensive LF-Tags including tenant ID support
- ✅ **Core Lake Formation Tags**: PHI, TenantID, DataType, Sensitivity, Environment, SourceSystem

### Excluded Constructs (CDK API Compatibility Issues)
- 🔧 **LakeFormationTagsConstruct.cs**: Advanced LF-Tag management
- 🔧 **LakeFormationPermissionsConstruct.cs**: Role-based PHI access control  
- 🔧 **LakeFormationValidationConstruct.cs**: PHI access control testing
- 🔧 **SampleTablesConstruct.cs**: Test tables with LF-Tags
- 🔧 **TenantManagementConstruct.cs**: Tenant lifecycle management
- 🔧 **EnvironmentConfigConstruct.cs**: Environment-specific configurations

## Architecture Overview

### Multi-Layered Security Model
1. **Role-based PHI Access**: Lake Formation level access control
2. **Tenant Filtering**: LF-Tags enable query-based tenant isolation
3. **Physical Partitioning**: S3 partitioning for additional isolation

### LF-Tags Classification System
- **PHI**: `true`/`false` for HIPAA compliance
- **TenantID**: `tenant-a`, `tenant-b`, `tenant-c`, `shared`, `multi-tenant`
- **DataType**: `clinical`, `research`, `operational`, `administrative`, `reference`
- **Sensitivity**: `public`, `internal`, `confidential`, `restricted`
- **Environment**: `development`, `staging`, `production`
- **SourceSystem**: `epic`, `cerner`, `allscripts`, `healthlake`, `external-api`

## Current Working Implementation

The `LakeFormationSetupStack.cs` includes:

```csharp
private void CreateLakeFormationTags()
{
    var tags = new Dictionary<string, string[]>
    {
        ["Environment"] = new[] { _config.Environment },
        ["DataClassification"] = new[] { "Public", "Internal", "Confidential" },
        ["PHI"] = new[] { "true", "false" },
        ["TenantID"] = new[] { "tenant-a", "tenant-b", "tenant-c", "shared", "multi-tenant" },
        ["DataType"] = new[] { "clinical", "research", "operational", "administrative", "reference" },
        ["Sensitivity"] = new[] { "public", "internal", "confidential", "restricted" },
        ["SourceSystem"] = new[] { "epic", "cerner", "allscripts", "healthlake", "external-api" }
    };
    
    foreach (var tag in tags)
    {
        new Amazon.CDK.AWS.LakeFormation.CfnTag(this, $"LFTag-{tag.Key}", new CfnTagProps
        {
            TagKey = tag.Key,
            TagValues = tag.Value
        });
    }
}
```

## Next Steps

### 1. Fix CDK API Compatibility Issues
- Research current CDK Lake Formation API documentation
- Update property names in constructs:
  - `LfTag` → `LFTag` 
  - `LfTagKeyResourceProperty` → `LFTagKeyResourceProperty`
  - `LfTagPairProperty` → `LFTagPairProperty`
  - `TableResource` → `Table`
  - `DatabaseResource` → `Database`
  - Fix `Stack.Of(this)` references

### 2. Re-enable Advanced Constructs
Remove exclusions from `AppInfraCdkV1.Apps.csproj`:
```xml
<!-- Remove these once API issues are fixed -->
<Compile Remove="LakeFormation/Constructs/LakeFormationTagsConstruct.cs" />
<Compile Remove="LakeFormation/Constructs/LakeFormationPermissionsConstruct.cs" />
<!-- ... other excluded files ... -->
```

### 3. Integration Testing
- Deploy Lake Formation stack with tags
- Test PHI access controls
- Validate tenant filtering capabilities
- Verify role-based permissions

### 4. Production Deployment
- Update environment configurations
- Deploy to development first
- Validate security boundaries
- Deploy to staging/production

## Features Implemented

### LakeFormationTagsConstruct
- 6 comprehensive tag types for data classification
- Helper methods for tag associations
- Support for custom tag values

### LakeFormationPermissionsConstruct
- PHI-authorized vs non-PHI role separation
- Environment-specific permission patterns
- Tenant-based filtering support
- Administrative role management

### SampleTablesConstruct
- PHI/non-PHI test tables per tenant
- Cross-tenant reference tables
- Comprehensive LF-Tag classification
- Glue table integration

### EnvironmentConfigConstruct
- Development: Broader access, 72-hour permissions
- Staging: Restricted access, no PHI, 48-hour permissions  
- Production: Strict controls, 24-hour permissions
- SSM parameter storage

### TenantManagementConstruct
- Dynamic tenant creation/management
- Tenant registry in SSM Parameter Store
- Lambda-based tenant operations
- Tenant isolation validation

### LakeFormationValidationConstruct
- Lambda functions for permission validation
- Automated PHI access boundary testing
- Tenant filtering capability validation
- Custom resources for deployment-time validation

## Benefits

1. **Infrastructure as Code**: Version-controlled, repeatable deployments
2. **Comprehensive Security**: Multi-layered data protection
3. **HIPAA Compliance**: PHI classification and access controls
4. **Multi-Tenant Support**: Tenant isolation and filtering
5. **Environment-Specific**: Development, staging, production patterns
6. **Operational Excellence**: Automated validation and monitoring

## Required Dependencies
- AWS CDK v2.201.0+
- .NET 8.0
- AWS Lake Formation permissions
- Appropriate IAM roles for Lake Formation administration