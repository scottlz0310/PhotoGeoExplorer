<#
.SYNOPSIS
    Builds (optional), Signs, and Installs the app for local development/testing.

.DESCRIPTION
    Streamlines the workflow of testing the Store-ready build locally.
    1. (Optional) Builds the project using Store configuration.
    2. Takes the resulting msixupload (Store artifact).
    3. Re-signs it with a local self-signed certificate (generated automatically if missing).
    4. Installs the certificate to Trusted People.
    5. Installs the package.

.PARAMETER Build
    If set, runs dotnet publish before signing.

.PARAMETER Clean
    If set, cleans previous artifacts in temp folders and removes the installed app/certificate.
#>
param(
    [switch]$Build,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$certDir = Join-Path $scriptDir 'certs'
$workDir = Join-Path $scriptDir 'temp'
$bundleArtifactsDir = Join-Path $projectRoot 'BundleArtifacts'
$appPackagesDir = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages'
$certSubject = "CN=PhotoGeoExplorer_LocalDebug"
$pkgName = "scottlz0310.PhotoGeoExplorer"

# --- Clean / Uninstall ---
if ($Clean) {
    Write-Host "Cleaning up artifacts..." -ForegroundColor Yellow
    if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }
    if (Test-Path $bundleArtifactsDir) { Remove-Item -Recurse -Force $bundleArtifactsDir }

    Write-Host "Uninstalling App ($pkgName)..." -ForegroundColor Yellow
    Get-AppxPackage $pkgName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue

    Write-Host "Removing Certificate ($certSubject)..." -ForegroundColor Yellow
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPeople", "LocalMachine"
    $store.Open("ReadWrite")
    try {
        $certs = $store.Certificates | Where-Object { $_.Subject -eq $certSubject }
        foreach ($c in $certs) {
            Write-Host "Removing from LocalMachine\TrustedPeople: $($c.Thumbprint)"
            $store.Remove($c)
        }
    } finally {
        $store.Close()
    }

    # Also check CurrentUser just in case
    $storeUser = New-Object System.Security.Cryptography.X509Certificates.X509Store "TrustedPeople", "CurrentUser"
    $storeUser.Open("ReadWrite")
    try {
        $certsUser = $storeUser.Certificates | Where-Object { $_.Subject -eq $certSubject }
        foreach ($c in $certsUser) {
             Write-Host "Removing from CurrentUser\TrustedPeople: $($c.Thumbprint)"
             $storeUser.Remove($c)
        }
    } finally {
        $storeUser.Close()
    }

    Write-Host "Cleanup complete." -ForegroundColor Green
    # If only clean was requested (and not build), exit.
    # But usually clean is used combined with build or standalone.
    # For now, let's treat -Clean as "Clean resources".
    # If the user wants to Clean AND Build, they can pass -Clean -Build.

    if (-not $Build) { exit }
}
$appPackagesDir = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages'
$certName = 'PhotoGeoExplorer_LocalDebug'
$certSubject = "CN=PhotoGeoExplorer Local Debug"

# --- Clean ---
if ($Clean) {
    Write-Host "Cleaning up temp folders..." -ForegroundColor Cyan
    if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }
    if (Test-Path $certDir) { Remove-Item -Recurse -Force $certDir }
}

# --- Build ---
if ($Build) {
    Write-Host "Building project for Store Upload..." -ForegroundColor Cyan
    Push-Location $projectRoot
    try {
        dotnet publish PhotoGeoExplorer\PhotoGeoExplorer.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload -p:AppxBundle=Always -p:AppxBundlePlatforms=x64 -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    }
    finally {
        Pop-Location
    }
}

# --- Find MSIX Upload ---
Write-Host "Searching for latest msixupload..." -ForegroundColor Cyan
if (-not (Test-Path $appPackagesDir)) {
    throw "AppPackages directory not found at $appPackagesDir. Run with -Build switch."
}

