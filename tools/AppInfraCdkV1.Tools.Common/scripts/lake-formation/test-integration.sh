#!/bin/bash

# Lake Formation Identity Center Integration Test Suite
# Comprehensive testing for Lake Formation Identity Center integration

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Test results tracking
TESTS_RUN=0
TESTS_PASSED=0
TESTS_FAILED=0
TESTS_SKIPPED=0

# Test report file
TEST_REPORT="test-report-$(date +%Y%m%d-%H%M%S).txt"

# Function to print colored output
print_status() {
    local status=$1
    local message=$2
    
    case $status in
        "success")
            echo -e "${GREEN}✓${NC} $message"
            ;;
        "error")
            echo -e "${RED}✗${NC} $message"
            ;;
        "warning")
            echo -e "${YELLOW}⚠${NC} $message"
            ;;
        "info")
            echo -e "${BLUE}ℹ${NC} $message"
            ;;
        "test")
            echo -e "${CYAN}▶${NC} $message"
            ;;
        *)
            echo "$message"
            ;;
    esac
}

# Function to log test results
log_test() {
    local test_name=$1
    local result=$2
    local details=$3
    
    echo "[$result] $test_name: $details" >> "$TEST_REPORT"
    
    TESTS_RUN=$((TESTS_RUN + 1))
    
    case $result in
        "PASS")
            TESTS_PASSED=$((TESTS_PASSED + 1))
            print_status "success" "TEST PASSED: $test_name"
            ;;
        "FAIL")
            TESTS_FAILED=$((TESTS_FAILED + 1))
            print_status "error" "TEST FAILED: $test_name"
            echo "  Details: $details"
            ;;
        "SKIP")
            TESTS_SKIPPED=$((TESTS_SKIPPED + 1))
            print_status "warning" "TEST SKIPPED: $test_name"
            echo "  Reason: $details"
            ;;
    esac
}

# Test 1: Prerequisites validation
test_prerequisites() {
    print_status "test" "Testing prerequisites validation..."
    
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local prereq_script="$script_dir/check-prerequisites.sh"
    
    if [ ! -f "$prereq_script" ]; then
        log_test "Prerequisites Script Exists" "FAIL" "Script not found at $prereq_script"
        return 1
    fi
    
    log_test "Prerequisites Script Exists" "PASS" "Script found"
    
    # Test script execution for dev
    if bash "$prereq_script" dev > /dev/null 2>&1; then
        log_test "Prerequisites Check - Dev" "PASS" "All prerequisites met"
    else
        log_test "Prerequisites Check - Dev" "FAIL" "Prerequisites not met for dev environment"
    fi
    
    return 0
}

# Test 2: AWS Authentication
test_aws_auth() {
    print_status "test" "Testing AWS authentication..."
    
    local environments=("dev" "prod")
    local profiles=("to-dev-admin" "to-prd-admin")
    local account_ids=("615299752206" "442042533707")
    
    for i in "${!environments[@]}"; do
        local env="${environments[$i]}"
        local profile="${profiles[$i]}"
        local expected_account="${account_ids[$i]}"
        
        # Test authentication
        if aws sts get-caller-identity --profile "$profile" > /dev/null 2>&1; then
            local actual_account=$(aws sts get-caller-identity --profile "$profile" --query Account --output text)
            
            if [ "$actual_account" = "$expected_account" ]; then
                log_test "AWS Auth - $env" "PASS" "Authenticated to correct account"
            else
                log_test "AWS Auth - $env" "FAIL" "Wrong account: expected $expected_account, got $actual_account"
            fi
        else
            log_test "AWS Auth - $env" "SKIP" "Not authenticated - run 'aws sso login --profile $profile'"
        fi
    done
}

