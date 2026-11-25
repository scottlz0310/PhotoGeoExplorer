using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BruTile;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using SkiaSharp;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// ラスタタイル 1 枚のレイヤーを背景として扱う（OpenStreetMap・Esri・Mapbox）。
/// </summary>
internal sealed class RasterMapBackground : IMapsuiBackground
{
    private readonly TileLayer _layer;
    private readonly ConditionalWeakTable<MRaster, StrongBox<bool>> _decodedValidityCache = new();
    private Mapsui.Map? _map;
    private long _dirtyVersion;
    private long _notifiedVersion;
    private bool _disposed;

    internal RasterMapBackground(TileLayer layer, MapTileSourceType sourceType)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _layer = layer;
        Layers = [layer];
        SourceType = sourceType;
        AttributionText = string.Join(' ', MapBackgroundCatalog.Get(sourceType).AttributionLinks.Select(link => link.Text));
        _layer.PropertyChanged += OnLayerPropertyChanged;
        _layer.DataChanged += OnLayerDataChanged;
    }

    public IReadOnlyList<ILayer> Layers { get; }

    public MapTileSourceType SourceType { get; }

    public string AttributionText { get; }

    public bool IsBusy => _layer.Busy;

    public event EventHandler? BusyChanged;

    public event EventHandler? FrameRendered;

    public void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(getPixelDensity);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var previous = Interlocked.Exchange(ref _map, map);
        if (previous is not null)
        {
            previous.Navigator.ViewportChanged -= OnViewportChanged;
        }

        map.Navigator.ViewportChanged += OnViewportChanged;
        MarkRenderPending();
    }

    public void OnPixelDensityChanged()
    {
    }

    public Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(renderService);

        if (_disposed)
        {
            return null;
        }

        var version = Volatile.Read(ref _dirtyVersion);
        if (version == Volatile.Read(ref _notifiedVersion))
        {
            return null;
        }

        if (!ContainsLayer(layers, _layer)
            || _layer.Busy
            || !MatchesCurrentViewport(viewport)
            || !HasRenderedViewportTiles(viewport, renderService))
        {
            return null;
        }

        return () => CompleteMapRender(version, viewport);
    }

    public Task<IMapExportLayers> CreateExportLayersAsync(
        Viewport viewport,
        float pixelDensity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MapExportLayers.Borrowed(Layers));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var map = Interlocked.Exchange(ref _map, null);
        if (map is not null)
        {
            map.Navigator.ViewportChanged -= OnViewportChanged;
        }

        _layer.DataChanged -= OnLayerDataChanged;
        _layer.PropertyChanged -= OnLayerPropertyChanged;
        _layer.Dispose();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TileLayer.Busy))
        {
            return;
        }

        if (_layer.Busy)
        {
            MarkRenderPending();
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLayerDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.Error is null)
        {
            MarkRenderPending();
        }
    }

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
        => MarkRenderPending();

    private void MarkRenderPending()
        => Interlocked.Increment(ref _dirtyVersion);

    private void CompleteMapRender(long renderedVersion, Viewport renderedViewport)
    {
        if (_disposed
            || Volatile.Read(ref _dirtyVersion) != renderedVersion
            || _layer.Busy
            || !MatchesCurrentViewport(renderedViewport)
            || !HasRenderedViewportTiles(renderedViewport, renderService: null))
        {
            return;
        }

        if (Interlocked.Exchange(ref _notifiedVersion, renderedVersion) == renderedVersion)
        {
            return;
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    private bool MatchesCurrentViewport(Viewport renderedViewport)
    {
        var map = Volatile.Read(ref _map);
        if (map is null)
        {
            return false;
        }

        var current = map.Navigator.Viewport;
        return current.HasSize() && current == renderedViewport;
    }

    private static bool ContainsLayer(IReadOnlyList<ILayer> layers, ILayer target)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            if (ReferenceEquals(layers[i], target))
            {
                return true;
            }
        }

        return false;
    }

    // Mapsui の TileFetchPlanner はキャッシュ追加後、DataChanged による再描画要求より先に Busy を解除し、
    // 取得失敗時も Busy を false に戻す。また RasterStyleRenderer は画像をデコードできない場合でも
    // 例外を握りつぶして復帰するため、対象ビューポートに必要な全タイルが描画可能な画像として
    // デコードできることまで検証し、画面への反映後にのみ完了を通知する。
    private bool HasRenderedViewportTiles(Viewport viewport, RenderService? renderService)
    {
        if (!viewport.HasSize() || !double.IsFinite(viewport.Resolution) || viewport.Resolution <= 0)
        {
            return false;
        }

        var schema = _layer.TileSource?.Schema;
        if (schema is null || schema.Resolutions.Count == 0)
        {
            return false;
        }

        var viewportExtent = viewport.ToExtent();
        if (viewportExtent is null)
        {
            return false;
        }

        var bruTileExtent = new Extent(viewportExtent.MinX, viewportExtent.MinY, viewportExtent.MaxX, viewportExtent.MaxY);
        var clipped = bruTileExtent.Intersect(schema.Extent);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return false;
        }

        var levelId = BruTile.Utilities.GetNearestLevel(schema.Resolutions, viewport.Resolution);
        var requiredTiles = schema.GetTileInfos(clipped, levelId).ToList();
        if (requiredTiles.Count == 0)
        {
            return false;
        }

        var rasterFeatures = _layer
            .GetFeatures(viewportExtent, viewport.Resolution)
            .OfType<RasterFeature>()
            .Where(feature => feature.Extent is not null && feature.Raster is not null)
            .ToList();

        if (rasterFeatures.Count < requiredTiles.Count)
        {
            return false;
        }

        foreach (var tile in requiredTiles)
        {
            var matched = rasterFeatures.FirstOrDefault(feature => MatchesTileExtent(feature.Extent!, tile.Extent));
            if (matched is null)
            {
                return false;
            }

            if (renderService is not null && !IsDecodableRaster(matched.Raster!, renderService))
            {
                return false;
            }
        }

        return true;
    }

    // 描画開始前に TileCache 経由でデコード結果を検証する。SKImage.FromEncodedData は遅延デコードのため、
    // ヘッダーだけ読める途中で切れた PNG でも寸法付きの SKImage を返し得る。SKCodec.GetPixels で
    // ピクセルの完全デコード成功（SKCodecResult.Success）まで確認し、結果は MRaster 単位でキャッシュする。
    // 有効なタイルは TileCache に残るため直後の MapRenderer.Render で再利用され、LRU 世代も最新化される。
    private bool IsDecodableRaster(MRaster raster, RenderService renderService)
    {
        if (raster.Data.Length == 0)
        {
            return false;
        }

        var entry = renderService.TileCache.GetOrAdd(raster, RasterStyleRenderer.ToTileCacheEntry, currentIteration: 0);
        if (entry?.Data is SKPicture picture)
        {
            return picture.CullRect.Width > 0 && picture.CullRect.Height > 0;
        }

        if (entry?.Data is not SKImage { Width: > 0, Height: > 0 })
        {
            return false;
        }

        return _decodedValidityCache
            .GetValue(raster, static key => new StrongBox<bool>(VerifyFullPixelDecode(key.Data)))
            .Value;
    }

    private static bool VerifyFullPixelDecode(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var codec = SKCodec.Create(stream, out var codecResult);
        if (codecResult != SKCodecResult.Success || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return false;
        }

        var decodeInfo = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var bitmap = new SKBitmap(decodeInfo);
        return codec.GetPixels(decodeInfo, bitmap.GetPixels()) == SKCodecResult.Success;
    }

    private static bool MatchesTileExtent(MRect actual, Extent expected)
    {
        var toleranceX = Math.Max(1e-6, expected.Width * 1e-6);
        var toleranceY = Math.Max(1e-6, expected.Height * 1e-6);
        return Math.Abs(actual.MinX - expected.MinX) <= toleranceX
            && Math.Abs(actual.MinY - expected.MinY) <= toleranceY
            && Math.Abs(actual.MaxX - expected.MaxX) <= toleranceX
            && Math.Abs(actual.MaxY - expected.MaxY) <= toleranceY;
    }
}
