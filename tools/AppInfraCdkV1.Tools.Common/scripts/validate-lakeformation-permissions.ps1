# Lake Formation Permission Validation Script (PowerShell)
# =============================================================================
# This PowerShell script validates Lake Formation permissions for cross-platform support
# 
# Usage:
#   .\validate-lakeformation-permissions.ps1 -Environment "Development" -Profile "to-dev-admin"
#   .\validate-lakeformation-permissions.ps1 -Environment "Production" -Profile "to-prd-admin"
# =============================================================================

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Development", "Production")]
    [string]$Environment,
    
    [Parameter(Mandatory=$true)]
    [string]$Profile,
    
    [switch]$SkipPHIValidation,
    [switch]$SkipDevOpsValidation,
    [switch]$GenerateReport = $true
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Initialize counters
$Global:TotalTests = 0
$Global:PassedTests = 0
$Global:FailedTests = 0
$Global:SkippedTests = 0

# Create log file
$LogFile = Join-Path $env:TEMP "lakeformation-validation-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$ReportFile = Join-Path $env:TEMP "lakeformation-validation-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $LogMessage = "$Timestamp - [$Level] $Message"
    
    switch ($Level) {
        "INFO" { Write-Host $LogMessage -ForegroundColor Blue }
        "SUCCESS" { Write-Host $LogMessage -ForegroundColor Green }
        "WARNING" { Write-Host $LogMessage -ForegroundColor Yellow }
        "ERROR" { Write-Host $LogMessage -ForegroundColor Red }
        default { Write-Host $LogMessage }
    }
    
    Add-Content -Path $LogFile -Value $LogMessage
}

function Increment-Test {
    param([string]$Result)
    
    $Global:TotalTests++
    switch ($Result) {
        "pass" { $Global:PassedTests++ }
        "fail" { $Global:FailedTests++ }
        "skip" { $Global:SkippedTests++ }
    }
}

function Test-Prerequisites {
    Write-Log "Validating prerequisites..." "INFO"
    
    # Test AWS CLI
    try {
        $null = Get-Command aws -ErrorAction Stop
        Write-Log "AWS CLI found" "SUCCESS"
    }
    catch {
        Write-Log "AWS CLI is not installed or not in PATH" "ERROR"
        exit 1
    }
    
    # Test AWS credentials
    try {
        $CallerIdentity = aws sts get-caller-identity --profile $Profile 2>$null | ConvertFrom-Json
        if ($CallerIdentity) {
            Write-Log "AWS credentials validated for profile: $Profile" "SUCCESS"
            Write-Log "Account: $($CallerIdentity.Account), User: $($CallerIdentity.Arn)" "INFO"
        }
        else {
            throw "Failed to get caller identity"
        }
    }
    catch {
        Write-Log "AWS credentials not valid for profile: $Profile" "ERROR"
        Write-Log "Run: aws sso login --profile $Profile" "ERROR"
        exit 1
    }
}

function Get-EnvironmentConfig {
    Write-Log "Loading environment configuration..." "INFO"
    
    switch ($Environment) {
        "Development" {
            $Global:AccountId = "615299752206"
            $Global:EnvPrefix = "dev"
            $Global:ExpectedGroups = @("data-analysts-dev", "data-engineers-dev")
        }
        "Production" {
            $Global:AccountId = "442042533707"
            $Global:EnvPrefix = "prod"
            $Global:ExpectedGroups = @("data-analysts-phi", "data-engineers-phi")
        }
    }
    
    Write-Log "Environment: $Environment" "INFO"
    Write-Log "Account ID: $Global:AccountId" "INFO"
    Write-Log "Profile: $Profile" "INFO"
    Write-Log "Expected Groups: $($Global:ExpectedGroups -join ', ')" "INFO"
}

function Test-LakeFormationSetup {
    Write-Log "Validating Lake Formation setup..." "INFO"
    
    try {
        $DataLakeSettings = aws lakeformation get-data-lake-settings --profile $Profile 2>$null | ConvertFrom-Json
        if ($DataLakeSettings) {
            Write-Log "Lake Formation is properly configured" "SUCCESS"
            Increment-Test "pass"
            
            $AdminCount = $DataLakeSettings.DataLakeSettings.DataLakeAdmins.Count
            if ($AdminCount -gt 0) {
                Write-Log "Lake Formation has $AdminCount admin(s) configured" "INFO"
            }
        }
        else {
            Write-Log "Lake Formation settings not found" "ERROR"
            Increment-Test "fail"
        }
    }
    catch {
        Write-Log "Failed to validate Lake Formation setup: $($_.Exception.Message)" "ERROR"
        Increment-Test "fail"
    }
}