# Test 3: Identity Center Configuration
test_identity_center() {
    print_status "test" "Testing Identity Center configuration..."
    
    # Identity Center is only in production account
    local profile="to-prd-admin"
    print_status "info" "Note: Identity Center is only in production account (442042533707)"
    
    # Check for Identity Center instances in production
    local instances=$(aws sso-admin list-instances --profile "$profile" 2>/dev/null || echo "")
    
    if [ -z "$instances" ] || [ "$instances" = "[]" ]; then
        log_test "Identity Center Instance" "FAIL" "No Identity Center instances found in production"
        return 1
    fi
    
    local instance_count=$(echo "$instances" | jq '.Instances | length')
    
    if [ "$instance_count" -gt 0 ]; then
        log_test "Identity Center Instance" "PASS" "Found $instance_count instance(s)"
        
        # Get instance details
        local instance_arn=$(echo "$instances" | jq -r '.Instances[0].InstanceArn')
        local identity_store_id=$(echo "$instances" | jq -r '.Instances[0].IdentityStoreId')
        
        if [ -n "$instance_arn" ] && [ "$instance_arn" != "null" ]; then
            log_test "Identity Center ARN" "PASS" "ARN retrieved successfully"
        else
            log_test "Identity Center ARN" "FAIL" "Could not retrieve instance ARN"
        fi
        
        if [ -n "$identity_store_id" ] && [ "$identity_store_id" != "null" ]; then
            log_test "Identity Store ID" "PASS" "ID retrieved successfully"
        else
            log_test "Identity Store ID" "FAIL" "Could not retrieve Identity Store ID"
        fi
    else
        log_test "Identity Center Instance" "FAIL" "No instances available"
    fi
}

# Test 4: Lake Formation Configuration
test_lakeformation_config() {
    print_status "test" "Testing Lake Formation configuration..."
    
    local profile="to-dev-admin"
    local account_id="615299752206"
    
    # Check Lake Formation Identity Center configuration
    local config=$(aws lakeformation describe-lake-formation-identity-center-configuration \
        --catalog-id "$account_id" \
        --profile "$profile" \
        2>/dev/null || echo "{}")
    
    if [ "$config" = "{}" ] || [ -z "$config" ]; then
        log_test "Lake Formation Config Exists" "FAIL" "No configuration found - run setup script first"
        return 1
    fi
    
    log_test "Lake Formation Config Exists" "PASS" "Configuration found"
    
    # Verify instance ARN is present
    local instance_arn=$(echo "$config" | jq -r '.InstanceArn // empty')
    
    if [ -n "$instance_arn" ] && [ "$instance_arn" != "empty" ]; then
        log_test "Lake Formation Instance ARN" "PASS" "Instance ARN configured"
    else
        log_test "Lake Formation Instance ARN" "FAIL" "Instance ARN not configured"
    fi
    
    # Check external filtering
    local external_filtering=$(echo "$config" | jq -r '.ExternalFiltering.Status // empty')
    
    if [ "$external_filtering" = "ENABLED" ]; then
        log_test "External Filtering" "PASS" "External filtering is enabled"
    elif [ "$external_filtering" = "DISABLED" ]; then
        log_test "External Filtering" "FAIL" "External filtering is disabled"
    else
        log_test "External Filtering" "SKIP" "External filtering status unknown"
    fi
}

# Test 5: Group Synchronization
test_group_sync() {
    print_status "test" "Testing group synchronization..."
    
    # Groups are in production Identity Center
    local profile="to-prd-admin"
    print_status "info" "Checking groups in production Identity Center..."
    
    # Get Identity Store ID
    local identity_store_id=""
    if [ -f .identity-center-env ]; then
        source .identity-center-env
        identity_store_id="$IDENTITY_STORE_ID"
    else
        # Get from production account
        local instances=$(aws sso-admin list-instances --profile "$profile" 2>/dev/null || echo "")
        if [ -n "$instances" ]; then
            identity_store_id=$(echo "$instances" | jq -r '.Instances[0].IdentityStoreId // empty')
        fi
    fi
    
    if [ -z "$identity_store_id" ] || [ "$identity_store_id" = "empty" ]; then
        log_test "Group Sync - Identity Store" "SKIP" "Identity Store ID not available"
        return 1
    fi
    
    # Test for expected groups
    local expected_groups=("data-analysts-dev" "data-analysts-phi" "data-engineers-phi")
    local groups_found=0
    local groups_missing=0
    
    for group_name in "${expected_groups[@]}"; do
        local group_search=$(aws identitystore list-groups \
            --identity-store-id "$identity_store_id" \
            --profile "$profile" \
            --filters "AttributePath=DisplayName,AttributeValue=$group_name" \
            2>/dev/null || echo "{}")
        
        if [ "$group_search" != "{}" ] && [ "$(echo "$group_search" | jq '.Groups | length')" -gt 0 ]; then
            log_test "Group Sync - $group_name" "PASS" "Group found in Identity Center"
            groups_found=$((groups_found + 1))
        else
            # Groups may not be synced yet - this is not necessarily a failure
            log_test "Group Sync - $group_name" "SKIP" "Group not yet synced (may take 15-40 minutes)"
            groups_missing=$((groups_missing + 1))
        fi
    done
    
    # Summary for group sync status
    if [ "$groups_missing" -gt 0 ]; then
        print_status "info" "Note: $groups_missing group(s) not yet synced from Google Workspace"
        print_status "info" "This is expected if groups were recently created"
    fi
    
    if [ "$groups_found" -gt 0 ]; then
        log_test "Group Sync Summary" "PASS" "$groups_found of ${#expected_groups[@]} groups available"
    else
        log_test "Group Sync Summary" "SKIP" "No groups synced yet - check Google Workspace sync status"
    fi
}

