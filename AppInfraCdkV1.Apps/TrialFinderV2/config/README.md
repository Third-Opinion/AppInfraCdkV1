# TrialFinderV2 JSON Configuration System

This directory contains simplified JSON configuration files that define only the ECS task environment variables for the TrialFinderV2 application stack. Each environment has its own configuration file.

## Configuration Files

### Environment-Specific Configuration Files

- **`development.json`** - Environment variables for development environment
- **`integration.json`** - Environment variables for integration environment  
- **`staging.json`** - Environment variables for staging environment
- **`production.json`** - Environment variables for production environment

Each file contains only the ECS task definition container environment variables in the following format:

```json
{
  "ecsConfiguration": {
    "taskDefinition": {
      "containerDefinitions": [
        {
          "name": "trial-finder-v2",
          "environment": [
            {
              "name": "ENVIRONMENT",
              "value": "${ENVIRONMENT}"
            },
            {
              "name": "ACCOUNT_TYPE", 
              "value": "${ACCOUNT_TYPE}"
            },
            {
              "name": "APP_VERSION",
              "value": "${APP_VERSION}"
            },
            {
              "name": "PORT",
              "value": "8080"
            },
            {
              "name": "HEALTH_CHECK_PATH",
              "value": "/health"
            }
          ]
        }
      ]
    }
  }
}
```

## Variable Substitution

The configuration files support variable substitution using the format `${VARIABLE_NAME}`. Common variables include:

- `${ENVIRONMENT}` - Environment name (Development, Production, etc.)
- `${ACCOUNT_TYPE}` - Account type (NonProduction, Production)
- `${APP_VERSION}` - Application version
- `${AWS_REGION}` - AWS region code
- `${SERVICE_NAME}` - ECS service name from naming convention
- `${TASK_DEFINITION_FAMILY}` - Task definition family name
- `${LOG_GROUP_NAME}` - CloudWatch log group name
- `${ALB_SECURITY_GROUP_ID}` - ALB security group ID (for ECS rules)
- `${EXECUTION_ROLE_ARN}` - ECS execution role ARN
- `${TASK_ROLE_ARN}` - ECS task role ARN
- `${CERTIFICATE_ARN}` - SSL certificate ARN for HTTPS

## Usage in CDK Code

```csharp
// Load configuration
var configLoader = new ConfigurationLoader();
var albConfig = configLoader.LoadAlbConfig();
var ecsConfig = configLoader.LoadEcsConfig();
var envConfig = configLoader.LoadEnvironmentConfig(context.Environment.Name);

// Substitute variables
albConfig = configLoader.SubstituteVariables(albConfig, context);
ecsConfig = configLoader.SubstituteVariables(ecsConfig, context);

// Use configuration in CDK constructs
var loadBalancer = new ApplicationLoadBalancer(this, "ALB", new ApplicationLoadBalancerProps
{
    Vpc = vpc,
    InternetFacing = albConfig.LoadBalancer.InternetFacing,
    // ... other properties from config
});
```

## Configuration Validation

The `ConfigurationLoader` class provides:
- JSON schema validation
- Type safety through C# classes  
- Variable substitution with context awareness
- Environment-specific configuration loading
- Error handling for missing files or invalid formats

## Environment-Specific Behavior

### Development Environment
- Single task instance
- 7-day log retention
- No auto-scaling
- Minimal monitoring
- Relaxed security for development

### Integration Environment  
- Single task instance with auto-scaling
- 14-day log retention
- Enhanced monitoring
- Production-like configuration for testing

### Staging Environment
- Multi-task deployment
- 30-day log retention
- Full monitoring and X-Ray tracing
- Production account with deletion protection

### Production Environment
- High availability (3+ tasks)
- 90-day log retention  
- Full monitoring, tracing, and alerting
- Maximum security and deletion protection

## Security Configuration

Security groups are defined in JSON with:
- **ALB Security Group**: Allows HTTPS (443) and HTTP (80) from internet
- **ECS Security Group**: Allows traffic from ALB on container port 8080
- **Loopback Rules**: Self-referencing rules for health checks

## Health Check Configuration

### Configurable Health Check Paths

The health check path is now configurable and follows this priority order:

1. **Custom Health Check Command**: If a custom `healthCheck.command` is specified in the configuration
2. **Environment Variable**: `HEALTH_CHECK_PATH` environment variable in the container
3. **Default**: `/health` (fallback)

#### Examples:

**Using Environment Variable:**
```json
{
  "name": "api-service",
  "image": "api:latest",
  "essential": true,
  "environment": [
    {
      "name": "HEALTH_CHECK_PATH",
      "value": "/api/health"
    }
  ]
}
```

**Using Custom Health Check Command:**
```json
{
  "name": "custom-service",
  "image": "service:latest",
  "essential": true,
  "healthCheck": {
    "command": [
      "CMD-SHELL",
      "curl -f http://localhost:8080/status || exit 1"
    ],
    "interval": 30,
    "timeout": 5,
    "retries": 3
  }
}
```

**Default Behavior:**
```json
{
  "name": "web-app",
  "image": "app:latest",
  "essential": true
  // Uses /health by default
}
```

### Health Check Parameters

| Parameter | Description | Default | Recommended for Cron Jobs |
|-----------|-------------|---------|---------------------------|
| `command` | Health check command | `curl -f http://localhost:8080/health` | N/A (disabled) |
| `interval` | Time between health checks (seconds) | 30 | N/A (disabled) |
| `timeout` | Health check timeout (seconds) | 5 | N/A (disabled) |
| `retries` | Number of consecutive failures before unhealthy | 3 | N/A (disabled) |
| `startPeriod` | Grace period before health checks start (seconds) | 60 | N/A (disabled) |

## Access Logging

ALB access logs are configured with:
- S3 bucket for log storage
- Environment-specific retention policies
- Automatic cleanup based on retention settings
- Prefix-based organization for easy querying

## Monitoring and Observability

Environment-specific monitoring configuration:
- **Container Insights**: Enabled in all environments
- **Detailed Monitoring**: Enabled in integration+ environments
- **X-Ray Tracing**: Enabled in staging and production
- **Custom Metrics**: Environment-appropriate detail levels

## Extending Configuration

To add new configuration parameters:

1. Update the appropriate JSON configuration file
2. Add corresponding properties to the C# configuration classes
3. Update the `ConfigurationLoader` variable substitution if needed
4. Use the new configuration in your CDK stack code

## Best Practices

1. **Version Control**: All configuration files are version controlled
2. **Environment Parity**: Maintain consistency between environments
3. **Security**: Never store secrets in configuration files
4. **Validation**: Use the ConfigurationLoader for type safety
5. **Documentation**: Document any custom configuration parameters
6. **Testing**: Test configuration changes in development first