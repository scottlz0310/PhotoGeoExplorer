[CmdletBinding()]
param(
    [string]$MsixPath,
    [string]$CertPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Resolve-ArtifactPath {
    param(
        [string]$Path,
        [string[]]$Patterns,
        [string]$Label
    )

    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) {
            throw "$Label not found: $Path"
        }
        return (Resolve-Path -LiteralPath $Path).Path
    }

    $searchRoots = @($PWD.Path, $PSScriptRoot) | Select-Object -Unique
    foreach ($root in $searchRoots) {
        foreach ($pattern in $Patterns) {
            $item = Get-ChildItem -Path $root -File -Filter $pattern -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
            if ($item) {
                return $item.FullName
            }
        }
    }

    $locations = $searchRoots -join ', '
    throw "$Label not found. Place it under: $locations."
}

$certPath = Resolve-ArtifactPath -Path $CertPath -Patterns @('PhotoGeoExplorer.cer', '*.cer') -Label '証明書 (CER)'
$msixPath = Resolve-ArtifactPath -Path $MsixPath -Patterns @('*.msixbundle', '*.msix') -Label 'MSIX'

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath)
$thumbprint = $cert.Thumbprint
$store = 'Cert:\CurrentUser\TrustedPeople'
$existing = Get-ChildItem $store | Where-Object { $_.Thumbprint -eq $thumbprint }
if (-not $existing) {
    Write-Host "Importing certificate into TrustedPeople: $certPath"
    Import-Certificate -FilePath $certPath -CertStoreLocation $store | Out-Null
} else {
    Write-Host "Certificate already installed."
}

if ($Force) {
    Get-AppxPackage -Name 'PhotoGeoExplorer' | ForEach-Object {
        Write-Host "Removing existing package: $($_.PackageFullName)"
        Remove-AppxPackage -Package $_.PackageFullName
    }
}

Write-Host "Installing MSIX: $msixPath"
Add-AppxPackage -Path $msixPath
Write-Host "Done."
