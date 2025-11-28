# PhotoGeoPreview

A C++/WinRT + WebView2 based PowerToys fork add-on for displaying image geotags.

## Overview

Implemented as a **PowerToys fork** using C++.
- **Tech**: C++/WinRT + WebView2 + HTML/CSS/JS
- **UI**: Single WebView2 hosting image and map
- **Splitter**: HTML/CSS/JS resizable layout

## Features
- ✅ Fully compliant with PowerToys standard structure (C++)
- ✅ Complete UI within single WebView2
- ✅ Flexible UI via HTML/CSS/JS
- ✅ Fast EXIF extraction via WIC

## Requirements
- Windows 10 / 11 (x64 / ARM64)
- PowerToys (forked version)
- WebView2 Runtime
- **HEIC Support**: Windows HEIF Image Extensions (from Microsoft Store)

## Current Status

This repository contains:
- 📋 **Documentation**: Complete implementation planning and architecture documents
- 📝 **Code Templates**: Ready-to-use C++ source code templates in `templates/` directory
- 🚀 **Setup Guide**: Detailed step-by-step instructions for implementing in PowerToys fork

**Note**: The actual implementation should be done in a separate PowerToys fork repository. This repository serves as documentation and code template reference.

## Getting Started

### Step 1: Read the Setup Guide

👉 **[SETUP_GUIDE.md](SETUP_GUIDE.md)** - Complete guide for forking PowerToys and implementing PhotoGeoPreview

### Step 2: Review Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) - System architecture and component design
- [ImplementationPlan.md](ImplementationPlan.md) - Detailed implementation plan
- [TASKS.md](TASKS.md) - Task breakdown and checklist
- [TECHSTACK.md](TECHSTACK.md) - Technology stack details

### Step 3: Use Code Templates

The `templates/` directory contains ready-to-use code:
- `PhotoGeoPreviewHandler.h` - Main handler header
- `PhotoGeoPreviewHandler.cpp` - Main handler implementation
- `Resources/template.html` - HTML template with Leaflet map
- `module.def` - COM export definitions
- `pch.h` / `pch.cpp` - Precompiled headers
- `preview_handler_registration.json` - Registration configuration

## Quick Start for PowerToys Fork

```bash
# 1. Fork and clone PowerToys
git clone https://github.com/YOUR_USERNAME/PowerToys.git
cd PowerToys

# 2. Create PhotoGeoPreview directory
mkdir src/modules/previewpane/PhotoGeoPreview
mkdir src/modules/previewpane/PhotoGeoPreview/Resources

# 3. Copy templates from this repository
# (Copy files from templates/ directory to your PowerToys fork)

# 4. Build PowerToys
.\build\build.cmd -Configuration Debug -Platform x64
```

For detailed instructions, see [SETUP_GUIDE.md](SETUP_GUIDE.md).

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

## Reference Implementations
- `src/modules/previewpane/MarkdownPreviewHandler/` (C++)
- `src/modules/previewpane/SvgPreviewHandler/` (C++)

## License

Follows PowerToys license (MIT License).

## Contact

[GitHub Issues](https://github.com/scottlz0310/PhotoGeoPreviewPane/issues)
