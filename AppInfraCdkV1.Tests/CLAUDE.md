# Unit Test Preferences

## Testing Framework
- Use **XUnit** for all unit tests
- Use **Shouldly** for assertions (prefer over FluentAssertions)

## Test Naming Conventions
- Use **camelCase** for test method names
- Format: `MethodName_Condition_ExpectedResult`
- Example: `GetCommonTags_WithValidContext_ReturnsAllRequiredTags`

## Test Data
- Prefer **fixed IDs and GUIDs** over random generation for predictable, repeatable tests
- Use consistent test data across similar test scenarios

## Mocking
- Use **strict mocks** to ensure all interactions are explicitly defined
- Verify all mock interactions are intentional and expected

## Code Style
- Use `var` **only when the type is evident** from the right-hand side
- Be explicit with type declarations when the type is not immediately clear

## Commands
- Run tests: `dotnet test`
- Run tests with coverage: `dotnet test --collect:"XPlat Code Coverage"`