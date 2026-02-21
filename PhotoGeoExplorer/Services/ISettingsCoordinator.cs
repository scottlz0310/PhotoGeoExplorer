using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Services;

internal interface ISettingsCoordinator : IDisposable
{
    bool SettingsFileExistsAtStartup { get; }

    string? LanguageOverride { get; }

    string? ExternalContentBaseUrl { get; }

    bool ShowQuickStartOnStartup { get; set; }

    PaneLayoutPreset PaneLayoutPreset { get; }

    PaneViewType PaneRegion1View { get; }

    PaneViewType PaneRegion2View { get; }

    PaneViewType PaneRegion3View { get; }

    event EventHandler<PaneLayoutChangedEventArgs>? PaneLayoutChanged;

    Task LoadAsync();

    void ScheduleSave();

    Task SaveAsync();

    Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt);

    void ChangeTheme(ThemePreference preference);

    void ChangeMapZoomLevel(int level);

    void ChangeMapTileSource(MapTileSourceType sourceType);

    void ChangePaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View);

    Task ExportSettingsAsync();

    Task ImportSettingsAsync();
}
