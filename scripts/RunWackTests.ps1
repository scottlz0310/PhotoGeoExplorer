<#
.SYNOPSIS
    Runs Windows App Certification Kit (WACK) validation.
    Restored from previous working implementation (wack/run-wack.ps1).

.DESCRIPTION
    Automates the WACK test process using environment isolation to prevent TAEF logging errors.
    1. Sets up temporary environment (TEMP, APPDATA, USERPROFILE) to isolate WACK.
    2. Finds the latest signed MSIX/Bundle.
    3. Executes WACK test (appcert.exe) pointing to the package file.
#>

param(
    [string]$OutputDir
)

# --- 0. Check for Administrator Privileges ---
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "管理者権限で再起動します..." -ForegroundColor Yellow
    $argsList = "-NoProfile -ExecutionPolicy Bypass -NoExit -File `"$($MyInvocation.MyCommand.Path)`""
    Start-Process powershell -ArgumentList $argsList -Verb RunAs
    exit
}

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptDir

# --- 1. Environment Isolation (CRITICAL) ---
# Previous implementation used this to avoid TAEF log creation failures on non-ASCII user profiles
# and general flakiness.
$WackWorkDir = Join-Path $ScriptDir 'wack_work'
$WackProfile = Join-Path $WackWorkDir 'profile'
$WackLocalAppData = Join-Path $WackProfile 'AppData\Local'
$WackRoamingAppData = Join-Path $WackProfile 'AppData\Roaming'
$WackTemp = Join-Path $WackLocalAppData 'Temp'
$WackLogs = Join-Path $WackWorkDir 'logs'

Write-Host "Setting up WACK environment isolation..." -ForegroundColor Gray
New-Item -ItemType Directory -Path $WackTemp, $WackLogs, $WackLocalAppData, $WackRoamingAppData -Force | Out-Null
$AppVerifierLogs = Join-Path $WackLocalAppData 'AppVerifierLogs'
New-Item -ItemType Directory -Path $AppVerifierLogs -Force | Out-Null

$env:TEMP = $WackTemp
$env:TMP = $WackTemp
$env:USERPROFILE = $WackProfile
$env:LOCALAPPDATA = $WackLocalAppData
$env:APPDATA = $WackRoamingAppData
$env:HOMEDRIVE = Split-Path $WackProfile -Qualifier
$env:HOMEPATH = $WackProfile.Substring($env:HOMEDRIVE.Length)
$env:TAEF_LOG_DIR = $WackLogs
$env:TAEF_LOG_ROOT = $WackLogs
$env:TAEF_LOG_PATH = $WackLogs

# --- 2. Locate WACK Tool ---
$AppCert = $env:WACK_APPCERT_PATH
if (-not $AppCert -or -not (Test-Path $AppCert)) {
    $CommonPaths = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe",
        "${env:ProgramFiles}\Windows Kits\10\App Certification Kit\appcert.exe"
    )
    foreach ($path in $CommonPaths) {
        if (Test-Path $path) {
            $AppCert = $path
            break
        }
    }
}

if (-not $AppCert) {
    throw "appcert.exe not found! Please install Windows SDK."
}
Write-Host "Using WACK: $AppCert" -ForegroundColor Gray

# --- 3. Locate Target Package ---
# Search order:
# 1. AppPackages/LocalDebug_*/PhotoGeoExplorer_LocalDebug.msix (Created by DevInstall.ps1)
# 2. AppPackages/**/*.msixbundle (VS Build)
# 3. AppPackages/**/*.msix (VS Build)

$AppPackagesDir = Join-Path $ProjectRoot 'PhotoGeoExplorer\AppPackages'

# Priority 1: Signed LocalDebug package
$Candidate = Get-ChildItem -Path $AppPackagesDir -Recurse -Filter "PhotoGeoExplorer_LocalDebug.msix" -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1

# Priority 2: Any recent bundle or msix
if (-not $Candidate) {
    $Candidate = Get-ChildItem -Path $AppPackagesDir -Recurse -Include *.msixbundle, *.msix |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if (-not $Candidate) {
    throw "No MSIX package found in '$AppPackagesDir'. Run 'scripts\DevInstall.ps1 -Build' first."
}

$PackagePath = $Candidate.FullName
Write-Host "Testing Package: $PackagePath" -ForegroundColor Cyan

# --- 4. Prepare Report Path ---
if (-not $OutputDir) { $OutputDir = Join-Path $ScriptDir 'wack_reports' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$ReportXml = Join-Path $OutputDir "WACK-$Timestamp.xml"

Write-Host "Report Path: $ReportXml" -ForegroundColor Gray

# --- 5. Run WACK ---
Write-Host "`nRunning WACK test (File Mode)..." -ForegroundColor Yellow
Write-Host "This command mimics the previous working implementation." -ForegroundColor Gray

# Use Call Operator & for the path with spaces
& $AppCert test -appxpackagepath "$PackagePath" -reportoutputpath "$ReportXml" 2>&1 | Tee-Object -Variable WackOutput

$ExitCode = $LASTEXITCODE
Write-Host "`nWACK Exit Code: $ExitCode" -ForegroundColor $(if ($ExitCode -eq 0) { 'Green' } else { 'Red' })

if (Test-Path $ReportXml) {
    Write-Host "Report saved: $ReportXml" -ForegroundColor Green

    # Run the analyzer
    Write-Host "`nAnalyzing Results..." -ForegroundColor Cyan
    & "$PSScriptRoot\AnalyzeWackReport.ps1" -ReportPath $ReportXml
} else {
    Write-Error "WACK failed to generate a report."
}

exit $ExitCode