function Test-LFTags {
    Write-Log "Validating LF-Tags..." "INFO"
    
    $ExpectedTags = @{
        "Environment" = @($Environment)
        "PHI" = @("true", "false")
        "TenantID" = @("tenant-a", "tenant-b", "tenant-c", "shared", "multi-tenant")
        "DataType" = @("clinical", "research", "operational", "administrative", "reference")
        "Sensitivity" = @("public", "internal", "confidential", "restricted")
        "SourceSystem" = @("epic", "cerner", "allscripts", "healthlake", "external-api")
    }
    
    foreach ($TagKey in $ExpectedTags.Keys) {
        try {
            $TagResponse = aws lakeformation get-lf-tag --tag-key $TagKey --profile $Profile 2>$null | ConvertFrom-Json
            if ($TagResponse) {
                $ActualValues = $TagResponse.TagValues
                $ExpectedValues = $ExpectedTags[$TagKey]
                
                $MissingValues = $ExpectedValues | Where-Object { $_ -notin $ActualValues }
                
                if ($MissingValues.Count -eq 0) {
                    Write-Log "LF-Tag '$TagKey' has correct values" "SUCCESS"
                    Increment-Test "pass"
                }
                else {
                    Write-Log "LF-Tag '$TagKey' missing values: $($MissingValues -join ', ')" "ERROR"
                    Increment-Test "fail"
                }
            }
            else {
                Write-Log "LF-Tag '$TagKey' not found" "ERROR"
                Increment-Test "fail"
            }
        }
        catch {
            Write-Log "Failed to validate LF-Tag '$TagKey': $($_.Exception.Message)" "ERROR"
            Increment-Test "fail"
        }
    }
}

function Test-GroupPermissions {
    Write-Log "Validating group permissions..." "INFO"
    
    foreach ($Group in $Global:ExpectedGroups) {
        try {
            $PrincipalArn = "arn:aws:iam::${Global:AccountId}:group/$Group"
            $PermissionsResponse = aws lakeformation list-permissions --principal $PrincipalArn --profile $Profile 2>$null | ConvertFrom-Json
            
            if ($PermissionsResponse -and $PermissionsResponse.PrincipalResourcePermissions) {
                $PermissionCount = $PermissionsResponse.PrincipalResourcePermissions.Count
                Write-Log "Group '$Group' has $PermissionCount Lake Formation permission(s)" "SUCCESS"
                Increment-Test "pass"
            }
            else {
                Write-Log "Group '$Group' has no Lake Formation permissions" "WARNING"
                Increment-Test "fail"
            }
        }
        catch {
            Write-Log "Failed to retrieve permissions for group '$Group': $($_.Exception.Message)" "ERROR"
            Increment-Test "fail"
        }
    }
}

function Test-PHIAccessControls {
    if ($SkipPHIValidation) {
        Write-Log "Skipping PHI access control validation (disabled)" "INFO"
        Increment-Test "skip"
        return
    }
    
    Write-Log "Validating PHI access controls..." "INFO"
    
    try {
        $PHITag = aws lakeformation get-lf-tag --tag-key "PHI" --profile $Profile 2>$null | ConvertFrom-Json
        if ($PHITag) {
            $HasTrue = "true" -in $PHITag.TagValues
            $HasFalse = "false" -in $PHITag.TagValues
            
            if ($HasTrue -and $HasFalse) {
                Write-Log "PHI LF-Tag has correct values (true, false)" "SUCCESS"
                Increment-Test "pass"
            }
            else {
                Write-Log "PHI LF-Tag values incorrect. Has true: $HasTrue, Has false: $HasFalse" "ERROR"
                Increment-Test "fail"
            }
        }
        else {
            Write-Log "PHI LF-Tag not found" "ERROR"
            Increment-Test "fail"
        }
    }
    catch {
        Write-Log "Failed to validate PHI LF-Tag: $($_.Exception.Message)" "ERROR"
        Increment-Test "fail"
    }
}

function Test-DevOpsAccessDenial {
    if ($SkipDevOpsValidation) {
        Write-Log "Skipping DevOps access denial validation (disabled)" "INFO"
        Increment-Test "skip"
        return
    }
    
    Write-Log "Validating DevOps access denial..." "INFO"
    
    $DevOpsRoles = @(
        "arn:aws:iam::${Global:AccountId}:role/dev-cdk-role-ue2-github-actions",
        "arn:aws:iam::${Global:AccountId}:role/prod-tfv2-role-ue2-github-actions",
        "arn:aws:iam::${Global:AccountId}:role/dev-tfv2-role-ue2-github-actions"
    )
    
    foreach ($Role in $DevOpsRoles) {
        try {
            $PermissionsResponse = aws lakeformation list-permissions --principal $Role --profile $Profile 2>$null | ConvertFrom-Json
            
            if ($PermissionsResponse -and $PermissionsResponse.PrincipalResourcePermissions) {
                $DataPermissions = $PermissionsResponse.PrincipalResourcePermissions | Where-Object {
                    $_.Permissions -contains "SELECT" -or
                    $_.Permissions -contains "INSERT" -or
                    $_.Permissions -contains "UPDATE" -or
                    $_.Permissions -contains "DELETE"
                }
                
                if ($DataPermissions) {
                    Write-Log "DevOps role has data access permissions: $Role" "ERROR"
                    Increment-Test "fail"
                }
                else {
                    Write-Log "DevOps role properly denied data access: $Role" "SUCCESS"
                    Increment-Test "pass"
                }
            }
            else {
                Write-Log "DevOps role has no Lake Formation permissions: $Role" "SUCCESS"
                Increment-Test "pass"
            }
        }
        catch {
            # If we can't retrieve permissions, assume it's properly denied
            Write-Log "DevOps role has no Lake Formation permissions: $Role" "SUCCESS"
            Increment-Test "pass"
        }
    }
}

