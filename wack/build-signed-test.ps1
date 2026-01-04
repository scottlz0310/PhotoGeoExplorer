Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

param(
    [string]$CertificateSubject = 'CN=PhotoGeoExplorer Test',
    [string]$CertificateName = 'PhotoGeoExplorer_Test',
    [string]$CertificatePassword,
    [switch]$ForceNewCertificate
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$certDir = Join-Path $scriptDir 'certs'
New-Item -ItemType Directory -Path $certDir -Force | Out-Null

$pfxPath = Join-Path $certDir "$CertificateName.pfx"
$cerPath = Join-Path $certDir "$CertificateName.cer"

if (-not $CertificatePassword) {
    $securePassword = Read-Host 'Enter PFX password' -AsSecureString
} else {
    $securePassword = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
}

if ($ForceNewCertificate -or -not (Test-Path $pfxPath)) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertificateSubject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
}

Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

$plainPassword = ConvertFrom-SecureString $securePassword -AsPlainText

$publishArgs = @(
    'publish',
    (Join-Path $projectRoot 'PhotoGeoExplorer\PhotoGeoExplorer.csproj'),
    '-c', 'Release',
    '-p:Platform=x64',
    '-p:WindowsPackageType=MSIX',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxBundle=Never',
    '-p:AppxPackageSigningEnabled=true',
    "-p:PackageCertificateKeyFile=$pfxPath",
    "-p:PackageCertificatePassword=$plainPassword",
    '-p:AppxSymbolPackageEnabled=false'
)

& dotnet @publishArgs

$msixPattern = Join-Path $projectRoot 'PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\PhotoGeoExplorer_*.msix'
$msix = Get-ChildItem -Path $msixPattern -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($msix) {
    Write-Host "Signed package: $($msix.FullName)"
} else {
    Write-Host 'Signed package not found.' -ForegroundColor Yellow
}
