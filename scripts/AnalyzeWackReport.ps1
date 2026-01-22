<#
.SYNOPSIS
    Analyzes and summarizes the WACK XML report.
    Restored from previous implementation (wack/analyze-wack.ps1) with improvements.

.DESCRIPTION
    Parses the WACK XML report and prints a human-readable summary of passed/failed tests.
    If no report path is provided, it attempts to find the most recent XML report in the standard output directory.
#>

param(
    [string]$ReportPath
)

$ScriptDir = $PSScriptRoot

# If no path provided, try to find the latest report
if (-not $ReportPath) {
    $ReportDir = Join-Path $ScriptDir 'wack_reports'
    if (Test-Path $ReportDir) {
        $LatestReport = Get-ChildItem -Path $ReportDir -Filter "WACK-*.xml" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($LatestReport) {
            $ReportPath = $LatestReport.FullName
            Write-Host "Using latest report: $ReportPath" -ForegroundColor Gray
        }
    }
}

if (-not $ReportPath -or -not (Test-Path $ReportPath)) {
    Write-Host "ERROR: WACK report not found." -ForegroundColor Red
    if ($ReportPath) { Write-Host "Path: $ReportPath" -ForegroundColor Gray }
    Write-Host "Usage: .\AnalyzeWackReport.ps1 [path-to-wack-report.xml]" -ForegroundColor Yellow
    exit 1
}

# XML parsing logic from original analyze-wack.ps1
[xml]$xml = Get-Content $ReportPath

Write-Host "`n=== WACK Test Summary ===" -ForegroundColor Cyan
$overallResult = $xml.REPORT.OVERALL_RESULT
Write-Host "Overall Result: $overallResult" -ForegroundColor $(if ($overallResult -eq 'PASS') { 'Green' } else { 'Red' })

# Get all tests from all requirements
$allTests = @($xml.REPORT.REQUIREMENTS.REQUIREMENT | ForEach-Object { $_.TEST })
if ($allTests.Count -eq 0) {
    Write-Host "WARNING: No tests found in report. The test run might have crashed or been incomplete." -ForegroundColor Yellow
    exit
}

# Improved filtering logic to handle XML structure variations
$failedTests = @($allTests | Where-Object {
    $_.RESULT -match 'FAIL' -or
    $_.'#cdata-section' -match 'FAIL' -or
    $_.RESULT.'#cdata-section' -match 'FAIL' -or
    $_.RESULT.Text -match 'FAIL'
})

$passedTests = @($allTests | Where-Object {
    $_.RESULT -match 'PASS' -or
    $_.'#cdata-section' -match 'PASS' -or
    $_.RESULT.'#cdata-section' -match 'PASS' -or
    $_.RESULT.Text -match 'PASS'
})

Write-Host "Total tests: $($allTests.Count)" -ForegroundColor White
Write-Host "Passed: $($passedTests.Count)" -ForegroundColor Green
Write-Host "Failed: $($failedTests.Count)" -ForegroundColor Red

if ($failedTests.Count -gt 0) {
    Write-Host "`n=== Failed Tests (Grouped by Requirement) ===" -ForegroundColor Red

    foreach ($req in $xml.REPORT.REQUIREMENTS.REQUIREMENT) {
        $filteredTests = @($req.TEST)
        $failedInReq = @($filteredTests | Where-Object {
            $_.RESULT -match 'FAIL' -or
            $_.'#cdata-section' -match 'FAIL' -or
            $_.RESULT.'#cdata-section' -match 'FAIL' -or
            $_.RESULT.Text -match 'FAIL'
        })

        if ($failedInReq.Count -gt 0) {
            Write-Host "`n[$($req.NUMBER)] $($req.TITLE)" -ForegroundColor Cyan
            foreach ($test in $failedInReq) {
                $optional = if ($test.OPTIONAL -eq 'TRUE') { ' [Optional]' } else { ' [Required]' }
                Write-Host "  [$($test.INDEX)]$optional $($test.NAME)" -ForegroundColor Yellow
                # Try to extract message, handling CDATA quirks
                $desc = if ($test.DESCRIPTION.'#cdata-section') { $test.DESCRIPTION.'#cdata-section' } else { $test.DESCRIPTION }
                Write-Host "    $desc" -ForegroundColor Gray
            }
        }
    }

    Write-Host "`n=== Summary Table ===" -ForegroundColor Cyan
    $summary = $failedTests | ForEach-Object {
        [PSCustomObject]@{
            Index = $_.INDEX
            Name = $_.NAME
            Required = if ($_.OPTIONAL -eq 'FALSE') { 'Yes' } else { 'No' }
        }
    }
    $summary | Format-Table -AutoSize

    Write-Host "`nRequired failures: $(($summary | Where-Object { $_.Required -eq 'Yes' }).Count)" -ForegroundColor Red
    Write-Host "Optional failures: $(($summary | Where-Object { $_.Required -eq 'No' }).Count)" -ForegroundColor Yellow
} else {
    Write-Host "`nALL TESTS PASSED!" -ForegroundColor Green
}
