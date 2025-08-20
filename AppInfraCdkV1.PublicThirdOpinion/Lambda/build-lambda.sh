#!/bin/bash

# Build and publish the Lambda function for deployment

set -e

echo "Building JWK Generator Lambda function..."

# Navigate to Lambda directory
cd "$(dirname "$0")"

# Clean previous builds
rm -rf bin obj publish

# Restore dependencies
dotnet restore

# Build the project
dotnet build -c Release

# Publish for Lambda (Linux x64)
dotnet publish -c Release -r linux-x64 --self-contained false -o publish

# Create a zip file if needed (optional, CDK will handle this)
# cd publish && zip -r ../jwk-generator.zip . && cd ..

echo "Lambda function built successfully!"
echo "Output directory: $(pwd)/publish"