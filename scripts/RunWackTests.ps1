<#
.SYNOPSIS
    Runs Windows App Certification Kit (WACK) validation for Store submission.

.DESCRIPTION
    Automates the WACK test process for the latest available appx/msix package.
    1. Finds the latest MSIX bundle in AppPackages.
    2. Executes WACK test (appcert.exe) with "Store App" profile.
    3. Analyzes the XML report and summarizes results.

.PARAMETER TestProfile
    XML profile path for WACK. Defaults to a standard Store App profile.

.PARAMETER OutputDir
    Directory to save WACK reports. Defaults to `scripts\wack_reports`.
#>

param(
    [string]$TestProfile,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptDir
$AppPackagesDir = Join-Path $ProjectRoot 'PhotoGeoExplorer\AppPackages'

# --- 1. Locate WACK Tool ---
$KitPath86 = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe"
$KitPath64 = "${env:ProgramFiles}\Windows Kits\10\App Certification Kit\appcert.exe"

if (Test-Path $KitPath64) { $AppCert = $KitPath64 }
elseif (Test-Path $KitPath86) { $AppCert = $KitPath86 }
else { 
    throw "Windows App Certification Kit (appcert.exe) not found. Please install Windows SDK."
}

Write-Host "Using WACK: $AppCert" -ForegroundColor Gray

# --- 2. Locate Target Package ---
if (-not (Test-Path $AppPackagesDir)) { throw "AppPackages not found. Run Build first." }

# Find latest Bundle (msixbundle or appxbundle)
# WACK should test the bundle that will be uploaded to the Store (unsigned or signed with store cert).
# Typically generated in PhotoGeoExplorer_x.y.z.0_Test folders.
$Bundles = Get-ChildItem -Path $AppPackagesDir -Recurse -Include *.msixbundle, *.appxbundle | 
           Sort-Object LastWriteTime -Descending

$LatestBundle = $Bundles | Select-Object -First 1

if (-not $LatestBundle) { throw "No app bundle found in $AppPackagesDir." }

Write-Host "Target: $($LatestBundle.Name)" -ForegroundColor Cyan
Write-Host "Path:   $($LatestBundle.FullName)" -ForegroundColor Gray

# --- 3. Prepare Output ---
if (-not $OutputDir) { $OutputDir = Join-Path $ScriptDir 'wack_reports' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$ReportXml = Join-Path $OutputDir "WACK-$Timestamp.xml"

# --- 4. Run WACK ---
Write-Host "`nStarting WACK Test... (This may take several minutes)" -ForegroundColor Yellow
Write-Host "Report will be saved to: $ReportXml" -ForegroundColor Gray

# Use "Store App" validation settings
$Args = @("test", "-apptype", "calt", "-packagepath", $LatestBundle.FullName, "-reportoutputpath", $ReportXml)

$Process = Start-Process -FilePath $AppCert -ArgumentList $Args -PassThru -Wait

if ($Process.ExitCode -ne 0) {
    Write-Warning "WACK exited with code $($Process.ExitCode). Check report for details."
} else {
    Write-Host "WACK execution completed." -ForegroundColor Green
}

# --- 5. Analyze Report ---
if (Test-Path $ReportXml) {
    Write-Host "`n--- Analysis Summary ---" -ForegroundColor Cyan
    [xml]$XmlContent = Get-Content $ReportXml
    
    $OverallResult = $XmlContent.REPORT.OVERALL_RESULT.Result
    $Color = if ($OverallResult -eq "PASS") { "Green" } else { "Red" }
    
    Write-Host "Overall Result: $OverallResult" -ForegroundColor $Color
    
    # List Failures if any
    if ($OverallResult -ne "PASS") {
        Write-Host "`nFailures:" -ForegroundColor Red
        $XmlContent.REPORT.REQUIREMENTS.REQUIREMENT | Where-Object { $_.OVERALL_RESULT.Result -eq "FAIL" } | ForEach-Object {
            Write-Host "  [-] $($_.Name)" -ForegroundColor Red
        }
    } else {
        Write-Host "All checks passed!" -ForegroundColor Green
    }
} else {
    Write-Error "Report file not found: $ReportXml"
}
