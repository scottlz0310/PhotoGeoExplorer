# PhotoGeoPreviewHandler Unregistration Script
# This script unregisters the COM Preview Handler from Windows Explorer
# Must be run as Administrator

$ErrorActionPreference = "Stop"

# Simply call the main registration script with -Unregister flag
& "$PSScriptRoot\register-handler.ps1" -Unregister
