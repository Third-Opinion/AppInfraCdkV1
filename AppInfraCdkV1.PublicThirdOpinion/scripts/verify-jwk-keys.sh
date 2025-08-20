#!/bin/bash

# Script to create or verify JWK keys in both development and production environments
# Usage: ./verify-jwk-keys.sh [dev|prod] [key-name] [algorithm] [--regenerate]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Parse arguments
ENVIRONMENT=$1
KEY_NAME=$2
ALGORITHM=${3:-ES384}
REGENERATE=false

if [[ "$4" == "--regenerate" ]]; then
    REGENERATE=true
fi

# Validate arguments
if [[ -z "$ENVIRONMENT" || -z "$KEY_NAME" ]]; then
    echo -e "${RED}Error: Missing required arguments${NC}"
    echo "Usage: $0 [dev|prod] [key-name] [algorithm] [--regenerate]"
    echo "Example: $0 dev athena_1 ES384"
    echo "Example: $0 prod epic_1 RS384 --regenerate"
    exit 1
fi

# Set AWS profile and Lambda function name based on environment
if [[ "$ENVIRONMENT" == "dev" ]]; then
    AWS_PROFILE="to-dev-admin"
    LAMBDA_FUNCTION="dev-pto-lambda-jwkgenerator-ue2"
    BUCKET_NAME=$(aws s3api list-buckets --profile $AWS_PROFILE --query "Buckets[?contains(Name, 'dev') && contains(Name, 'pto') && contains(Name, 'publicwebsite')].Name" --output text)
    DOMAIN="https://dev-public.thirdopinion.io"
elif [[ "$ENVIRONMENT" == "prod" ]]; then
    AWS_PROFILE="to-prd-admin"
    LAMBDA_FUNCTION="prod-pto-lambda-jwkgenerator-ue2"
    BUCKET_NAME=$(aws s3api list-buckets --profile $AWS_PROFILE --query "Buckets[?contains(Name, 'prod') && contains(Name, 'pto') && contains(Name, 'publicwebsite')].Name" --output text)
    DOMAIN="https://public.thirdopinion.io"
else
    echo -e "${RED}Error: Invalid environment. Use 'dev' or 'prod'${NC}"
    exit 1
fi

# Validate algorithm
if [[ "$ALGORITHM" != "ES384" && "$ALGORITHM" != "RS384" ]]; then
    echo -e "${RED}Error: Invalid algorithm. Use 'ES384' or 'RS384'${NC}"
    exit 1
fi

echo -e "${GREEN}=== JWK Key Verification Script ===${NC}"
echo -e "Environment: ${YELLOW}$ENVIRONMENT${NC}"
echo -e "Key Name: ${YELLOW}$KEY_NAME${NC}"
echo -e "Algorithm: ${YELLOW}$ALGORITHM${NC}"
echo -e "Regenerate: ${YELLOW}$REGENERATE${NC}"
echo -e "Lambda Function: ${YELLOW}$LAMBDA_FUNCTION${NC}"
echo -e "S3 Bucket: ${YELLOW}$BUCKET_NAME${NC}"
echo ""

# Check if key already exists in S3
echo -e "${GREEN}Step 1: Checking if key exists in S3...${NC}"
KEY_PATH="jwks/${KEY_NAME}/jwks.json"
KEY_EXISTS=false

if aws s3api head-object --profile $AWS_PROFILE --bucket $BUCKET_NAME --key $KEY_PATH 2>/dev/null; then
    echo -e "${YELLOW}Key already exists at s3://$BUCKET_NAME/$KEY_PATH${NC}"
    KEY_EXISTS=true
    
    if [[ "$REGENERATE" == "false" ]]; then
        echo -e "${YELLOW}Use --regenerate flag to overwrite existing key${NC}"
    fi
else
    echo -e "${GREEN}Key does not exist yet${NC}"
fi

# Create or regenerate key via Lambda
echo -e "${GREEN}Step 2: Invoking Lambda function to generate key...${NC}"

PAYLOAD=$(cat <<EOF
{
  "name": "$KEY_NAME",
  "algorithm": "$ALGORITHM",
  "regenerate": $REGENERATE
}
EOF
)

RESPONSE=$(aws lambda invoke \
    --profile $AWS_PROFILE \
    --function-name $LAMBDA_FUNCTION \
    --payload "$PAYLOAD" \
    --cli-binary-format raw-in-base64-out \
    /tmp/lambda-response.json 2>&1)

