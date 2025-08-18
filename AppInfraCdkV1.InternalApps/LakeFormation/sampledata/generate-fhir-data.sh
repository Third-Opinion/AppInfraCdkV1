#!/bin/bash

# generate-fhir-data.sh
# Script to generate sample FHIR R4 data using Synthea
# Must be run after build-synthea.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SYNTHEA_JAR="$SCRIPT_DIR/synthea.jar"
OUTPUT_DIR="$SCRIPT_DIR/generated-data"
FHIR_OUTPUT_DIR="$OUTPUT_DIR/fhir"

# Default configuration
DEFAULT_POPULATION=100
DEFAULT_STATE="Massachusetts"
DEFAULT_CITY="Boston"

echo "🏥 Generating FHIR R4 Data with Synthea"
echo "======================================="

# Check prerequisites
check_prerequisites() {
    echo "🔍 Checking prerequisites..."
    
    if [ ! -f "$SYNTHEA_JAR" ]; then
        echo "❌ Synthea JAR not found. Please run './build-synthea.sh' first."
        exit 1
    fi
    
    if ! command -v java &> /dev/null; then
        echo "❌ Java is not installed."
        exit 1
    fi
    
    echo "✅ Prerequisites satisfied"
}

# Configure Synthea for FHIR R4 output
configure_synthea() {
    echo "⚙️  Configuring Synthea for FHIR R4 output..."
    
    # Create output directory
    mkdir -p "$FHIR_OUTPUT_DIR"
    
    # Create Synthea configuration
    local config_file="$OUTPUT_DIR/synthea.properties"
    cat > "$config_file" << EOF
# Synthea Configuration for FHIR R4 Data Generation

# FHIR R4 configuration for HealthLake
exporter.fhir.export = true

# Collection bundles (not transaction bundles) for HealthLake
exporter.fhir.transaction_bundle = false

# Enable bulk data export to create NDJSON format
exporter.fhir.bulk_data = true

# US Core IG conformance version 4.0.0
exporter.fhir.use_us_core_ig = true
exporter.fhir.us_core_version = 4.0.0

# Disable SHR extensions
exporter.fhir.use_shr_extensions = false

# Disable other FHIR versions
exporter.fhir_dstu2.export = false
exporter.fhir_stu3.export = false

# Export hospital and practitioner data in separate files
exporter.hospital.fhir.export = true
exporter.hospital.fhir_stu3.export = false
exporter.hospital.fhir_dstu2.export = false

exporter.practitioner.fhir.export = true
exporter.practitioner.fhir_stu3.export = false
exporter.practitioner.fhir_dstu2.export = false

# Disable all other export formats
exporter.ccda.export = false
exporter.csv.export = false
exporter.text.export = false
exporter.json.export = false

# Output directory
exporter.baseDirectory = $FHIR_OUTPUT_DIR

# Generate realistic clinical data
generate.medications = true
generate.procedures = true
generate.immunizations = true
generate.observations = true
generate.allergies = true
generate.conditions = true

# Demographics
generate.demographics.default_file = geography/demographics.csv
generate.append_numbers_to_person_names = false
generate.only_dead_patients = false
generate.clinical_note_frequency = 0.1

# Terminology settings
generate.terminology.expand_value_sets = true
EOF
    
    echo "✅ Synthea configuration created: $config_file"
}

# Generate patient data
generate_data() {
    local population=${1:-$DEFAULT_POPULATION}
    local state=${2:-$DEFAULT_STATE}
    local city=${3:-$DEFAULT_CITY}
    
    echo "👥 Generating data for $population patients in $city, $state..."
    
    # Run Synthea with our configuration for NDJSON bulk data export
    java -jar "$SYNTHEA_JAR" \
        -p "$population" \
        -c "$OUTPUT_DIR/synthea.properties" \
        "$state" "$city"
    
    echo "✅ Data generation completed"
}

