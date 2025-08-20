# PublicThirdOpinion Deployment Guide

## Two-Stage Deployment Process

This project supports two deployment strategies:

### Option 1: Two-Stage Deployment (Recommended for first-time setup)

This approach separates certificate creation from the main infrastructure, allowing time for manual DNS validation.

#### Stage 1: Deploy Certificate Stack

```bash
# Development
AWS_PROFILE=to-dev-admin PTO_STACK_TYPE=certificate npx cdk deploy

# Production
AWS_PROFILE=to-prd-admin CDK_ENVIRONMENT=Production PTO_STACK_TYPE=certificate npx cdk deploy
```

This creates:
- Route53 Hosted Zone
- ACM Certificate (in us-east-1 for CloudFront)
- Outputs the nameservers for DNS delegation

#### Manual Step: Configure DNS

1. Note the nameservers from the stack output
2. Add NS records to the parent domain (thirdopinion.io)
3. Wait for certificate validation (check AWS ACM console)

#### Stage 2: Deploy Main Stack

Once the certificate is validated:

```bash
# Development
AWS_PROFILE=to-dev-admin PTO_USE_CERT_STACK=true npx cdk deploy

# Production
AWS_PROFILE=to-prd-admin CDK_ENVIRONMENT=Production PTO_USE_CERT_STACK=true npx cdk deploy
```

This creates:
- S3 Bucket for static website hosting
- CloudFront Distribution (using the validated certificate)
- Route53 A record
- JWK Lambda function

### Option 2: Single-Stage Deployment

If DNS delegation is already configured or you want CDK to handle everything:

```bash
# Development
AWS_PROFILE=to-dev-admin npx cdk deploy

# Production
AWS_PROFILE=to-prd-admin CDK_ENVIRONMENT=Production npx cdk deploy
```

**Note**: This will pause at certificate validation until DNS is configured.

## Stack Names

- Certificate Stack: `{env}-pto-cert-{region}` (e.g., `dev-pto-cert-ue2`)
- Main Stack: `{env}-pto-public-{region}` (e.g., `dev-pto-public-ue2`)

## Verify Deployment

### Check Certificate Status
```bash
# List certificates
AWS_PROFILE=to-dev-admin aws acm list-certificates --region us-east-1

# Check validation status
AWS_PROFILE=to-dev-admin aws acm describe-certificate \
  --certificate-arn <arn> \
  --region us-east-1 \
  --query 'Certificate.Status'
```

### Check Stack Status
```bash
# Certificate stack
AWS_PROFILE=to-dev-admin aws cloudformation describe-stacks \
  --stack-name dev-pto-cert-ue2 \
  --query 'Stacks[0].StackStatus'

# Main stack
AWS_PROFILE=to-dev-admin aws cloudformation describe-stacks \
  --stack-name dev-pto-public-ue2 \
  --query 'Stacks[0].StackStatus'
```

## Cleanup

To remove the stacks:

```bash
# Remove main stack first
AWS_PROFILE=to-dev-admin cdk destroy dev-pto-public-ue2

# Then remove certificate stack
AWS_PROFILE=to-dev-admin cdk destroy dev-pto-cert-ue2
```

## Troubleshooting

### Certificate Not Validating
- Ensure NS records are added to parent domain
- Check Route53 hosted zone for validation CNAME record
- DNS propagation can take up to 48 hours

### Stack Creation Failed
- Check CloudFormation events for specific error
- Ensure IAM permissions are sufficient
- Verify AWS account limits (CloudFront distributions, certificates)

### Cross-Stack References
- Certificate stack must be deployed and stable before main stack
- Do not delete certificate stack while main stack exists
- Exports are named: `{Environment}-PublicWebsite-CertificateArn` and `{Environment}-PublicWebsite-HostedZoneId`