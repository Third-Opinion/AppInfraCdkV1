#!/bin/bash

# build-synthea.sh
# Script to clone and build Synthea for FHIR data generation
# Requires Java 11+ and Git

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SYNTHEA_DIR="$SCRIPT_DIR/synthea"
SYNTHEA_REPO="https://github.com/synthetichealth/synthea.git"

echo "🏗️  Building Synthea FHIR Data Generator"
echo "=====================================/"

# Check prerequisites
check_prerequisites() {
    echo "🔍 Checking prerequisites..."
    
    # Check Java version
    if ! command -v java &> /dev/null; then
        echo "❌ Java is not installed. Please install Java 11 or later."
        exit 1
    fi
    
    JAVA_VERSION=$(java -version 2>&1 | head -1 | awk -F '"' '/version/ {print $2}')
    if [[ "$JAVA_VERSION" =~ ^1\. ]]; then
        JAVA_MAJOR=$(echo $JAVA_VERSION | cut -d. -f2)
    else
        JAVA_MAJOR=$(echo $JAVA_VERSION | cut -d. -f1)
    fi
    
    if [ "$JAVA_MAJOR" -lt 11 ]; then
        echo "❌ Java 11 or later is required. Found Java $JAVA_VERSION"
        exit 1
    fi
    
    echo "✅ Java $JAVA_VERSION found"
    
    # Check JAVA_HOME and set if needed (macOS specific)
    if [ -z "$JAVA_HOME" ]; then
        if command -v /usr/libexec/java_home &> /dev/null; then
            export JAVA_HOME=$(/usr/libexec/java_home 2>/dev/null)
            echo "   JAVA_HOME set to: $JAVA_HOME"
        else
            echo "⚠️  JAVA_HOME not set. This may cause build issues."
        fi
    else
        echo "   JAVA_HOME: $JAVA_HOME"
    fi
    
    # Check Git
    if ! command -v git &> /dev/null; then
        echo "❌ Git is not installed. Please install Git."
        exit 1
    fi
    
    echo "✅ Git found"
    
    # Check Gradle (will be downloaded by gradlew if not present)
    echo "✅ Gradle will be handled by gradlew wrapper"
}

# Clone Synthea repository
clone_synthea() {
    echo "📥 Cloning Synthea repository..."
    
    if [ -d "$SYNTHEA_DIR" ]; then
        echo "🔄 Synthea directory exists. Updating..."
        cd "$SYNTHEA_DIR"
        git pull origin master
    else
        echo "📦 Cloning Synthea from $SYNTHEA_REPO"
        git clone "$SYNTHEA_REPO" "$SYNTHEA_DIR"
        cd "$SYNTHEA_DIR"
    fi
    
    echo "✅ Synthea repository ready"
}

# Build Synthea
build_synthea() {
    echo "🔨 Building Synthea..."
    cd "$SYNTHEA_DIR"
    
    # Make gradlew executable
    chmod +x ./gradlew
    
    # Build the project
    echo "📦 Running Gradle build..."
    ./gradlew build -x test
    
    # Create distribution
    echo "📦 Creating distribution..."
    ./gradlew distTar
    
    echo "✅ Synthea build completed"
}

# Verify build
verify_build() {
    echo "🧪 Verifying build..."
    cd "$SYNTHEA_DIR"
    
    # Check if jar files exist
    if [ -f "build/libs/synthea-with-dependencies.jar" ]; then
        echo "✅ Synthea JAR file found"
    else
        echo "❌ Synthea JAR file not found"
        exit 1
    fi
    
    # Test basic functionality
    echo "🧪 Testing Synthea..."
    java -jar build/libs/synthea-with-dependencies.jar --help > /dev/null 2>&1
    if [ $? -eq 0 ]; then
        echo "✅ Synthea is working correctly"
    else
        echo "❌ Synthea test failed"
        exit 1
    fi
}

# Create convenience symlink
create_convenience_files() {
    echo "🔗 Creating convenience files..."
    
    # Create symlink to jar for easy access
    ln -sf "$SYNTHEA_DIR/build/libs/synthea-with-dependencies.jar" "$SCRIPT_DIR/synthea.jar"
    
    # Create quick run script
    cat > "$SCRIPT_DIR/run-synthea.sh" << 'EOF'
#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
java -jar "$SCRIPT_DIR/synthea.jar" "$@"
EOF
    chmod +x "$SCRIPT_DIR/run-synthea.sh"
    
    echo "✅ Created convenience files:"
    echo "   - synthea.jar (symlink to built JAR)"
    echo "   - run-synthea.sh (quick run script)"
}

# Main execution
main() {
    echo "Starting Synthea build process..."
    echo "Working directory: $SCRIPT_DIR"
    echo ""
    
    check_prerequisites
    clone_synthea
    build_synthea
    verify_build
    create_convenience_files
    
    echo ""
    echo "🎉 Synthea build completed successfully!"
    echo ""
    echo "Next steps:"
    echo "1. Run './generate-fhir-data.sh' to generate sample FHIR data"
    echo "2. Use './run-synthea.sh' for direct Synthea access"
    echo ""
    echo "Synthea is installed at: $SYNTHEA_DIR"
}

# Handle command line arguments
case "${1:-}" in
    --help|-h)
        echo "Usage: $0 [--clean]"
        echo ""
        echo "Build Synthea FHIR data generator from source"
        echo ""
        echo "Options:"
        echo "  --clean    Remove existing Synthea directory before build"
        echo "  --help     Show this help message"
        exit 0
        ;;
    --clean)
        echo "🧹 Cleaning existing Synthea installation..."
        rm -rf "$SYNTHEA_DIR"
        rm -f "$SCRIPT_DIR/synthea.jar"
        rm -f "$SCRIPT_DIR/run-synthea.sh"
        echo "✅ Cleaned"
        ;;
esac

main