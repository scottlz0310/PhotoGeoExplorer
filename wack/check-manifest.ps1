# Extract and check AppxManifest.xml from MSIX package
$tempDir = "$env:TEMP\PhotoGeoExplorer_Extract"

# Clean up old extraction
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}

# Find the MSIX file
$msix = Get-ChildItem "PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\PhotoGeoExplorer_*.msix" | Select-Object -First 1

if (-not $msix) {
    Write-Host "ERROR: MSIX file not found" -ForegroundColor Red
    exit 1
}

Write-Host "Extracting: $($msix.FullName)" -ForegroundColor Cyan

# Extract MSIX
Expand-Archive -Path $msix.FullName -DestinationPath $tempDir -Force

# Read and display AppxManifest.xml
$manifestPath = Join-Path $tempDir "AppxManifest.xml"
if (Test-Path $manifestPath) {
    Write-Host "`nAppxManifest.xml contents:" -ForegroundColor Green
    Get-Content $manifestPath
} else {
    Write-Host "ERROR: AppxManifest.xml not found in package" -ForegroundColor Red
}
