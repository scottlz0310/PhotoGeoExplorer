# PhotoGeoExplorer

PhotoGeoExplorer is a Windows desktop app for viewing photo locations on a map.
It targets Windows 10/11 and uses WinUI 3 and WebView2 to render a local Leaflet
map.

## Status

This repository is in early development. The UI shell and map loading are in
place, but the file browser, EXIF extraction, and map plotting logic are still
under active development.

## Current Features

- WinUI 3 single-window layout with File Browser, Image Preview, and Map panes.
- WebView2 loads a local `wwwroot/index.html` map page.
- Application logging to `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log`.

## Planned Features

- Folder navigation and file list with thumbnails.
- EXIF/GPS extraction and map markers.
- Image preview controls (zoom/pan).
- Offline-friendly map tile caching.

## Tech Stack

- .NET 10 / C#  (WinUI 3, Windows App SDK)
- WebView2 + Leaflet for map rendering
- MetadataExtractor for EXIF metadata
- SixLabors.ImageSharp for thumbnails and image processing

## Prerequisites

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2026 with WinUI 3 workload (optional for IDE usage)
- WebView2 Runtime

## Build

```powershell
dotnet restore PhotoGeoExplorer.sln
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

## Run

```powershell
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64
```

## Formatting and Quality Gates

```powershell
dotnet format --verify-no-changes PhotoGeoExplorer.sln
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64 -p:TreatWarningsAsErrors=true -p:AnalysisLevel=latest
```

Optional hooks:

```powershell
lefthook install
```

## Release Artifacts

The release workflow builds an unsigned MSIX installer for `win-x64` on tag
pushes (e.g., `v0.1.0`).

## License

See `LICENSE` if added. Otherwise, this project is currently unlicensed.
