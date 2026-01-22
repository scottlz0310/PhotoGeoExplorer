<#
.SYNOPSIS
    msixupload から生成した自己署名済みパッケージをインストールします。

.DESCRIPTION
    build-from-upload.ps1 で生成した署名済み MSIX パッケージをインストールします。
    複数バージョンが存在する場合は最新版を自動選択します。

.PARAMETER MsixPath
    インストールする MSIX ファイルのパス。省略時は最新版を自動検索。

.PARAMETER CertificatePath
    証明書ファイルのパス。省略時はデフォルトパスを使用。

.EXAMPLE
    .\install-from-upload.ps1
    最新の署名済みパッケージをインストールします。
#>

param(
    [string]$MsixPath,
    [string]$CertificatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# === 1. 証明書の確認とインポート ===
if (-not $CertificatePath) {
    $CertificatePath = Join-Path $scriptDir 'certs\PhotoGeoExplorer_Test.cer'
}

if (-not (Test-Path $CertificatePath)) {
    Write-Host "Certificate not found: $CertificatePath" -ForegroundColor Red
    Write-Host 'Run wack\build-from-upload.ps1 first.' -ForegroundColor Yellow
    exit 1
}

Write-Host '=== Importing certificate ===' -ForegroundColor Cyan
Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' -ErrorAction SilentlyContinue | Out-Null
Write-Host "Certificate imported: $CertificatePath" -ForegroundColor Green

# === 2. MSIX パッケージの検索 ===
if (-not $MsixPath) {
    Write-Host '=== Searching for latest signed package ===' -ForegroundColor Cyan

    $appPackagesDir = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages'
    $signedPattern = Join-Path $appPackagesDir 'PhotoGeoExplorer_*_SignedTest\PhotoGeoExplorer_*_signed.msix'

    $packages = Get-ChildItem -Path $signedPattern -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.BaseName -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1') } -Descending

    if ($null -eq $packages -or @($packages).Count -eq 0) {
        Write-Host "Signed package not found in: $appPackagesDir" -ForegroundColor Red
        Write-Host 'Run wack\build-from-upload.ps1 first.' -ForegroundColor Yellow
        exit 1
    }

    $MsixPath = ($packages | Select-Object -First 1).FullName
}

if (-not (Test-Path $MsixPath)) {
    Write-Host "MSIX not found: $MsixPath" -ForegroundColor Red
    exit 1
}

$version = [System.IO.Path]::GetFileName($MsixPath) -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1'
Write-Host "Package: $MsixPath" -ForegroundColor Green
Write-Host "Version: $version" -ForegroundColor Green

# === 3. 既存パッケージの確認 ===
Write-Host '=== Checking existing installation ===' -ForegroundColor Cyan

$existingPackage = Get-AppxPackage -Name 'scottlz0310.PhotoGeoExplorer' -ErrorAction SilentlyContinue
if ($existingPackage) {
    Write-Host "Existing package found: $($existingPackage.Version)" -ForegroundColor Yellow
    Write-Host 'Removing existing package...' -ForegroundColor Yellow
    Remove-AppxPackage -Package $existingPackage.PackageFullName
    Write-Host 'Existing package removed.' -ForegroundColor Green
}

# === 4. インストール ===
Write-Host '=== Installing package ===' -ForegroundColor Cyan
Write-Host "Installing: $MsixPath"

Add-AppxPackage -Path $MsixPath

# === 5. 確認 ===
$installed = Get-AppxPackage -Name 'scottlz0310.PhotoGeoExplorer' -ErrorAction SilentlyContinue
if ($installed) {
    Write-Host ''
    Write-Host '=== Installation completed ===' -ForegroundColor Green
    Write-Host "Installed version: $($installed.Version)" -ForegroundColor Cyan
    Write-Host "Install location: $($installed.InstallLocation)" -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'To verify AppxManifest Resources:' -ForegroundColor Yellow
    Write-Host "  [xml]`$xml = Get-Content `"$($installed.InstallLocation)\AppxManifest.xml`"; `$xml.Package.Resources.Resource | ForEach-Object { `$_.Language }" -ForegroundColor White
    Write-Host ''
    Write-Host 'To launch the app:' -ForegroundColor Yellow
    Write-Host '  Start-Process shell:AppsFolder\scottlz0310.PhotoGeoExplorer_r99jq8jxntmym!App' -ForegroundColor White
} else {
    Write-Host 'Installation may have failed. Please check the error message above.' -ForegroundColor Red
    exit 1
}
