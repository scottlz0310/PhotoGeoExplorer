# PhotoGeoPreview

A standalone C++/Win32 + WebView2 based Preview Handler for displaying image geotags.
Displays photos and their shooting locations (map) in the Windows Explorer preview pane.

## Overview

This project is a **standalone** lightweight Preview Handler that **does not depend on external tools like PowerToys**.
- **Tech**: C++/Win32 (ATL/WRL) + WebView2 + HTML/CSS/JS
- **UI**: Single WebView2 hosting image and map
- **Splitter**: HTML/CSS/JS resizable layout
- **Dependencies**: Minimal (WebView2 Runtime only)

## Features
- ✅ **Standalone**: No PowerToys or huge frameworks required
- ✅ **Lightweight**: Fast execution via native C++ DLL
- ✅ **WebView2**: Modern UI built with web technologies
- ✅ **WIC**: Fast EXIF extraction using Windows standard features

## Requirements
- Windows 10 / 11 (x64 / ARM64)
- WebView2 Runtime (Pre-installed on Windows 11)
- **HEIC Support**: Windows HEIF Image Extensions (from Microsoft Store)

## Current Status

This repository contains:
- 📋 **Documentation**: Implementation planning and architecture design
- 📝 **Source Code**: C++ Preview Handler implementation (In Progress)
- 🚀 **Build Guide**: Build instructions using Visual Studio

## Getting Started

### Step 1: Prerequisites

- Visual Studio 2022 (C++ Desktop Development workload)
- Windows SDK

### Step 2: Build

1. Open `PhotoGeoPreview.sln` in Visual Studio
2. Set solution configuration to `Release` / `x64`
3. Build the solution

### Step 3: Install (Register)

Open Command Prompt as Administrator and register the built DLL.

```cmd
regsvr32.exe PhotoGeoPreviewHandler.dll
```

## Technical Details

### UI Structure (HTML)
```html
<div class="split-container">
  <div class="image-pane">
    <img src="{IMAGE_PATH}">
  </div>
  <div class="splitter"></div>
  <div class="map-pane"></div>
</div>
```

### EXIF Extraction (C++ + WIC)
```cpp
IWICImagingFactory* factory;
IWICBitmapDecoder* decoder;
IWICMetadataQueryReader* reader;
// Extract GPS coordinates
```

## License

Follows PowerToys license (MIT License).

## Contact

[GitHub Issues](https://github.com/scottlz0310/PhotoGeoPreviewPane/issues)
