Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

param(
    [string]$MsixPath,
    [string]$CertificatePath
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

if (-not $CertificatePath) {
    $CertificatePath = Join-Path $scriptDir 'certs\PhotoGeoExplorer_Test.cer'
}

if (-not (Test-Path $CertificatePath)) {
    Write-Host "Certificate not found: $CertificatePath" -ForegroundColor Red
    Write-Host 'Run wack\build-signed-test.ps1 first.' -ForegroundColor Yellow
    exit 1
}

Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

if (-not $MsixPath) {
    $msixPattern = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\PhotoGeoExplorer_*.msix'
    $msix = Get-ChildItem -Path $msixPattern -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $msix) {
        Write-Host 'MSIX package not found. Run wack\build-signed-test.ps1 first.' -ForegroundColor Red
        exit 1
    }

    $MsixPath = $msix.FullName
}

Write-Host "Installing: $MsixPath"
Add-AppxPackage -Path $MsixPath