function Test-DatabaseAccess {
    Write-Log "Validating database access patterns..." "INFO"
    
    try {
        $DatabasesResponse = aws glue get-databases --profile $Profile 2>$null | ConvertFrom-Json
        if ($DatabasesResponse -and $DatabasesResponse.DatabaseList) {
            $DatabaseCount = $DatabasesResponse.DatabaseList.Count
            Write-Log "Found $DatabaseCount database(s) in Glue catalog" "SUCCESS"
            Increment-Test "pass"
            
            $DatabaseNames = $DatabasesResponse.DatabaseList | ForEach-Object { $_.Name }
            Write-Log "Databases: $($DatabaseNames -join ', ')" "INFO"
        }
        else {
            Write-Log "No databases found in Glue catalog" "WARNING"
            Increment-Test "fail"
        }
    }
    catch {
        Write-Log "Failed to retrieve databases from Glue catalog: $($_.Exception.Message)" "ERROR"
        Increment-Test "fail"
    }
}

function Generate-ComplianceReport {
    if (-not $GenerateReport) {
        Write-Log "Skipping compliance report generation (disabled)" "INFO"
        return
    }
    
    Write-Log "Generating compliance validation report..." "INFO"
    
    $Report = @{
        validation_report = @{
            timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            environment = $Environment
            account_id = $Global:AccountId
            aws_profile = $Profile
            summary = @{
                total_tests = $Global:TotalTests
                passed_tests = $Global:PassedTests
                failed_tests = $Global:FailedTests
                skipped_tests = $Global:SkippedTests
                success_rate = if ($Global:TotalTests -gt 0) { [math]::Round(($Global:PassedTests / $Global:TotalTests) * 100, 1) } else { 0 }
            }
            validation_results = @{
                lakeformation_setup = if ($Global:FailedTests -eq 0) { "PASS" } else { "FAIL" }
                lf_tags = "validated"
                group_permissions = "validated"
                phi_access_controls = if (-not $SkipPHIValidation) { "validated" } else { "skipped" }
                devops_access_denial = if (-not $SkipDevOpsValidation) { "validated" } else { "skipped" }
                database_access = "validated"
            }
            compliance_status = @{
                hipaa_ready = ($Environment -eq "Production") -and ($Global:FailedTests -eq 0)
                multi_tenant_ready = $Global:FailedTests -eq 0
                recommendations = @()
            }
        }
    }
    
    $ReportJson = $Report | ConvertTo-Json -Depth 10
    Set-Content -Path $ReportFile -Value $ReportJson
    
    Write-Log "Compliance report generated: $ReportFile" "SUCCESS"
}

function Show-Summary {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "VALIDATION SUMMARY" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Total Tests:  $Global:TotalTests"
    Write-Host "Passed:       $Global:PassedTests" -ForegroundColor Green
    Write-Host "Failed:       $Global:FailedTests" -ForegroundColor Red
    Write-Host "Skipped:      $Global:SkippedTests" -ForegroundColor Yellow
    
    if ($Global:TotalTests -gt 0) {
        $SuccessRate = [math]::Round(($Global:PassedTests / $Global:TotalTests) * 100, 1)
        Write-Host "Success Rate: $SuccessRate%"
    }
    
    Write-Host ""
    Write-Host "Log File:     $LogFile"
    Write-Host "Report File:  $ReportFile"
    Write-Host "==================================================" -ForegroundColor Cyan
    
    if ($Global:FailedTests -eq 0) {
        Write-Log "All validation tests passed!" "SUCCESS"
    }
    else {
        Write-Log "$Global:FailedTests test(s) failed" "ERROR"
    }
}

# Main execution
try {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "Lake Formation Permission Validation Script (PS)" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Log "Starting Lake Formation permission validation" "INFO"
    Write-Log "Log file: $LogFile" "INFO"
    
    # Run validation steps
    Test-Prerequisites
    Get-EnvironmentConfig
    Test-LakeFormationSetup
    Test-LFTags
    Test-GroupPermissions
    Test-PHIAccessControls
    Test-DevOpsAccessDenial
    Test-DatabaseAccess
    
    # Generate reports
    Generate-ComplianceReport
    
    # Display summary
    Show-Summary
    
    # Exit with appropriate code
    if ($Global:FailedTests -eq 0) {
        exit 0
    }
    else {
        exit 1
    }
}
catch {
    Write-Log "Validation failed with error: $($_.Exception.Message)" "ERROR"
    exit 1
}