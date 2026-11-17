using System;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Services.MapRendering;
using SkiaSharp;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 表示範囲画像を Mapsui へ載せる専用レイヤー。1 フィーチャー＝「画像＋その地理範囲」で、
/// 操作中は直前の画像が Mapsui の通常の描画として視点に追従する。画像の交換は UI スレッドで行う。
/// </summary>
internal sealed class ViewExtentImageLayer : IDisposable
{
    private readonly MemoryLayer _layer;
    private SharedResource<SKImage>? _current;
    private bool _disposed;

    internal ViewExtentImageLayer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ViewExtentImageStyleRenderer.EnsureRegistered();
        _layer = new MemoryLayer { Name = name, Style = null };
    }

    internal ILayer Layer => _layer;

    /// <summary>
    /// 新しい画像へ交換する。画像の参照（1 つ）はこのレイヤーが引き継ぎ、交換で外れた画像は参照が
    /// すべて返ってから解放される。破棄済みなら画像をそのまま解放する。
    /// </summary>
    internal void Show(ViewExtentFrame<SKImage> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_disposed)
        {
            frame.Image.Release();
            return;
        }

        var previous = _current;
        _current = frame.Image;
        _layer.Features = [CreateFeature(frame)];
        _layer.DataHasChanged();
        previous?.Release();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layer.Features = [];
        var previous = _current;
        _current = null;
        previous?.Release();
        _layer.Dispose();
    }

    private static GeometryFeature CreateFeature(ViewExtentFrame<SKImage> frame)
    {
        var extent = frame.Request.Extent;
        var ring = new LinearRing(
        [
            new Coordinate(extent.MinX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MinY),
            new Coordinate(extent.MaxX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MaxY),
            new Coordinate(extent.MinX, extent.MinY),
        ]);
        var feature = new GeometryFeature(new Polygon(ring));
        feature.Styles.Clear();
        feature.Styles.Add(new ViewExtentImageStyle(frame.Image, extent));
        return feature;
    }
}
