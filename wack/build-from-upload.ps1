<#
.SYNOPSIS
    Store 提出用 msixupload から自己署名済みテストパッケージを生成します。

.DESCRIPTION
    PhotoGeoExplorer\AppPackages 配下の msixupload ファイル（複数存在する場合は最新版）を
    展開し、自己署名を付与してローカルテスト用の MSIX パッケージを生成します。

.PARAMETER CertificateSubject
    自己署名証明書のサブジェクト名。デフォルトは 'CN=PhotoGeoExplorer Test'。

.PARAMETER CertificateName
    証明書ファイルの基本名。デフォルトは 'PhotoGeoExplorer_Test'。

.PARAMETER CertificatePassword
    PFX ファイルのパスワード。省略時は対話的に入力を求めます。

.PARAMETER ForceNewCertificate
    既存の証明書があっても新規作成します。

.EXAMPLE
    .\build-from-upload.ps1
    最新の msixupload から自己署名済みパッケージを生成します。
#>

param(
    [string]$CertificateSubject = 'CN=PhotoGeoExplorer Test',
    [string]$CertificateName = 'PhotoGeoExplorer_Test',
    [string]$CertificatePassword,
    [switch]$ForceNewCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$certDir = Join-Path $scriptDir 'certs'
$workDir = Join-Path $scriptDir 'temp'

# クリーンアップ
if (Test-Path $workDir) {
    Remove-Item -Recurse -Force $workDir
}
New-Item -ItemType Directory -Path $certDir -Force | Out-Null
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

# === 1. 最新の msixupload を検索 ===
Write-Host '=== Searching for latest msixupload ===' -ForegroundColor Cyan

$appPackagesDir = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages'

# 複数のパターンを検索（_Test サブフォルダ内、または直接 AppPackages 配下）
$uploads = @()
$patterns = @(
    (Join-Path $appPackagesDir 'PhotoGeoExplorer_*_Test\PhotoGeoExplorer_*_bundle.msixupload'),
    (Join-Path $appPackagesDir 'PhotoGeoExplorer_*_bundle.msixupload')
)

foreach ($pattern in $patterns) {
    $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    if ($found) {
        $uploads += $found
    }
}

$uploads = $uploads | Sort-Object { [version]($_.BaseName -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1') } -Descending

if ($null -eq $uploads -or @($uploads).Count -eq 0) {
    Write-Host "msixupload not found in: $appPackagesDir" -ForegroundColor Red
    Write-Host 'Run the following command first:' -ForegroundColor Yellow
    Write-Host 'dotnet publish .\PhotoGeoExplorer\PhotoGeoExplorer.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always -p:AppxBundlePlatforms=x64 -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false'
    exit 1
}

$latestUpload = $uploads | Select-Object -First 1
$version = $latestUpload.BaseName -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1'

Write-Host "Found: $($latestUpload.FullName)" -ForegroundColor Green
Write-Host "Version: $version" -ForegroundColor Green

# === 2. msixupload を展開 ===
Write-Host '=== Extracting msixupload ===' -ForegroundColor Cyan

$extractDir = Join-Path $workDir 'upload'
Expand-Archive -Path $latestUpload.FullName -DestinationPath $extractDir -Force

# msixbundle を検索
$bundle = Get-ChildItem -Path $extractDir -Filter '*.msixbundle' | Select-Object -First 1
if (-not $bundle) {
    Write-Host 'msixbundle not found in msixupload.' -ForegroundColor Red
    exit 1
}

Write-Host "Found bundle: $($bundle.Name)" -ForegroundColor Green

# === 3. msixbundle を展開 ===
Write-Host '=== Extracting msixbundle ===' -ForegroundColor Cyan

$bundleExtractDir = Join-Path $workDir 'bundle'
Expand-Archive -Path $bundle.FullName -DestinationPath $bundleExtractDir -Force

# msix を検索
$msix = Get-ChildItem -Path $bundleExtractDir -Filter '*.msix' | Select-Object -First 1
if (-not $msix) {
    Write-Host 'msix not found in msixbundle.' -ForegroundColor Red
    exit 1
}

Write-Host "Found msix: $($msix.Name)" -ForegroundColor Green

# === 4. 証明書の準備 ===
Write-Host '=== Preparing certificate ===' -ForegroundColor Cyan

$pfxPath = Join-Path $certDir "$CertificateName.pfx"
$cerPath = Join-Path $certDir "$CertificateName.cer"

if (-not $CertificatePassword) {
    $securePassword = Read-Host 'Enter PFX password' -AsSecureString
} else {
    $securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
}

if ($ForceNewCertificate -or -not (Test-Path $pfxPath)) {
    Write-Host 'Creating new self-signed certificate...' -ForegroundColor Yellow

    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertificateSubject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    Write-Host "Certificate created: $pfxPath" -ForegroundColor Green
} else {
    Write-Host "Using existing certificate: $pfxPath" -ForegroundColor Green
}

# 証明書をインポート
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' -ErrorAction SilentlyContinue | Out-Null
$imported = Import-PfxCertificate -FilePath $pfxPath -Password $securePassword -CertStoreLocation 'Cert:\CurrentUser\My'
if (-not $imported) {
    Write-Host "Failed to import PFX: $pfxPath" -ForegroundColor Red
    exit 1
}
$thumbprint = $imported.Thumbprint
Write-Host "Certificate thumbprint: $thumbprint" -ForegroundColor Green

# === 5. MSIX を展開して再パッケージ＆署名 ===
Write-Host '=== Repacking and signing msix package ===' -ForegroundColor Cyan

# Windows SDK ツールを検索
$sdkBinDir = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

$makeAppx = Join-Path $sdkBinDir.FullName 'x64\makeappx.exe'
$signTool = Join-Path $sdkBinDir.FullName 'x64\signtool.exe'

if (-not (Test-Path $makeAppx)) {
    Write-Host "MakeAppx.exe not found. Install Windows SDK." -ForegroundColor Red
    exit 1
}

Write-Host "Using MakeAppx: $makeAppx" -ForegroundColor Gray
Write-Host "Using SignTool: $signTool" -ForegroundColor Gray

# 出力ディレクトリ
$outputDir = Join-Path $appPackagesDir "PhotoGeoExplorer_${version}_SignedTest"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# MSIX を展開
$msixExtractDir = Join-Path $workDir 'msix_content'
& $makeAppx unpack /p $msix.FullName /d $msixExtractDir /o
if ($LASTEXITCODE -ne 0) {
    Write-Host 'MakeAppx unpack failed.' -ForegroundColor Red
    exit $LASTEXITCODE
}

# AppxManifest.xml の Publisher を自己署名証明書に合わせて変更
$manifestPath = Join-Path $msixExtractDir 'AppxManifest.xml'
[xml]$manifestXml = Get-Content $manifestPath
$originalPublisher = $manifestXml.Package.Identity.Publisher
$manifestXml.Package.Identity.Publisher = $CertificateSubject
$manifestXml.Save($manifestPath)
Write-Host "Changed Publisher: $originalPublisher -> $CertificateSubject" -ForegroundColor Yellow

# 再パッケージ
$signedMsix = Join-Path $outputDir "PhotoGeoExplorer_${version}_x64_signed.msix"
& $makeAppx pack /d $msixExtractDir /p $signedMsix /o
if ($LASTEXITCODE -ne 0) {
    Write-Host 'MakeAppx pack failed.' -ForegroundColor Red
    exit $LASTEXITCODE
}

# 署名実行
$signArgs = @(
    'sign',
    '/fd', 'SHA256',
    '/sha1', $thumbprint,
    '/t', 'http://timestamp.digicert.com',
    $signedMsix
)

& $signTool @signArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host 'SignTool failed.' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ''
Write-Host '=== Build completed ===' -ForegroundColor Green
Write-Host "Signed package: $signedMsix" -ForegroundColor Cyan
Write-Host ''
Write-Host 'To install, run:' -ForegroundColor Yellow
Write-Host "  .\wack\install-from-upload.ps1" -ForegroundColor White
Write-Host ''

# クリーンアップ（temp フォルダ）
Remove-Item -Recurse -Force $workDir -ErrorAction SilentlyContinue
