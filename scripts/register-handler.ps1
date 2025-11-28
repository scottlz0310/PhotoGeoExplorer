# PhotoGeoPreviewHandler Registration Script
# This script registers the COM Preview Handler with Windows Explorer
# Must be run as Administrator

param(
    [switch]$Unregister,
    [string]$DllPath = $null
)

$ErrorActionPreference = "Stop"

# CLSID and ProgID (must match PreviewHandler.cs)
$CLSID = "{E7C3A7D9-4B5A-4F2E-8C1D-9B6E5F3A2D8C}"
$ProgID = "PhotoGeoPreviewHandler.PreviewHandler"
$HandlerName = "PhotoGeo Preview Handler"

# Supported file extensions
$Extensions = @(
    ".jpg",
    ".jpeg",
    ".png",
    ".bmp",
    ".gif",
    ".heic",
    ".heif"
)

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-DllPath {
    if ($DllPath) {
        return $DllPath
    }

    # Try to find the DLL in common build locations
    $scriptDir = Split-Path -Parent $PSScriptRoot
    $possiblePaths = @(
        "$scriptDir\PhotoGeoPreviewHandler\bin\Debug\net10.0-windows\PhotoGeoPreviewHandler.dll",
        "$scriptDir\PhotoGeoPreviewHandler\bin\Release\net10.0-windows\PhotoGeoPreviewHandler.dll",
        "$scriptDir\bin\Debug\net10.0-windows\PhotoGeoPreviewHandler.dll",
        "$scriptDir\bin\Release\net10.0-windows\PhotoGeoPreviewHandler.dll"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "Could not find PhotoGeoPreviewHandler.dll. Please build the project first or specify -DllPath"
}

function Register-Handler {
    param([string]$DllPath)

    Write-Host "Registering PhotoGeo Preview Handler..." -ForegroundColor Cyan
    Write-Host "DLL Path: $DllPath" -ForegroundColor Gray

    if (-not (Test-Path $DllPath)) {
        throw "DLL not found: $DllPath"
    }

    # Register COM server using regsvr32 or .NET COM registration
    Write-Host "Registering COM server..." -ForegroundColor Yellow

    # For .NET assemblies, we use regasm
    $regasm = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    if (-not (Test-Path $regasm)) {
        # Try .NET 5+ approach
        Write-Host "Using ComHost registration..." -ForegroundColor Yellow
        # The DLL should have a .comhost.dll companion if EnableComHosting is set
    }

    # Register Preview Handler CLSID
    $clsidPath = "HKLM:\SOFTWARE\Classes\CLSID\$CLSID"
    New-Item -Path $clsidPath -Force | Out-Null
    Set-ItemProperty -Path $clsidPath -Name "(Default)" -Value $HandlerName
    Set-ItemProperty -Path $clsidPath -Name "DisplayName" -Value $HandlerName
    Set-ItemProperty -Path $clsidPath -Name "AppID" -Value $CLSID

    # InprocServer32
    $inprocPath = "$clsidPath\InprocServer32"
    New-Item -Path $inprocPath -Force | Out-Null
    Set-ItemProperty -Path $inprocPath -Name "(Default)" -Value $DllPath
    Set-ItemProperty -Path $inprocPath -Name "ThreadingModel" -Value "Apartment"

    # Register ProgID
    $progIdPath = "HKLM:\SOFTWARE\Classes\$ProgID"
    New-Item -Path $progIdPath -Force | Out-Null
    Set-ItemProperty -Path $progIdPath -Name "(Default)" -Value $HandlerName

    $progIdClsidPath = "$progIdPath\CLSID"
    New-Item -Path $progIdClsidPath -Force | Out-Null
    Set-ItemProperty -Path $progIdClsidPath -Name "(Default)" -Value $CLSID

    # Register for each file extension
    foreach ($ext in $Extensions) {
        Write-Host "Registering for $ext..." -ForegroundColor Gray

        # Register shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f} (IPreviewHandler)
        $extPath = "HKLM:\SOFTWARE\Classes\$ext\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}"
        New-Item -Path $extPath -Force | Out-Null
        Set-ItemProperty -Path $extPath -Name "(Default)" -Value $CLSID
    }

    # Add to approved shell extensions
    $approvedPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"
    Set-ItemProperty -Path $approvedPath -Name $CLSID -Value $HandlerName

    Write-Host "Registration complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: You must restart Windows Explorer for changes to take effect:" -ForegroundColor Yellow
    Write-Host "  Stop-Process -Name explorer -Force" -ForegroundColor Cyan
}

function Unregister-Handler {
    Write-Host "Unregistering PhotoGeo Preview Handler..." -ForegroundColor Cyan

    # Remove from approved shell extensions
    $approvedPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"
    Remove-ItemProperty -Path $approvedPath -Name $CLSID -ErrorAction SilentlyContinue

    # Remove file extension associations
    foreach ($ext in $Extensions) {
        Write-Host "Unregistering from $ext..." -ForegroundColor Gray
        $extPath = "HKLM:\SOFTWARE\Classes\$ext\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}"
        Remove-Item -Path $extPath -Force -ErrorAction SilentlyContinue
    }

    # Remove ProgID
    $progIdPath = "HKLM:\SOFTWARE\Classes\$ProgID"
    Remove-Item -Path $progIdPath -Recurse -Force -ErrorAction SilentlyContinue

    # Remove CLSID
    $clsidPath = "HKLM:\SOFTWARE\Classes\CLSID\$CLSID"
    Remove-Item -Path $clsidPath -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "Unregistration complete!" -ForegroundColor Green
    Write-Host "Please restart Windows Explorer for changes to take effect." -ForegroundColor Yellow
}

# Main execution
try {
    if (-not (Test-Administrator)) {
        Write-Error "This script must be run as Administrator!"
        Write-Host "Please right-click and select 'Run as Administrator'" -ForegroundColor Yellow
        exit 1
    }

    if ($Unregister) {
        Unregister-Handler
    }
    else {
        $dllPath = Get-DllPath
        Register-Handler -DllPath $dllPath
    }
}
catch {
    Write-Error "An error occurred: $_"
    exit 1
}
