using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Models;

internal sealed class AppSettings
{
    public string? LastFolderPath { get; set; }
    public bool ShowImagesOnly { get; set; } = true;
    public FileViewMode FileViewMode { get; set; } = FileViewMode.Details;
    public string? Language { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool AutoCheckUpdates { get; set; } = true;
}
