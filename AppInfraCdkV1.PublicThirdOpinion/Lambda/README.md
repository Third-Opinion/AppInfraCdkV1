# JWK Generator Lambda Function

This Lambda function generates JWK (JSON Web Key) key pairs for FHIR R4 authentication.

## Features

- Supports ES384 (ECDSA with P-384 curve) and RS384 (RSA with SHA-384) algorithms
- Stores public keys in S3 at path `jwks/{name}/jwks.json`
- Stores private keys securely in AWS Secrets Manager
- Supports key regeneration with optional `regenerate` flag
- FHIR R4 compliant JWK format

## Building

Run the build script to compile the Lambda:

```bash
./build-lambda.sh
```

This will:
1. Restore NuGet packages
2. Build the project in Release mode
3. Publish for Linux x64 runtime
4. Output to `publish/` directory

## Input Format

```json
{
  "name": "athena_1",
  "algorithm": "ES384",
  "regenerate": false
}
```

## Output Format

```json
{
  "statusCode": 200,
  "body": {
    "message": "Successfully generated JWK for athena_1",
    "kid": "uuid-here",
    "algorithm": "ES384",
    "s3_path": "s3://bucket/jwks/athena_1/jwks.json",
    "secret_name": "dev/jwk/athena_1"
  }
}
```

## JWK Format

The public key stored in S3 follows the FHIR R4 JWK specification:

```json
{
  "keys": [
    {
      "kty": "EC",
      "use": "sig",
      "alg": "ES384",
      "kid": "unique-key-id",
      "key_ops": ["verify"],
      "ext": true,
      "crv": "P-384",
      "x": "base64url-encoded-x-coordinate",
      "y": "base64url-encoded-y-coordinate"
    }
  ]
}
```

## Testing

Use the verification script to test key generation:

```bash
../scripts/verify-jwk-keys.sh dev athena_1 ES384
```

## Environment Variables

- `BUCKET_NAME`: S3 bucket for storing public keys
- `ENVIRONMENT`: Environment name (Development/Production)
- `SECRETS_PREFIX`: Prefix for Secrets Manager keys