# Organize and validate generated data
organize_data() {
    echo "📁 Organizing generated FHIR data..."
    
    # Create organized structure
    local organized_dir="$OUTPUT_DIR/organized"
    mkdir -p "$organized_dir"
    
    # Check for NDJSON bundle files first (preferred for HealthLake)
    if [ -d "$FHIR_OUTPUT_DIR/fhir" ]; then
        cd "$FHIR_OUTPUT_DIR/fhir"
        
        echo "📊 FHIR Data Summary:"
        echo "===================="
        
        # Look for NDJSON files first
        local ndjson_files
        ndjson_files=$(find . -name "*.ndjson" 2>/dev/null | head -5)
        
        if [ -n "$ndjson_files" ]; then
            echo "✅ Found NDJSON bundle files (HealthLake ready):"
            find . -name "*.ndjson" | while read -r file; do
                local bundle_count
                bundle_count=$(wc -l < "$file" 2>/dev/null || echo "0")
                echo "  $(basename "$file"): $bundle_count bundles"
                
                # Copy NDJSON files to organized directory
                cp "$file" "$organized_dir/"
            done
        else
            echo "⚠️  No NDJSON files found, checking individual JSON files..."
            
            # Count each resource type in individual JSON files
            for resource_type in Patient Practitioner Organization Encounter Observation Condition Procedure Medication MedicationRequest Immunization AllergyIntolerance DiagnosticReport; do
                local count
                count=$(find . -name "*.json" -exec grep -l "\"resourceType\": \"$resource_type\"" {} \; 2>/dev/null | wc -l)
                if [ "$count" -gt 0 ]; then
                    echo "  $resource_type: $count resources"
                    
                    # Create directory for this resource type
                    mkdir -p "$organized_dir/$resource_type"
                    find . -name "*.json" -exec grep -l "\"resourceType\": \"$resource_type\"" {} \; 2>/dev/null | \
                        xargs -I {} cp {} "$organized_dir/$resource_type/"
                fi
            done
            
            echo ""
            echo "⚠️  Note: Individual JSON files found instead of NDJSON bundles."
            echo "   HealthLake requires NDJSON format. Consider regenerating with bundle settings."
        fi
        
        cd "$SCRIPT_DIR"
    fi
    
    # Create summary file
    create_summary_file "$organized_dir"
    
    echo "✅ Data organized in: $organized_dir"
}

