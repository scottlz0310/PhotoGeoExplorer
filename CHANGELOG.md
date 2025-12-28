# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- WinUI 3 app shell with File Browser, Image Preview, and Map panes.
- WebView2 initialization that loads the local `wwwroot/index.html` map page.
- Application logging to `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log`.
- CI/quality/security workflows and a tag-based release workflow for unsigned MSIX artifacts.
- Pre-commit and pre-push checks via lefthook.

### Changed
- Adopted strict analyzer settings and formatting checks in CI and hooks.
- Updated core dependencies (Windows App SDK, WebView2, MetadataExtractor).

### Fixed
- CodeQL build failures by redirecting generated files to a shorter path.
- Windows App SDK bootstrap path for app startup.
- Window sizing logic to use `AppWindow` safely.

### Removed
- Placeholder Models/Services/ViewModels that were not yet wired into the app.
