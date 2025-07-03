# AWS Infrastructure Integration Testing Plan

## Overview

This plan outlines the approach for implementing integration tests that validate deployed AWS infrastructure matches the CDK code definitions. The tests will ensure infrastructure consistency, catch drift, and validate security configurations.

## Goals

1. **Validate Resource Existence**: Ensure all defined resources are actually deployed
2. **Verify Configuration**: Check that resource properties match code specifications
3. **Detect Drift**: Identify when deployed infrastructure differs from code
4. **Security Validation**: Confirm security groups, IAM roles, and encryption settings
5. **Cross-Resource Validation**: Test resource interactions and dependencies

## Architecture

### Test Structure

```
AppInfraCdkV1.Tests/
├── Unit/                          # Existing unit tests
├── Integration/                   # New integration tests
│   ├── Base/
│   │   ├── IntegrationTestBase.cs
│   │   ├── AwsClientFactory.cs
│   │   └── ResourceValidator.cs
│   ├── Stacks/
│   │   ├── WebApplicationStackTests.cs
│   │   └── TrialFinderV2StackTests.cs
│   ├── Resources/
│   │   ├── S3BucketValidationTests.cs
│   │   ├── EcsClusterValidationTests.cs
│   │   ├── RdsInstanceValidationTests.cs
│   │   ├── VpcValidationTests.cs
│   │   └── SecurityGroupValidationTests.cs
│   └── Scenarios/
│       ├── EndToEndDeploymentTests.cs
│       └── CrossStackValidationTests.cs
```

### Test Categories

1. **Pre-Deployment Tests**
   - Validate CDK synthesis
   - Check CloudFormation template generation
   - Verify resource naming conventions

2. **Post-Deployment Tests**
   - Validate resource existence
   - Check resource configurations
   - Verify security settings

3. **Drift Detection Tests**
   - Compare deployed state with expected state
   - Identify manual changes
   - Validate tag compliance

## Implementation Phases

### Phase 1: Foundation (Week 1-2)

1. **Set up test infrastructure**
   ```csharp
   // Add NuGet packages
   - AWSSDK.Core
   - AWSSDK.S3
   - AWSSDK.EC2
   - AWSSDK.ECS
   - AWSSDK.RDS
   - AWSSDK.ElasticLoadBalancingV2
   - AWSSDK.CloudFormation
   ```

2. **Create base test classes**
   ```csharp
   public abstract class IntegrationTestBase
   {
       protected IAmazonS3 S3Client { get; }
       protected IAmazonECS EcsClient { get; }
       protected IAmazonRDS RdsClient { get; }
       protected DeploymentContext Context { get; }
       
       protected async Task<bool> ResourceExistsAsync(string resourceType, string resourceId);
       protected async Task ValidateTagsAsync(string resourceArn, Dictionary<string, string> expectedTags);
   }
   ```

3. **Implement AWS client factory**
   ```csharp
   public class AwsClientFactory
   {
       public T CreateClient<T>(RegionEndpoint region) where T : IAmazonService;
       public async Task<Credentials> AssumeRoleAsync(string roleArn);
   }
   ```

### Phase 2: Core Resource Validation (Week 3-4)

1. **S3 Bucket Validation**
   ```csharp
   [Fact]
   [Trait("Category", "Integration")]
   public async Task S3Bucket_ShouldExist_WithCorrectConfiguration()
   {
       // Arrange
       var bucketName = Context.Namer.S3Bucket("documents");
       
       // Act
       var bucket = await S3Client.GetBucketVersioningAsync(bucketName);
       
       // Assert
       bucket.Status.ShouldBe(VersioningStatus.Enabled);
   }
   ```

2. **VPC and Security Group Validation**
   ```csharp
   [Fact]
   [Trait("Category", "Integration")]
   public async Task SecurityGroup_ShouldHaveCorrectRules()
   {
       // Validate ingress/egress rules match expectations
   }
   ```

3. **ECS Cluster and Service Validation**
   ```csharp
   [Fact]
   [Trait("Category", "Integration")]
   public async Task EcsService_ShouldBeRunning_WithCorrectTaskCount()
   {
       // Validate service is running with expected configuration
   }
   ```

### Phase 3: Advanced Validation (Week 5-6)

1. **CloudFormation Stack Validation**
   ```csharp
   public class StackValidator
   {
       public async Task<StackValidationResult> ValidateStackAsync(string stackName)
       {
           // Compare deployed stack with synthesized template
           // Identify drift
           // Return detailed comparison
       }
   }
   ```

2. **Cross-Resource Validation**
   ```csharp
   [Fact]
   [Trait("Category", "Integration")]
   public async Task EcsTask_ShouldHaveAccessToS3Bucket()
   {
       // Validate IAM role permissions
       // Test actual connectivity
   }
   ```

3. **Drift Detection**
   ```csharp
   public class DriftDetector
   {
       public async Task<DriftReport> DetectDriftAsync(DeploymentContext context)
       {
           // Compare all resources
           // Generate drift report
           // Flag manual changes
       }
   }
   ```

### Phase 4: CI/CD Integration (Week 7-8)

1. **GitHub Actions Workflow**
   ```yaml
   name: Integration Tests
   
   on:
     schedule:
       - cron: '0 */4 * * *'  # Every 4 hours
     workflow_dispatch:
   
   jobs:
     integration-test:
       runs-on: ubuntu-latest
       steps:
         - name: Run Integration Tests
           run: dotnet test --filter Category=Integration
         
         - name: Generate Drift Report
           run: dotnet run --project DriftDetector
         
         - name: Notify on Drift
           if: failure()
           uses: slack-action@v1
   ```

