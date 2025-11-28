# PhotoGeoPreviewHandler

Windows Explorer Preview Handler for images with embedded geolocation data (EXIF GPS).

## Overview

This project implements a Windows Shell Extension (COM Preview Handler) that displays:
- **Image preview** (top pane)
- **Interactive map** (bottom pane) showing the photo's location via Leaflet + OpenStreetMap
- **Resizable split view** with GridSplitter

When you select an image file in Windows Explorer, the preview pane shows both the image and an interactive map pinpointing where the photo was taken (if GPS data is available).

## Features

- **Multi-format support**: JPEG, PNG, BMP, GIF, and HEIC (environment-dependent)
- **EXIF GPS extraction**: Automatically reads geolocation from image metadata
- **Interactive map**: Powered by Leaflet.js via WebView2
- **Fallback handling**: Shows "Null Island" (0°, 0°) when GPS data is unavailable
- **Modern UI**: WPF-based with adjustable split layout
- **x86/x64 support**: Works on both architectures

## Prerequisites

- **Windows 10/11** (x64 or x86)
- **WebView2 Runtime** ([Download from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/))
- **.NET 10 Runtime** (if not bundled)
- **HEIF Image Extensions** (optional, for HEIC support via Microsoft Store)

## Technology Stack

- **.NET 10** / **C# 14.0**
- **WPF** (Windows Presentation Foundation)
- **WebView2** (Microsoft Edge WebView2)
- **Leaflet.js** (via CDN)
- **MetadataExtractor** (NuGet package for EXIF parsing)
- **COM Interop** (IPreviewHandler, IInitializeWithStream)

## Build Instructions

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/PhotoGeoPreviewHandler.git
   cd PhotoGeoPreviewHandler
   ```

2. Open in Visual Studio 2026:
   ```bash
   start PhotoGeoPreviewHandler.slnx
   ```

3. Restore NuGet packages and build:
   ```bash
   dotnet restore
   dotnet build -c Release
   ```

## Installation

### Register the Preview Handler

Run the provided registration script as **Administrator**:

```powershell
# PowerShell (from scripts/ folder)
.\register-handler.ps1
```

Or use the `.reg` files for manual registration (x86/x64).

### Unregister

```powershell
.\unregister-handler.ps1
```

### Restart Explorer

After registration, restart Windows Explorer:
```powershell
Stop-Process -Name explorer -Force
```

## Usage

1. Open **Windows Explorer**
2. Select an image file (JPEG, PNG, etc.)
3. Enable the **Preview Pane** (View > Preview Pane, or Alt+P)
4. See the image and map displayed side-by-side

## Project Structure

```
PhotoGeoPreviewHandler/
├── PhotoGeoPreviewHandler/       # Main project
│   ├── PreviewHandler.cs         # COM Preview Handler implementation
│   ├── PreviewHandlerControl.xaml # WPF UI layout
│   ├── ExifDataExtractor.cs      # EXIF GPS extraction
│   ├── MapHtmlGenerator.cs       # Leaflet HTML generation
│   └── Resources/
│       └── map-template.html     # HTML template for map
├── scripts/                      # Installation scripts
│   ├── register-handler.ps1
│   └── unregister-handler.ps1
├── docs/                         # Documentation
│   ├── ARCHITECTURE.md
│   ├── TECHSTACK.md
│   ├── ImplementationPlan.md
│   └── TASKS.md
└── README.md                     # This file
```

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md) for development guidelines and [COPILOT_RULES.md](COPILOT_RULES.md) for coding standards.

### Debugging

1. Build in Debug mode
2. Register the handler
3. Attach Visual Studio to `explorer.exe` process
4. Set breakpoints in your code
5. Select an image file in Explorer to trigger the handler

## Roadmap

### Phase 1 - MVP (Current)
- [x] Basic COM Preview Handler
- [x] WPF UI with split layout
- [x] EXIF GPS extraction
- [x] Leaflet map integration
- [ ] Registration scripts
- [ ] Multi-format testing

### Phase 2 - Enhancements
- [ ] Persist split ratio settings
- [ ] Reverse geocoding (location names)
- [ ] Performance optimizations
- [ ] Error handling UI improvements

## Known Limitations

- **HEIC support** requires Windows HEIF codecs (may require additional setup)
- **WebView2 Runtime** must be pre-installed
- **Large images** may take time to load in preview
- **GPS data** must be embedded in EXIF metadata

## License

[MIT License](LICENSE)

## Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting pull requests.

## Troubleshooting

### Preview not showing
- Verify WebView2 Runtime is installed
- Check Windows Event Viewer for errors
- Restart Explorer after registration

### Map shows "Null Island"
- The image has no GPS data in EXIF
- Use a tool like ExifTool to verify metadata

### HEIC files not working
- Install "HEIF Image Extensions" from Microsoft Store
- Verify codec support with Windows Photos app

## Credits

- **Leaflet.js** - Interactive map library
- **OpenStreetMap** - Map tile provider
- **MetadataExtractor** - EXIF parsing library
- **WebView2** - Microsoft Edge WebView2

## Contact

For issues and feature requests, please use the [GitHub Issues](https://github.com/yourusername/PhotoGeoPreviewHandler/issues) page.
