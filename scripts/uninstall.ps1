[CmdletBinding()]
param(
    [string]$CertPath,
    [switch]$KeepCertificate
)

$ErrorActionPreference = 'Stop'

function Resolve-CertThumbprints {
    param(
        [string]$Path
    )

    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path)) {
            throw "Certificate not found: $Path"
        }
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $Path).Path)
        return @($cert.Thumbprint)
    }

    return (Get-ChildItem Cert:\CurrentUser\TrustedPeople |
        Where-Object { $_.Subject -eq 'CN=PhotoGeoExplorer' } |
        Select-Object -ExpandProperty Thumbprint)
}

Get-AppxPackage -Name 'PhotoGeoExplorer' | ForEach-Object {
    Write-Host "Removing package: $($_.PackageFullName)"
    Remove-AppxPackage -Package $_.PackageFullName
}

if (-not $KeepCertificate) {
    $thumbprints = Resolve-CertThumbprints -Path $CertPath
    if ($thumbprints.Count -eq 0) {
        Write-Host "No matching certificate found."
    } else {
        foreach ($thumbprint in $thumbprints) {
            $items = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
                Where-Object { $_.Thumbprint -eq $thumbprint }
            foreach ($item in $items) {
                Write-Host "Removing certificate: $($item.Subject) ($($item.Thumbprint))"
                Remove-Item -Path $item.PSPath
            }
        }
    }
} else {
    Write-Host "Keeping certificate."
}

Write-Host "Done."
