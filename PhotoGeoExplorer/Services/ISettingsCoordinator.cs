using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Services;

internal interface ISettingsCoordinator : IDisposable
{
    bool SettingsFileExistsAtStartup { get; }

    string? LanguageOverride { get; }

    bool ShowQuickStartOnStartup { get; set; }

    Task LoadAsync();

    void ScheduleSave();

    Task SaveAsync();

    Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt);

    void ChangeTheme(ThemePreference preference);

    void ChangeMapZoomLevel(int level);

    void ChangeMapTileSource(MapTileSourceType sourceType);

    Task ExportSettingsAsync();

    Task ImportSettingsAsync();
}
