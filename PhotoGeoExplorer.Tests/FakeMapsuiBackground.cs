using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 呼び出しを記録する <see cref="IMapsuiBackground"/>。
/// </summary>
internal sealed class FakeMapsuiBackground : IMapsuiBackground
{
    private bool _busy;

    public List<ILayer> Layers { get; } = [new MemoryLayer { Name = "background" }];

    public List<ILayer> ExportLayers { get; } = [new MemoryLayer { Name = "export" }];

    public MapTileSourceType SourceType { get; set; } = MapTileSourceType.OpenStreetMap;

    public int DisposeCount { get; private set; }

    public int ExportDisposeCount { get; set; }

    public Viewport ExportViewport { get; private set; }

    public float ExportPixelDensity { get; private set; }

    public Mapsui.Map? AttachedMap { get; private set; }

    public Func<double>? GetPixelDensity { get; private set; }

    public int AttachCount { get; private set; }

    public int PixelDensityChangedCount { get; private set; }

    public int BeginMapRenderCount { get; private set; }

    IReadOnlyList<ILayer> IMapsuiBackground.Layers => Layers;

    public string AttributionText => string.Empty;

    public bool IsBusy => _busy;

    public event EventHandler? BusyChanged;

    public event EventHandler? FrameRendered;

    public void SetBusy(bool busy)
    {
        _busy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseFrameRendered() => FrameRendered?.Invoke(this, EventArgs.Empty);

    public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
        AttachCount++;
        AttachedMap = map;
        GetPixelDensity = getPixelDensity;
    }

    public void OnPixelDensityChanged() => PixelDensityChangedCount++;

    public Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
    {
        BeginMapRenderCount++;
        return RaiseFrameRendered;
    }

    public Task<IMapExportLayers> CreateExportLayersAsync(Viewport viewport, float pixelDensity, CancellationToken cancellationToken)
    {
        ExportViewport = viewport;
        ExportPixelDensity = pixelDensity;
        return Task.FromResult<IMapExportLayers>(new FakeExportLayers(this));
    }

    public void Dispose() => DisposeCount++;

    private sealed class FakeExportLayers(FakeMapsuiBackground owner) : IMapExportLayers
    {
        public IReadOnlyList<ILayer> Layers => owner.ExportLayers;

        public void Dispose() => owner.ExportDisposeCount++;
    }
}