# Create data summary file
create_summary_file() {
    local target_dir="$1"
    local summary_file="$target_dir/data-summary.json"
    
    echo "📋 Creating data summary..."
    
    # Check if we have NDJSON files or individual JSON files
    local ndjson_count
    local json_count
    local total_bundles=0
    
    ndjson_count=$(find "$target_dir" -name "*.ndjson" 2>/dev/null | wc -l)
    json_count=$(find "$target_dir" -name "*.json" 2>/dev/null | wc -l)
    
    if [ "$ndjson_count" -gt 0 ]; then
        # Count total bundles in NDJSON files
        for file in "$target_dir"/*.ndjson; do
            if [ -f "$file" ]; then
                local file_bundles
                file_bundles=$(wc -l < "$file" 2>/dev/null || echo "0")
                total_bundles=$((total_bundles + file_bundles))
            fi
        done
        
        cat > "$summary_file" << EOF
{
  "generated_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "generator": "Synthea",
  "fhir_version": "R4",
  "format": "NDJSON Bundles (HealthLake Ready)",
  "population": {
    "total_bundles": $total_bundles,
    "location": "$DEFAULT_CITY, $DEFAULT_STATE"
  },
  "files": {
    "ndjson_files": $ndjson_count,
    "total_bundles": $total_bundles
  },
  "data_structure": {
    "format": "FHIR R4 NDJSON Bundles",
    "organization": "bundle_per_line",
    "healthlake_ready": true,
    "total_files": $ndjson_count
  },
  "next_steps": [
    "Upload to S3 using upload-to-s3.sh",
    "Import into HealthLake using test-healthlake-import.sh",
    "Query via Athena through Lake Formation"
  ]
}
EOF
    else
        # Handle individual JSON files (legacy format)
        local total_patients
        total_patients=$(find "$target_dir/Patient" -name "*.json" 2>/dev/null | wc -l)
        
        cat > "$summary_file" << EOF
{
  "generated_at": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "generator": "Synthea",
  "fhir_version": "R4",
  "format": "Individual JSON Files (Not HealthLake Ready)",
  "population": {
    "total_patients": $total_patients,
    "location": "$DEFAULT_CITY, $DEFAULT_STATE"
  },
  "resources": {
EOF
        
        local first=true
        for resource_type in Patient Practitioner Organization Encounter Observation Condition Procedure Medication MedicationRequest Immunization AllergyIntolerance DiagnosticReport; do
            local count
            count=$(find "$target_dir/$resource_type" -name "*.json" 2>/dev/null | wc -l)
            if [ "$count" -gt 0 ]; then
                if [ "$first" = false ]; then
                    echo "," >> "$summary_file"
                fi
                echo "    \"$resource_type\": $count" >> "$summary_file"
                first=false
            fi
        done
        
        cat >> "$summary_file" << EOF
  },
  "data_structure": {
    "format": "FHIR R4 Individual JSON",
    "organization": "by_resource_type",
    "healthlake_ready": false,
    "total_files": $json_count,
    "note": "HealthLake requires NDJSON bundle format. Please regenerate with bundle settings."
  },
  "next_steps": [
    "Regenerate data with NDJSON bundle format",
    "Upload to S3 using upload-to-s3.sh",
    "Import into HealthLake using test-healthlake-import.sh"
  ]
}
EOF
    fi
    
    echo "✅ Summary created: $summary_file"
}

# Display usage information
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS]

Generate sample FHIR R4 data using Synthea

Options:
  -p, --population NUM    Number of patients to generate (default: $DEFAULT_POPULATION)
  -s, --state STATE      State for patient demographics (default: $DEFAULT_STATE)
  -c, --city CITY        City for patient demographics (default: $DEFAULT_CITY)
  --clean                Remove existing generated data before running
  -h, --help             Show this help message

Examples:
  $0                                # Generate $DEFAULT_POPULATION patients in $DEFAULT_CITY, $DEFAULT_STATE
  $0 -p 500                        # Generate 500 patients
  $0 -p 50 -s California -c "Los Angeles"  # Generate 50 patients in Los Angeles, CA
  $0 --clean -p 1000               # Clean and generate 1000 patients

Output:
  Generated data will be stored in: $OUTPUT_DIR/organized/
EOF
}

# Parse command line arguments
parse_arguments() {
    POPULATION=$DEFAULT_POPULATION
    STATE=$DEFAULT_STATE
    CITY=$DEFAULT_CITY
    CLEAN=false
    
    while [[ $# -gt 0 ]]; do
        case $1 in
            -p|--population)
                POPULATION="$2"
                shift 2
                ;;
            -s|--state)
                STATE="$2"
                shift 2
                ;;
            -c|--city)
                CITY="$2"
                shift 2
                ;;
            --clean)
                CLEAN=true
                shift
                ;;
            -h|--help)
                show_usage
                exit 0
                ;;
            *)
                echo "❌ Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
}

# Clean existing data
clean_data() {
    if [ "$CLEAN" = true ]; then
        echo "🧹 Cleaning existing generated data..."
        rm -rf "$OUTPUT_DIR"
        echo "✅ Cleaned"
    fi
}

# Main execution
main() {
    echo "Starting FHIR data generation..."
    echo "Configuration:"
    echo "  Population: $POPULATION patients"
    echo "  Location: $CITY, $STATE"
    echo "  Output: $OUTPUT_DIR"
    echo ""
    
    check_prerequisites
    clean_data
    configure_synthea
    generate_data "$POPULATION" "$STATE" "$CITY"
    organize_data
    
    echo ""
    echo "🎉 FHIR data generation completed successfully!"
    echo ""
    echo "Generated data location: $OUTPUT_DIR/organized/"
    echo "Summary file: $OUTPUT_DIR/organized/data-summary.json"
    echo ""
    echo "Next steps:"
    echo "1. Review the generated data in $OUTPUT_DIR/organized/"
    echo "2. Run './upload-to-s3.sh' to upload to AWS S3"
    echo "3. Import into HealthLake for testing"
}

# Execute main function with parsed arguments
parse_arguments "$@"
main