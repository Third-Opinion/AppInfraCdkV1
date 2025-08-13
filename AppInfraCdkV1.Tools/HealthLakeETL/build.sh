#!/bin/bash

# Build script for HealthLake ETL Processor
# This script builds the ETL processor for different deployment targets

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_FILE="$SCRIPT_DIR/AppInfraCdkV1.Tools.HealthLakeETL.csproj"

echo "Building HealthLake ETL Processor..."

# Function to build for Lambda
build_lambda() {
    echo "Building for AWS Lambda..."
    dotnet publish "$PROJECT_FILE" \
        -c Release \
        -r linux-x64 \
        --self-contained false \
        -p:PublishReadyToRun=true \
        -o "$SCRIPT_DIR/bin/Release/net8.0/publish"
    
    echo "Lambda package ready at: $SCRIPT_DIR/bin/Release/net8.0/publish"
}

# Function to build for Glue
build_glue() {
    echo "Building for AWS Glue..."
    dotnet publish "$PROJECT_FILE" \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -o "$SCRIPT_DIR/bin/Release/net8.0/glue"
    
    echo "Glue executable ready at: $SCRIPT_DIR/bin/Release/net8.0/glue/healthlake-etl"
}

# Function to build for local testing
build_local() {
    echo "Building for local testing..."
    dotnet build "$PROJECT_FILE" -c Debug
    echo "Local build ready. Run with: dotnet run --project $PROJECT_FILE"
}

# Function to upload to S3
upload_to_s3() {
    local bucket=$1
    local profile=${2:-default}
    
    if [ -z "$bucket" ]; then
        echo "Error: S3 bucket name required"
        echo "Usage: ./build.sh upload <bucket-name> [profile]"
        exit 1
    fi
    
    echo "Uploading to S3 bucket: $bucket"
    
    # Upload Lambda package
    aws s3 cp "$SCRIPT_DIR/bin/Release/net8.0/publish/" \
        "s3://$bucket/lambda/healthlake-etl/" \
        --recursive \
        --profile "$profile"
    
    # Upload Glue executable
    aws s3 cp "$SCRIPT_DIR/bin/Release/net8.0/glue/healthlake-etl" \
        "s3://$bucket/glue-scripts/healthlake-etl" \
        --profile "$profile"
    
    echo "Upload complete!"
}

# Parse command line arguments
case "${1:-all}" in
    lambda)
        build_lambda
        ;;
    glue)
        build_glue
        ;;
    local)
        build_local
        ;;
    all)
        build_lambda
        build_glue
        ;;
    upload)
        build_lambda
        build_glue
        upload_to_s3 "$2" "${3:-default}"
        ;;
    clean)
        echo "Cleaning build artifacts..."
        rm -rf "$SCRIPT_DIR/bin" "$SCRIPT_DIR/obj"
        echo "Clean complete!"
        ;;
    *)
        echo "Usage: ./build.sh [lambda|glue|local|all|upload|clean]"
        echo "  lambda  - Build for AWS Lambda deployment"
        echo "  glue    - Build for AWS Glue job"
        echo "  local   - Build for local testing"
        echo "  all     - Build for both Lambda and Glue"
        echo "  upload  - Build and upload to S3 (requires bucket name)"
        echo "  clean   - Remove build artifacts"
        exit 1
        ;;
esac

echo "Build complete!"