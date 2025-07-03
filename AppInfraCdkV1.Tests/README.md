# Infrastructure Tests

## Overview
Comprehensive test suite for AWS CDK C# infrastructure with naming convention enforcement.

## Test Categories

### 🧪 Unit Tests (`/Unit/`)
- **Naming Convention Tests**: Validate resource naming patterns
- **Stack Tests**: Test individual CDK stacks in isolation
- **Configuration Tests**: Test deployment context and configurations
- **Extension Tests**: Test helper methods and extensions

### 🔗 Integration Tests (`/Integration/`)
- **Full Stack Tests**: Test complete infrastructure deployment
- **Cross-Stack Tests**: Test interactions between multiple stacks
- **End-to-End Tests**: Test entire deployment pipelines

## Running Tests

### All Tests
```bash
cd Infrastructure
dotnet test tests/Infrastructure.Tests/
```

### Unit Tests Only
```bash
dotnet test --filter "Category!=Integration&Category!=Performance"
```

### Integration Tests Only
```bash
dotnet test --filter "Category=Integration"
```

### Performance Tests Only
```bash
dotnet test --filter "Category=Performance"
```

### With Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Utilities

### TestHelpers
Provides utilities for creating test contexts:
```csharp
var context = TestHelpers.CreateTestContext("Development", "TrialFinderV2", "us-east-1");
```

### Naming Assertions
Specialized assertions for naming conventions:
```csharp
TestHelpers.AssertResourceNameFollowsConvention(
    resourceName, "d", "tfv2", "ecs", "ue1", "main");
```

## CI/CD Integration

Tests run automatically on:
- Pull requests to main/develop
- Pushes to main/develop
- Manual workflow dispatch

### Test Matrix
- **Environments**: Development, Production
- **Applications**: TrialFinderV2
- **Regions**: us-east-1, us-west-2

## Coverage Requirements
- **Minimum Coverage**: 80%
- **Critical Paths**: 95% (naming conventions, stack creation)
- **Reports**: Generated on every PR

## Best Practices

### Writing Tests
1. **Use descriptive names**: Test names should explain what's being tested
2. **Follow AAA pattern**: Arrange, Act, Assert
3. **Test edge cases**: Invalid inputs, boundary conditions
4. **Use TestHelpers**: Leverage utilities for common setups

### CDK Testing
1. **Test resource creation**: Verify correct resources are created
2. **Test resource properties**: Check configurations match expectations
3. **Test naming conventions**: Ensure all names follow standards
4. **Test different environments**: Verify dev/prod differences

### Example Test
```csharp
[Fact]
public void EcsCluster_WithDevelopmentContext_ReturnsCorrectName()
{
    // Arrange
    var context = TestHelpers.CreateTestContext("Development", "TrialFinderV2", "us-east-1");

    // Act
    var result = context.Namer.EcsCluster();

    // Assert
    result.Should().Be("d-tfv2-ecs-ue1-main");
}
```

## Troubleshooting

### Common Issues
1. **CDK synthesis errors**: Check that all dependencies are properly referenced
2. **Naming validation failures**: Ensure test contexts use valid environment/app/region combinations
3. **AWS credential errors**: Integration tests may need AWS configuration

### Debug Tests
```bash
dotnet test --logger "console;verbosity=detailed"
```