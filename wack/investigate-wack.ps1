# Investigate specific WACK test failures
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportPath = Join-Path $scriptDir 'wack-report.xml'

[xml]$xml = Get-Content $reportPath

# Focus on required failures
$requiredTests = @(31, 91, 90, 70, 62, 61, 52, 56, 57, 55, 54, 53, 92)

Write-Host "=== Investigating Required Test Failures ===" -ForegroundColor Cyan

foreach ($index in $requiredTests) {
    $test = $xml.REPORT.REQUIREMENTS.REQUIREMENT.TEST | Where-Object { $_.INDEX -eq $index }
    if ($test) {
        Write-Host "`n[$($test.INDEX)] $($test.NAME)" -ForegroundColor Yellow
        Write-Host "Description: $($test.DESCRIPTION)" -ForegroundColor Gray

        # Check if there's detailed information in the XML structure
        if ($test.MESSAGES.MESSAGE) {
            Write-Host "Messages:" -ForegroundColor Cyan
            $test.MESSAGES.MESSAGE | ForEach-Object {
                Write-Host "  - $($_.'#text')" -ForegroundColor White
            }
        } else {
            Write-Host "Messages: (none)" -ForegroundColor DarkGray
        }
    }
}

# Check for file-level details in the APPLICATIONS section
Write-Host "`n`n=== Checking Application Details ===" -ForegroundColor Cyan
if ($xml.REPORT.APPLICATIONS) {
    Write-Host "Applications section exists" -ForegroundColor Green

    # Look for specific file issues
    $files = $xml.REPORT.APPLICATIONS.Installed_Programs.Program.StaticProperties.Files.File
    Write-Host "Total files analyzed: $($files.Count)" -ForegroundColor White
}