# Test 6: Rollback Capability
test_rollback() {
    print_status "test" "Testing rollback capability..."
    
    # Check for backup files
    local backup_found=false
    
    for backup_dir in backups/prod-* backups/dev-*; do
        if [ -d "$backup_dir" ]; then
            backup_found=true
            log_test "Rollback Backup Exists" "PASS" "Backup found at $backup_dir"
            
            # Check backup contents
            if [ -f "$backup_dir/data-lake-settings.json" ]; then
                log_test "Rollback - Data Lake Settings" "PASS" "Data lake settings backed up"
            else
                log_test "Rollback - Data Lake Settings" "FAIL" "Data lake settings not backed up"
            fi
            
            if [ -f "$backup_dir/identity-center-config.json" ]; then
                log_test "Rollback - Identity Center Config" "PASS" "Identity Center config backed up"
            else
                log_test "Rollback - Identity Center Config" "FAIL" "Identity Center config not backed up"
            fi
            
            break
        fi
    done
    
    if [ "$backup_found" = false ]; then
        log_test "Rollback Backup Exists" "SKIP" "No backup directories found"
    fi
}

# Test 7: Setup Script Functionality
test_setup_scripts() {
    print_status "test" "Testing setup script functionality..."
    
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    
    # Test dev setup script
    local dev_script="$script_dir/setup-lakeformation-identity-center-dev.sh"
    if [ -f "$dev_script" ]; then
        log_test "Dev Setup Script Exists" "PASS" "Script found"
        
        # Check if script is executable
        if [ -x "$dev_script" ]; then
            log_test "Dev Setup Script Executable" "PASS" "Script has execute permissions"
        else
            log_test "Dev Setup Script Executable" "FAIL" "Script not executable"
        fi
    else
        log_test "Dev Setup Script Exists" "FAIL" "Script not found"
    fi
    
    # Test prod setup script
    local prod_script="$script_dir/setup-lakeformation-identity-center-prod.sh"
    if [ -f "$prod_script" ]; then
        log_test "Prod Setup Script Exists" "PASS" "Script found"
        
        # Check if script is executable
        if [ -x "$prod_script" ]; then
            log_test "Prod Setup Script Executable" "PASS" "Script has execute permissions"
        else
            log_test "Prod Setup Script Executable" "FAIL" "Script not executable"
        fi
    else
        log_test "Prod Setup Script Exists" "FAIL" "Script not found"
    fi
}

# Test 8: Verification Script
test_verification() {
    print_status "test" "Testing verification script..."
    
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local verify_script="$script_dir/verify-integration.sh"
    
    if [ -f "$verify_script" ]; then
        log_test "Verification Script Exists" "PASS" "Script found"
        
        # Run verification for dev environment
        if bash "$verify_script" dev > /dev/null 2>&1; then
            log_test "Verification Script - Dev" "PASS" "Verification completed successfully"
        else
            log_test "Verification Script - Dev" "FAIL" "Verification failed or returned errors"
        fi
    else
        log_test "Verification Script Exists" "FAIL" "Script not found"
    fi
}