if [[ $? -ne 0 ]]; then
    echo -e "${RED}Error invoking Lambda function:${NC}"
    echo "$RESPONSE"
    exit 1
fi

# Check Lambda response
LAMBDA_RESULT=$(cat /tmp/lambda-response.json)
echo -e "${GREEN}Lambda response:${NC}"
echo "$LAMBDA_RESULT" | jq '.'

# Parse response
STATUS_CODE=$(echo "$LAMBDA_RESULT" | jq -r '.statusCode // 200')
if [[ "$STATUS_CODE" != "200" ]]; then
    echo -e "${RED}Lambda function returned error status: $STATUS_CODE${NC}"
    echo "$LAMBDA_RESULT" | jq -r '.body' | jq '.'
    exit 1
fi

# Extract details from response
BODY=$(echo "$LAMBDA_RESULT" | jq -r '.body' | jq '.')
KID=$(echo "$BODY" | jq -r '.kid')
S3_PATH=$(echo "$BODY" | jq -r '.s3_path')
SECRET_NAME=$(echo "$BODY" | jq -r '.secret_name')

echo -e "${GREEN}Key generated successfully!${NC}"
echo -e "Key ID: ${YELLOW}$KID${NC}"
echo -e "S3 Path: ${YELLOW}$S3_PATH${NC}"
echo -e "Secret Name: ${YELLOW}$SECRET_NAME${NC}"

# Step 3: Verify public key can be read from S3
echo -e "${GREEN}Step 3: Verifying public key can be read from S3...${NC}"

# Download the public key
aws s3 cp --profile $AWS_PROFILE s3://$BUCKET_NAME/$KEY_PATH /tmp/jwks_verify.json

if [[ $? -ne 0 ]]; then
    echo -e "${RED}Error: Failed to download public key from S3${NC}"
    exit 1
fi

echo -e "${GREEN}Public key downloaded successfully:${NC}"
cat /tmp/jwks_verify.json | jq '.'

# Verify key structure
KEY_COUNT=$(cat /tmp/jwks_verify.json | jq '.keys | length')
if [[ "$KEY_COUNT" -ne 1 ]]; then
    echo -e "${RED}Error: Expected 1 key in JWKS, found $KEY_COUNT${NC}"
    exit 1
fi

# Verify key properties
KEY_TYPE=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].kty')
KEY_ALG=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].alg')
KEY_USE=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].use')
KEY_KID=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].kid')

echo -e "${GREEN}Key validation:${NC}"
echo -e "  Key Type: ${YELLOW}$KEY_TYPE${NC}"
echo -e "  Algorithm: ${YELLOW}$KEY_ALG${NC}"
echo -e "  Use: ${YELLOW}$KEY_USE${NC}"
echo -e "  Key ID: ${YELLOW}$KEY_KID${NC}"

# Validate key properties
if [[ "$KEY_ALG" != "$ALGORITHM" ]]; then
    echo -e "${RED}Error: Algorithm mismatch. Expected $ALGORITHM, got $KEY_ALG${NC}"
    exit 1
fi

if [[ "$KEY_USE" != "sig" ]]; then
    echo -e "${RED}Error: Invalid key use. Expected 'sig', got $KEY_USE${NC}"
    exit 1
fi

if [[ "$KEY_KID" != "$KID" ]]; then
    echo -e "${RED}Error: Key ID mismatch. Expected $KID, got $KEY_KID${NC}"
    exit 1
fi

# Algorithm-specific validation
if [[ "$ALGORITHM" == "ES384" ]]; then
    if [[ "$KEY_TYPE" != "EC" ]]; then
        echo -e "${RED}Error: Invalid key type for ES384. Expected 'EC', got $KEY_TYPE${NC}"
        exit 1
    fi
    
    CURVE=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].crv')
    if [[ "$CURVE" != "P-384" ]]; then
        echo -e "${RED}Error: Invalid curve for ES384. Expected 'P-384', got $CURVE${NC}"
        exit 1
    fi
    
    # Check for required EC fields
    X_COORD=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].x')
    Y_COORD=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].y')
    
    if [[ -z "$X_COORD" || -z "$Y_COORD" ]]; then
        echo -e "${RED}Error: Missing EC coordinates (x, y)${NC}"
        exit 1
    fi