$uploads = Get-ChildItem -Path $appPackagesDir -Recurse -Filter "*.msixupload" |
           Sort-Object { [version]($_.BaseName -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1') } -Descending

$latestUpload = $uploads | Select-Object -First 1

if (-not $latestUpload) {
    throw "No msixupload found in $appPackagesDir. Run with -Build switch."
}
Write-Host "Found: $($latestUpload.Name)" -ForegroundColor Green
$version = $latestUpload.BaseName -replace '^PhotoGeoExplorer_(\d+\.\d+\.\d+\.\d+).*', '$1'

# --- Prepare Certificate ---
if (-not (Test-Path $certDir)) { New-Item -ItemType Directory -Path $certDir -Force | Out-Null }
$pfxPath = Join-Path $certDir "$certName.pfx"
$cerPath = Join-Path $certDir "$certName.cer"
$securePassword = ConvertTo-SecureString "password" -AsPlainText -Force

if (-not (Test-Path $pfxPath)) {
    Write-Host "Creating self-signed certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(1)

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
} else {
    Write-Host "Using existing certificate: $pfxPath" -ForegroundColor Green
}

# Ensure CER matches PFX
$certObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $pfxPath, "password"
$bytes = $certObj.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
[System.IO.File]::WriteAllBytes($cerPath, $bytes)

# Import PFX to CurrentUser\My so SignTool can use it
$imported = Import-PfxCertificate -FilePath $pfxPath -Password $securePassword -CertStoreLocation 'Cert:\CurrentUser\My'
if (-not $imported) { throw "Failed to import PFX to personal store." }
$thumbprint = $imported.Thumbprint

# --- Unpack, Sign, Repack ---
Write-Host "Repacking and Signing..." -ForegroundColor Cyan
if (Test-Path $workDir) { Remove-Item -Recurse -Force $workDir }
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

$extractDir = Join-Path $workDir "upload"
Expand-Archive -Path $latestUpload.FullName -DestinationPath $extractDir -Force

# Note: The Upload file contains the bundle.
$bundle = Get-ChildItem -Path $extractDir -Filter "*.msixbundle" | Select-Object -First 1
if (-not $bundle) { throw "No msixbundle found in upload." }

$bundleExtractDir = Join-Path $workDir "bundle"
Expand-Archive -Path $bundle.FullName -DestinationPath $bundleExtractDir -Force

# Note: The bundle contains the msix.
$msix = Get-ChildItem -Path $bundleExtractDir -Filter "*.msix" | Select-Object -First 1
if (-not $msix) { throw "No msix found in bundle." }

$msixExtractDir = Join-Path $workDir "msix_content"

# Find SDK Tools
$sdkBinDir = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
             Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
             Sort-Object { [version]$_.Name } -Descending |
             Select-Object -First 1

if (-not $sdkBinDir) { throw "Windows SDK not found." }

$makeAppx = Join-Path $sdkBinDir.FullName 'x64\makeappx.exe'
$signTool = Join-Path $sdkBinDir.FullName 'x64\signtool.exe'

Write-Host "Unpacking MSIX..." -ForegroundColor Gray
& $makeAppx unpack /p $msix.FullName /d $msixExtractDir /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakeAppx unpack failed" }

# Update Publisher in AppxManifest.xml
$manifestPath = Join-Path $msixExtractDir 'AppxManifest.xml'
[xml]$xml = Get-Content $manifestPath
$originalPublisher = $xml.Package.Identity.Publisher
Write-Host "Updating Publisher: $originalPublisher -> $certSubject" -ForegroundColor Yellow
$xml.Package.Identity.Publisher = $certSubject
$xml.Save($manifestPath)

# Pack
$signedPkgDir = Join-Path $appPackagesDir "LocalDebug_$version"
if (-not (Test-Path $signedPkgDir)) { New-Item -ItemType Directory -Path $signedPkgDir -Force | Out-Null }

$signedMsix = Join-Path $signedPkgDir "PhotoGeoExplorer_LocalDebug.msix"
Write-Host "Packing signed MSIX to $signedMsix..." -ForegroundColor Gray
& $makeAppx pack /d $msixExtractDir /p $signedMsix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed" }

# Sign
Write-Host "Signing..." -ForegroundColor Gray
& $signTool sign /fd SHA256 /sha1 $thumbprint /t http://timestamp.digicert.com $signedMsix
if ($LASTEXITCODE -ne 0) { throw "SignTool failed" }

# --- Install ---
Write-Host "Installing Certificate..." -ForegroundColor Cyan

# 1. CurrentUser\TrustedPeople (Basic requirement)
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' -ErrorAction SilentlyContinue | Out-Null

# 2. LocalMachine\TrustedPeople (Required for runFullTrust apps on many systems)
# Check if running as Admin
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isAdmin) {
    Write-Host "Running as Admin: Importing to LocalMachine\TrustedPeople..." -ForegroundColor Cyan
    Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    Write-Host "Imported to LocalMachine." -ForegroundColor Green
} else {
    # Check if already exists in Machine store
    $machineCert = Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $thumbprint }

    if (-not $machineCert) {
        Write-Host "Certificate missing from LocalMachine Store (Required for runFullTrust)." -ForegroundColor Yellow
        Write-Host "Launching Admin prompt to install certificate... Please click 'Yes' on UAC." -ForegroundColor Yellow

        $installCmd = "Import-Certificate -FilePath '$cerPath' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'"
        $proc = Start-Process powershell -ArgumentList "-NoProfile -Command $installCmd" -Verb RunAs -PassThru -Wait

        if ($proc.ExitCode -eq 0) {
            Write-Host "Successfully imported to LocalMachine." -ForegroundColor Green
        } else {
            Write-Warning "Failed to import to LocalMachine. Installation may fail with 0x800B0109."
        }
    } else {
        Write-Host "Certificate already present in LocalMachine." -ForegroundColor Green
    }
}

Write-Host "Installing App..." -ForegroundColor Cyan
# ForceUpdateFromAnyVersion allows installing over the Store version or previous debug versions
Add-AppxPackage -Path $signedMsix -ForceUpdateFromAnyVersion
if ($LASTEXITCODE -ne 0) { throw "Installation failed" }

Write-Host "Done! App installed successfully." -ForegroundColor Green

# Cleanup
Remove-Item -Recurse -Force $workDir -ErrorAction SilentlyContinue
