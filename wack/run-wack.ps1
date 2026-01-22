# WACK (Windows App Certification Kit) Test Runner
# This script automatically runs WACK tests on the latest MSIX package

# Check for administrator privileges (WACK requires admin)
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host '管理者権限で再起動します (UAC の許可が必要です)…' -ForegroundColor Yellow
    $scriptPath = $MyInvocation.MyCommand.Path
    Start-Process -FilePath 'powershell.exe' -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`"" -Verb RunAs
    exit
}

# Navigate to project root (parent of wack folder)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
Set-Location $projectRoot

# Configuration
$wackDir = Join-Path $projectRoot 'wack'
$report = Join-Path $wackDir 'wack-report.xml'
$wackProfile = Join-Path $wackDir 'profile'
$wackLocalAppData = Join-Path $wackProfile 'AppData\\Local'
$wackRoamingAppData = Join-Path $wackProfile 'AppData\\Roaming'
$wackTemp = Join-Path $wackLocalAppData 'Temp'
$wackLogs = Join-Path $wackDir 'logs'

# Use repo-local temp/log paths to avoid TAEF log creation failures on non-ASCII user profiles.
New-Item -ItemType Directory -Path $wackTemp, $wackLogs, $wackLocalAppData, $wackRoamingAppData -Force | Out-Null
$appVerifierLogs = Join-Path $wackLocalAppData 'AppVerifierLogs'
New-Item -ItemType Directory -Path $appVerifierLogs -Force | Out-Null
$env:TEMP = $wackTemp
$env:TMP = $wackTemp
$env:USERPROFILE = $wackProfile
$env:LOCALAPPDATA = $wackLocalAppData
$env:APPDATA = $wackRoamingAppData
$env:HOMEDRIVE = Split-Path $wackProfile -Qualifier
$env:HOMEPATH = $wackProfile.Substring($env:HOMEDRIVE.Length)
$env:TAEF_LOG_DIR = $wackLogs
$env:TAEF_LOG_ROOT = $wackLogs
$env:TAEF_LOG_PATH = $wackLogs
# Find appcert.exe - try environment variable first, then common paths
$appcert = $env:WACK_APPCERT_PATH
if (-not $appcert -or -not (Test-Path $appcert)) {
    $commonPaths = @(
        "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe",
        "C:\Program Files\Windows Kits\10\App Certification Kit\appcert.exe"
    )

    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            $appcert = $path
            break
        }
    }
}

if (-not $appcert -or -not (Test-Path $appcert)) {
    Write-Host "ERROR: appcert.exe not found!" -ForegroundColor Red
    Write-Host "Please set WACK_APPCERT_PATH environment variable or install Windows SDK." -ForegroundColor Yellow
    Write-Host "Example: `$env:WACK_APPCERT_PATH = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'" -ForegroundColor Gray
    exit 1
}

# Find the latest MSIX bundle
$packagePattern = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\*.msixbundle'
$package = Get-ChildItem -Path $packagePattern -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $package) {
    Write-Host "ERROR: No MSIX bundle found. Please build the package first." -ForegroundColor Red
    Write-Host "Run: dotnet publish PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX" -ForegroundColor Yellow
    exit 1
}

Write-Host "Project root: $projectRoot" -ForegroundColor Gray
Write-Host "WACK tool: $appcert" -ForegroundColor Gray
Write-Host "Package: $package" -ForegroundColor Gray
Write-Host "Report: $report" -ForegroundColor Gray

# Auto-clean old report
if (Test-Path $report) {
    Write-Host "`nRemoving old report..." -ForegroundColor Yellow
    Remove-Item $report -Force
}

Write-Host "`nRunning WACK test..." -ForegroundColor Cyan
Write-Host "This may take several minutes. Please wait..." -ForegroundColor Gray

& $appcert test -appxpackagepath $package -reportoutputpath $report 2>&1 | Tee-Object -Variable output

$exitCode = $LASTEXITCODE
Write-Host "`nExit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { 'Green' } else { 'Red' })

if (Test-Path $report) {
    Write-Host "Report saved to: $report" -ForegroundColor Green
    Write-Host "`nRun 'wack\analyze-wack.ps1' to see detailed results." -ForegroundColor Cyan
} else {
    Write-Host "WARNING: Report file not created!" -ForegroundColor Red
}

exit $exitCode