# Test 9: Configuration Files
test_config_files() {
    print_status "test" "Testing configuration files..."
    
    # Check for saved configuration files
    if [ -f "lake-formation-identity-center-config-dev.json" ]; then
        log_test "Dev Config File" "PASS" "Configuration file exists"
        
        # Validate JSON
        if jq '.' "lake-formation-identity-center-config-dev.json" > /dev/null 2>&1; then
            log_test "Dev Config JSON Valid" "PASS" "Valid JSON format"
        else
            log_test "Dev Config JSON Valid" "FAIL" "Invalid JSON format"
        fi
    else
        log_test "Dev Config File" "SKIP" "Configuration file not yet created"
    fi
    
    if [ -f "lake-formation-identity-center-config-prod.json" ]; then
        log_test "Prod Config File" "PASS" "Configuration file exists"
        
        # Validate JSON
        if jq '.' "lake-formation-identity-center-config-prod.json" > /dev/null 2>&1; then
            log_test "Prod Config JSON Valid" "PASS" "Valid JSON format"
        else
            log_test "Prod Config JSON Valid" "FAIL" "Invalid JSON format"
        fi
    else
        log_test "Prod Config File" "SKIP" "Configuration file not yet created"
    fi
}

# Test 10: Log Files
test_log_files() {
    print_status "test" "Testing log file generation..."
    
    # Check for log files
    local log_count=$(ls -1 lake-formation-setup-*.log 2>/dev/null | wc -l)
    
    if [ "$log_count" -gt 0 ]; then
        log_test "Log Files Generated" "PASS" "Found $log_count log file(s)"
        
        # Check log content
        local latest_log=$(ls -1t lake-formation-setup-*.log 2>/dev/null | head -1)
        if [ -f "$latest_log" ]; then
            local log_size=$(wc -l < "$latest_log")
            if [ "$log_size" -gt 0 ]; then
                log_test "Log File Content" "PASS" "Log contains $log_size lines"
            else
                log_test "Log File Content" "FAIL" "Log file is empty"
            fi
        fi
    else
        log_test "Log Files Generated" "SKIP" "No log files found"
    fi
}

# Function to generate test summary
generate_summary() {
    echo
    echo "================================================"
    echo "Test Summary"
    echo "================================================"
    echo "Tests Run:     $TESTS_RUN"
    echo -e "${GREEN}Tests Passed:  $TESTS_PASSED${NC}"
    
    if [ "$TESTS_FAILED" -gt 0 ]; then
        echo -e "${RED}Tests Failed:  $TESTS_FAILED${NC}"
    else
        echo "Tests Failed:  $TESTS_FAILED"
    fi
    
    if [ "$TESTS_SKIPPED" -gt 0 ]; then
        echo -e "${YELLOW}Tests Skipped: $TESTS_SKIPPED${NC}"
    else
        echo "Tests Skipped: $TESTS_SKIPPED"
    fi
    
    # Calculate pass rate
    if [ "$TESTS_RUN" -gt 0 ]; then
        local pass_rate=$((TESTS_PASSED * 100 / TESTS_RUN))
        echo "Pass Rate:     ${pass_rate}%"
    fi
    
    echo "================================================"
    
    # Save summary to report
    {
        echo ""
        echo "================================="
        echo "Test Execution Summary"
        echo "================================="
        echo "Timestamp: $(date)"
        echo "Total Tests: $TESTS_RUN"
        echo "Passed: $TESTS_PASSED"
        echo "Failed: $TESTS_FAILED"
        echo "Skipped: $TESTS_SKIPPED"
    } >> "$TEST_REPORT"
    
    echo
    print_status "info" "Detailed report saved to: $TEST_REPORT"
}

# Main test execution
main() {
    echo -e "${CYAN}╔════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║  Lake Formation Identity Center Test Suite     ║${NC}"
    echo -e "${CYAN}╚════════════════════════════════════════════════╝${NC}"
    echo
    echo "Starting test execution at $(date)"
    echo "Test report: $TEST_REPORT"
    echo
    
    # Initialize test report
    {
        echo "Lake Formation Identity Center Integration Test Report"
        echo "======================================================="
        echo "Started: $(date)"
        echo ""
    } > "$TEST_REPORT"
    
    # Run all tests
    test_prerequisites
    echo
    
    test_aws_auth
    echo
    
    test_identity_center
    echo
    
    test_lakeformation_config
    echo
    
    test_group_sync
    echo
    
    test_rollback
    echo
    
    test_setup_scripts
    echo
    
    test_verification
    echo
    
    test_config_files
    echo
    
    test_log_files
    echo
    
    # Generate summary
    generate_summary
    
    # Exit with appropriate code
    if [ "$TESTS_FAILED" -gt 0 ]; then
        exit 1
    else
        exit 0
    fi
}

# Run main function
main "$@"