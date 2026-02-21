using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Models;

internal enum MapTileSourceType
{
    OpenStreetMap = 0,
    EsriWorldImagery = 1
}

internal enum PaneLayoutPreset
{
    LeftCenterRight = 0,
    LeftAndRightSplit = 1,
    LeftSplitAndRight = 2
}

internal enum PaneViewType
{
    File = 0,
    Preview = 1,
    Map = 2
}

internal sealed class AppSettings
{
    internal const string DefaultExternalContentBaseUrl = "https://photogeoexplorer.pages.dev";
    internal const PaneLayoutPreset DefaultPaneLayoutPreset = PaneLayoutPreset.LeftAndRightSplit;
    internal const PaneViewType DefaultPaneRegion1View = PaneViewType.File;
    internal const PaneViewType DefaultPaneRegion2View = PaneViewType.Preview;
    internal const PaneViewType DefaultPaneRegion3View = PaneViewType.Map;

    public string? LastFolderPath { get; set; }
    public bool ShowImagesOnly { get; set; } = true;
    public FileViewMode FileViewMode { get; set; } = FileViewMode.Details;
    public bool ShowDetailsModifiedColumn { get; set; } = true;
    public bool ShowDetailsResolutionColumn { get; set; } = true;
    public bool ShowDetailsSizeColumn { get; set; } = true;
    public bool ShowDetailsTakenAtColumn { get; set; }
    public bool ShowDetailsLocationColumn { get; set; }
    public string? Language { get; set; }
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool AutoCheckUpdates { get; set; } = true;
    public int MapDefaultZoomLevel { get; set; } = 14;
    public MapTileSourceType MapTileSource { get; set; } = MapTileSourceType.OpenStreetMap;
    public bool ShowQuickStartOnStartup { get; set; }
    public string? ExternalContentBaseUrl { get; set; } = DefaultExternalContentBaseUrl;
    public PaneLayoutPreset PaneLayoutPreset { get; set; } = DefaultPaneLayoutPreset;
    public PaneViewType PaneRegion1View { get; set; } = DefaultPaneRegion1View;
    public PaneViewType PaneRegion2View { get; set; } = DefaultPaneRegion2View;
    public PaneViewType PaneRegion3View { get; set; } = DefaultPaneRegion3View;
}
