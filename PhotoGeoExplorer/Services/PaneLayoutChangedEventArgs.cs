using System;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal sealed class PaneLayoutChangedEventArgs : EventArgs
{
    public PaneLayoutChangedEventArgs(
        PaneLayoutPreset preset,
        PaneViewType region1View,
        PaneViewType region2View,
        PaneViewType region3View)
    {
        Preset = preset;
        Region1View = region1View;
        Region2View = region2View;
        Region3View = region3View;
    }

    public PaneLayoutPreset Preset { get; }

    public PaneViewType Region1View { get; }

    public PaneViewType Region2View { get; }

    public PaneViewType Region3View { get; }
}