2. **Test Execution Strategies**
   - Run subset on each PR (quick validation)
   - Full suite on merge to main
   - Scheduled drift detection
   - Manual trigger for debugging

## Key Implementation Details

### 1. Resource Tagging Strategy
```csharp
public class TagValidator
{
    private readonly Dictionary<string, string> _requiredTags = new()
    {
        ["Environment"] = context.Environment.Name,
        ["Application"] = context.Application.Name,
        ["ManagedBy"] = "CDK",
        ["DeployedBy"] = context.DeployedBy
    };
    
    public async Task ValidateResourceTagsAsync(string resourceArn);
}
```

### 2. Retry Logic for Eventual Consistency
```csharp
public static class AwsRetry
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        int delayMs = 1000)
    {
        // Exponential backoff for AWS API calls
    }
}
```

### 3. Test Data Management
```csharp
public class TestDataBuilder
{
    public DeploymentContext CreateTestContext(string environment)
    {
        // Create consistent test contexts
        // Use fixed IDs as per CLAUDE.md preferences
    }
}
```

### 4. Assertion Helpers
```csharp
public static class AwsAssertions
{
    public static async Task ShouldExistAsync(this IAmazonS3 client, string bucketName);
    public static async Task ShouldHaveTagAsync(this Tag tag, string key, string value);
    public static async Task ShouldBeRunningAsync(this EcsService service);
}
```

## Configuration

### TestSettings.json Enhancement
```json
{
  "IntegrationTests": {
    "Enabled": true,
    "TargetEnvironment": "Development",
    "AwsProfile": "integration-test",
    "Regions": ["us-east-2"],
    "ResourceValidation": {
      "ValidateTags": true,
      "ValidateEncryption": true,
      "ValidateBackups": true
    },
    "DriftDetection": {
      "Enabled": true,
      "IgnoreResources": [],
      "NotificationWebhook": "https://hooks.slack.com/..."
    }
  }
}
```

## Success Metrics

1. **Coverage**: 100% of deployed resources have validation tests
2. **Execution Time**: Full suite runs in < 10 minutes
3. **Drift Detection**: Catches 100% of manual changes
4. **False Positives**: < 1% false positive rate
5. **Maintainability**: New resources automatically get base validation

## Tools and Dependencies

### Required NuGet Packages
```xml
<ItemGroup>
  <!-- AWS SDK -->
  <PackageReference Include="AWSSDK.Core" Version="3.7.*" />
  <PackageReference Include="AWSSDK.S3" Version="3.7.*" />
  <PackageReference Include="AWSSDK.EC2" Version="3.7.*" />
  <PackageReference Include="AWSSDK.ECS" Version="3.7.*" />
  <PackageReference Include="AWSSDK.RDS" Version="3.7.*" />
  <PackageReference Include="AWSSDK.CloudFormation" Version="3.7.*" />
  <PackageReference Include="AWSSDK.ElasticLoadBalancingV2" Version="3.7.*" />
  <PackageReference Include="AWSSDK.SecurityToken" Version="3.7.*" />
  
  <!-- Testing -->
  <PackageReference Include="Polly" Version="8.2.*" />
  <PackageReference Include="FluentAssertions.Json" Version="6.1.*" />
</ItemGroup>
```

### External Tools
- AWS CLI for local testing
- CloudFormation Drift Detection API
- AWS Config for compliance validation (optional)

## Example Test Implementation

```csharp
[Collection("Integration")]
[Trait("Category", "Integration")]
public class TrialFinderV2IntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task TrialFinderV2_CompleteDeployment_ShouldMatchSpecification()
    {
        // Arrange
        var context = CreateDeploymentContext("Development");
        var validator = new StackValidator(context);
        
        // Act
        var validationResult = await validator.ValidateCompleteStackAsync();
        
        // Assert
        validationResult.IsValid.ShouldBeTrue();
        validationResult.Errors.ShouldBeEmpty();
        
        // Validate specific resources
        await ValidateS3BucketsAsync(context);
        await ValidateEcsResourcesAsync(context);
        await ValidateNetworkingAsync(context);
        await ValidateSecurityAsync(context);
    }
    
    private async Task ValidateS3BucketsAsync(DeploymentContext context)
    {
        var documentsBucket = context.Namer.S3Bucket("documents");
        
        // Verify bucket exists
        var bucketExists = await S3Client.DoesS3BucketExistAsync(documentsBucket);
        bucketExists.ShouldBeTrue($"Bucket {documentsBucket} should exist");
        
        // Verify versioning
        var versioning = await S3Client.GetBucketVersioningAsync(documentsBucket);
        versioning.Status.ShouldBe(VersionStatus.Enabled);
        
        // Verify encryption
        var encryption = await S3Client.GetBucketEncryptionAsync(documentsBucket);
        encryption.ServerSideEncryptionConfiguration.Rules.ShouldNotBeEmpty();
        
        // Verify tags
        var tags = await S3Client.GetBucketTaggingAsync(documentsBucket);
        await ValidateResourceTagsAsync(tags.TagSet, context);
    }
}
```

## Next Steps

1. Review and approve this plan
2. Create the Integration folder structure
3. Implement Phase 1 foundation classes
4. Begin writing tests for each resource type
5. Integrate with CI/CD pipeline
6. Set up monitoring and alerting for drift detection

This plan provides a comprehensive approach to validating that AWS infrastructure matches the CDK code, with automated testing, drift detection, and continuous validation.