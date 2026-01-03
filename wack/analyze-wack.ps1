# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportPath = Join-Path $scriptDir 'wack-report.xml'

if (-not (Test-Path $reportPath)) {
    Write-Host "ERROR: WACK report not found at: $reportPath" -ForegroundColor Red
    Write-Host "Please run run-wack.ps1 first." -ForegroundColor Yellow
    exit 1
}

[xml]$xml = Get-Content $reportPath

Write-Host "`n=== WACK Test Summary ===" -ForegroundColor Cyan
$overallResult = $xml.REPORT.OVERALL_RESULT
Write-Host "Overall Result: $overallResult" -ForegroundColor $(if ($overallResult -eq 'PASS') { 'Green' } else { 'Red' })

# Get all tests from all requirements
$allTests = @($xml.REPORT.REQUIREMENTS.REQUIREMENT | ForEach-Object { $_.TEST })
$failedTests = @($allTests | Where-Object { $_.'#cdata-section' -like '*FAIL*' -or $_.RESULT -like '*FAIL*' -or $_.RESULT.'#cdata-section' -like '*FAIL*' })
$passedTests = @($allTests | Where-Object { $_.'#cdata-section' -like '*PASS*' -or $_.RESULT -like '*PASS*' -or $_.RESULT.'#cdata-section' -like '*PASS*' })

Write-Host "Total tests: $($allTests.Count)" -ForegroundColor White
Write-Host "Passed: $($passedTests.Count)" -ForegroundColor Green
Write-Host "Failed: $($failedTests.Count)" -ForegroundColor Red

if ($failedTests.Count -gt 0) {
    Write-Host "`n=== Failed Tests (Grouped by Requirement) ===" -ForegroundColor Red

    foreach ($req in $xml.REPORT.REQUIREMENTS.REQUIREMENT) {
        $failedInReq = @($req.TEST | Where-Object {
            $_.'#cdata-section' -like '*FAIL*' -or
            $_.RESULT -like '*FAIL*' -or
            $_.RESULT.'#cdata-section' -like '*FAIL*'
        })

        if ($failedInReq.Count -gt 0) {
            Write-Host "`n[$($req.NUMBER)] $($req.TITLE)" -ForegroundColor Cyan
            foreach ($test in $failedInReq) {
                $optional = if ($test.OPTIONAL -eq 'TRUE') { ' [Optional]' } else { ' [Required]' }
                Write-Host "  [$($test.INDEX)]$optional $($test.NAME)" -ForegroundColor Yellow
                Write-Host "    $($test.DESCRIPTION)" -ForegroundColor Gray
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
    Write-Host "`n ALL TESTS PASSED!" -ForegroundColor Green
}