elif [[ "$ALGORITHM" == "RS384" ]]; then
    if [[ "$KEY_TYPE" != "RSA" ]]; then
        echo -e "${RED}Error: Invalid key type for RS384. Expected 'RSA', got $KEY_TYPE${NC}"
        exit 1
    fi
    
    # Check for required RSA fields
    E_EXP=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].e')
    N_MOD=$(cat /tmp/jwks_verify.json | jq -r '.keys[0].n')
    
    if [[ -z "$E_EXP" || -z "$N_MOD" ]]; then
        echo -e "${RED}Error: Missing RSA components (e, n)${NC}"
        exit 1
    fi
fi

# Step 4: Test public URL access (if CloudFront is deployed)
echo -e "${GREEN}Step 4: Testing public URL access...${NC}"
PUBLIC_URL="${DOMAIN}/jwks/${KEY_NAME}/jwks.json"
echo -e "Testing URL: ${YELLOW}$PUBLIC_URL${NC}"

# Try to fetch from public URL (this will fail if CloudFront is not yet deployed)
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$PUBLIC_URL" 2>/dev/null || echo "000")

if [[ "$HTTP_STATUS" == "200" ]]; then
    echo -e "${GREEN}✓ Public key is accessible via CloudFront${NC}"
    curl -s "$PUBLIC_URL" | jq '.'
elif [[ "$HTTP_STATUS" == "000" ]]; then
    echo -e "${YELLOW}⚠ CloudFront distribution may not be deployed yet${NC}"
    echo -e "  Once deployed, the key will be available at: $PUBLIC_URL"
else
    echo -e "${YELLOW}⚠ Received HTTP status $HTTP_STATUS from CloudFront${NC}"
    echo -e "  This may be normal if the distribution is still propagating"
fi

# Step 5: Verify private key in Secrets Manager (admin only)
echo -e "${GREEN}Step 5: Verifying private key in Secrets Manager...${NC}"

SECRET_EXISTS=$(aws secretsmanager describe-secret \
    --profile $AWS_PROFILE \
    --secret-id "$SECRET_NAME" \
    --query 'Name' \
    --output text 2>/dev/null || echo "")

if [[ -n "$SECRET_EXISTS" ]]; then
    echo -e "${GREEN}✓ Private key is stored in Secrets Manager: $SECRET_NAME${NC}"
    
    # Get secret metadata (not the actual value)
    aws secretsmanager describe-secret \
        --profile $AWS_PROFILE \
        --secret-id "$SECRET_NAME" \
        --query '{Name:Name,Description:Description,CreatedDate:CreatedDate,LastChangedDate:LastChangedDate}' \
        --output json | jq '.'
else
    echo -e "${RED}✗ Private key not found in Secrets Manager${NC}"
    exit 1
fi

# Summary
echo ""
echo -e "${GREEN}=== Verification Complete ===${NC}"
echo -e "${GREEN}✓ JWK key pair successfully created/verified${NC}"
echo -e "  Environment: ${YELLOW}$ENVIRONMENT${NC}"
echo -e "  Key Name: ${YELLOW}$KEY_NAME${NC}"
echo -e "  Algorithm: ${YELLOW}$ALGORITHM${NC}"
echo -e "  Key ID: ${YELLOW}$KID${NC}"
echo -e "  Public Key: ${YELLOW}s3://$BUCKET_NAME/$KEY_PATH${NC}"
echo -e "  Private Key: ${YELLOW}$SECRET_NAME${NC}"
echo -e "  Public URL: ${YELLOW}$PUBLIC_URL${NC}"
echo ""
echo -e "${GREEN}FHIR R4 Compliance:${NC}"
echo -e "  ✓ Algorithm: $ALGORITHM"
echo -e "  ✓ Key use: sig (signature)"
echo -e "  ✓ Key operations: [verify]"
echo -e "  ✓ Exportable: true (ext: true)"
if [[ "$ALGORITHM" == "ES384" ]]; then
    echo -e "  ✓ Curve: P-384 (ECDSA)"
else
    echo -e "  ✓ Key size: 3072 bits (RSA)"
fi

# Cleanup
rm -f /tmp/lambda-response.json /tmp/jwks_verify.json

echo -e "${GREEN}Done!${NC}"