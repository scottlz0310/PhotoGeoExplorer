using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.Services.PanZoom;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// <see cref="IMapSurface"/> の Mapsui 実装。Mapsui の <see cref="Navigator"/>・<see cref="MemoryLayer"/> との変換をここに閉じ込める。
/// </summary>
/// <remarks>
/// 購読先（<see cref="Navigator"/>）は <see cref="Mapsui.Map"/> の一部で、この表面と同じ寿命なので、解除は要らない。
/// 写真マーカー用のレイヤーは、この表面が地図の最前面（背景の上）へ追加し、破棄するときに外す。
/// <para>
/// 利用者の入力は <see cref="ApplyGesture"/>・<see cref="ZoomByWheel"/> だけから受ける（#362 段階 1）。
/// <c>MapControl</c> 自身の移動・拡大・回転は、<see cref="Navigator"/> のロックで止める。回転は当面許さない。
/// 慣性とホイールの段階ズームは、従来どおり Mapsui の処理を呼ぶ。
/// 視点を指定して移す API（<c>CenterOn</c> など）は、ロックの影響を受けない。
/// </para>
/// </remarks>
internal sealed class MapsuiMapSurface : IMapSurface
{
    internal const string MarkerLayerName = "PhotoMarkers";
    internal const string MarkerKey = "MapMarker";

    private const string SelectionLayerName = "RectangleSelection";

    // 慣性の最長時間。Mapsui の MapControl と同じ
    private const long FlingDurationMilliseconds = 1000;

    private static readonly Color SelectionFillColor = Color.FromArgb(64, 0, 120, 215);
    private static readonly Color SelectionOutlineColor = Color.FromArgb(255, 0, 120, 215);

    private readonly Mapsui.Map _map;
    private readonly MemoryLayer _markerLayer;
    private MemoryLayer? _selectionLayer;
    private IReadOnlyList<MapMarker> _markers = [];
    private bool _panSuspended;
    private readonly FlingTracker _fling = new();
    private bool _flingBlocked;
    private readonly MapAttributionPresenter _attribution = new();
    private IMapsuiBackground? _background;
    private bool _attributionAttached;
    private double _pixelDensity = 1.0;
    private readonly bool _ownsMap;

    internal MapsuiMapSurface(Mapsui.Map map)
        : this(map, ownsMap: false)
    {
    }

    private MapsuiMapSurface(Mapsui.Map map, bool ownsMap)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
        _ownsMap = ownsMap;
        _markerLayer = new MemoryLayer
        {
            Name = MarkerLayerName,
            Features = Array.Empty<IFeature>(),
            Style = null
        };
        map.Layers.Add(_markerLayer);
        map.Navigator.PanLock = true;
        map.Navigator.ZoomLock = true;
        map.Navigator.RotationLock = true;
        map.Navigator.ViewportChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 地図を持つ表示面を作る。地図は、表示面を破棄するときに破棄する。
    /// </summary>
    internal static MapsuiMapSurface Create() => new(new Mapsui.Map(), ownsMap: true);

    /// <summary>
    /// 汎用の表示面を、この実装の型へ変換する。<see langword="null"/> はそのまま返す。
    /// </summary>
    /// <exception cref="ArgumentException">この実装が作った表示面ではないとき</exception>
    internal static MapsuiMapSurface? From(IMapSurface? surface)
    {
        return surface switch
        {
            null => null,
            MapsuiMapSurface mapsuiSurface => mapsuiSurface,
            _ => throw new ArgumentException("The surface is not a Mapsui surface.", nameof(surface))
        };
    }

    /// <summary>
    /// 地図の実体。<c>MapControl</c> へ渡す（<see cref="MapsuiMapView"/>）。
    /// </summary>
    internal Mapsui.Map Map => _map;
    /// <summary>
    /// この表示面が地図を所有し、破棄するときに地図も破棄するか。<see cref="Create"/> で作った表示面は所有する。
    /// </summary>
    internal bool OwnsMap => _ownsMap;


    public MapViewState View
    {
        get
        {
            var viewport = _map.Navigator.Viewport;
            return new MapViewState(
                viewport.HasSize(),
                viewport.CenterX,
                viewport.CenterY,
                viewport.Resolution,
                viewport.Width,
                viewport.Height);
        }
    }

    public event EventHandler? ViewChanged;

    public IReadOnlyList<MapMarker> Markers => _markers;

    public void SetView(double centerX, double centerY, double resolution)
    {
        _map.Navigator.CenterOnAndZoomTo(new MPoint(centerX, centerY), resolution, duration: 0);
        _map.Refresh();
    }

    public void ShowMarker(MapMarker marker, int zoomLevel)
    {
        ArgumentNullException.ThrowIfNull(marker);

        _markers = [marker];
        _markerLayer.Features = new[] { CreateFeature(marker) };
        _map.Refresh();

        var navigator = _map.Navigator;
        navigator.CenterOn(ToWorld(marker), 0, Mapsui.Animations.Easing.CubicOut);
        if (navigator.Resolutions.Count > 0)
        {
            var targetLevel = Math.Clamp(zoomLevel, 0, navigator.Resolutions.Count - 1);
            navigator.ZoomToLevel(targetLevel);
        }
    }

    public void ShowMarkers(IReadOnlyList<MapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        _markers = markers;
        var features = new List<IFeature>(markers.Count);
        var hasBounds = false;
        var minX = 0d;
        var minY = 0d;
        var maxX = 0d;
        var maxY = 0d;

        foreach (var marker in markers)
        {
            var position = ToWorld(marker);
            if (!hasBounds)
            {
                minX = maxX = position.X;
                minY = maxY = position.Y;
                hasBounds = true;
            }
            else
            {
                minX = Math.Min(minX, position.X);
                maxX = Math.Max(maxX, position.X);
                minY = Math.Min(minY, position.Y);
                maxY = Math.Max(maxY, position.Y);
            }

            features.Add(CreateFeature(marker));
        }

        _markerLayer.Features = features;
        _map.Refresh();

        if (!hasBounds)
        {
            return;
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var padding = Math.Max(spanX, spanY) * 0.1;
        if (padding <= 0)
        {
            padding = 500;
        }

        var bounds = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        _map.Navigator.ZoomToBox(bounds, MBoxFit.Fit, 0, Mapsui.Animations.Easing.CubicOut);
    }

    public void ClearMarkers()
    {
        _markers = [];
        _markerLayer.Features = Array.Empty<IFeature>();
        _map.Refresh();
    }

    public bool TryGetGeoPosition(double screenX, double screenY, out GeoPoint position)
    {
        var world = _map.Navigator.Viewport.ScreenToWorld(screenX, screenY);
        var lonLat = SphericalMercator.ToLonLat(world);
        position = new GeoPoint(lonLat.Y, lonLat.X);
        return double.IsFinite(position.Latitude) && double.IsFinite(position.Longitude);
    }

    public void LockPan() => _panSuspended = true;

    public void RestorePan() => _panSuspended = false;

    public void ApplyGesture(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY)
    {
        var view = View;
        if (!view.HasSize)
        {
            return;
        }

        // 慣性の速さは、従来の地図と同じく Mapsui の追跡で求める。パンを止めている間の動きは数えない
        if (_panSuspended)
        {
            _flingBlocked = true;
        }
        else
        {
            _fling.AddEvent(new ScreenPosition(pivot.X + translationX, pivot.Y + translationY), DateTime.Now.Ticks);
        }

        var center = new PanZoomPoint(view.WidthDip / 2, view.HeightDip / 2);
        if (_panSuspended)
        {
            pivot = center;
            translationX = 0;
            translationY = 0;
        }

        // Navigator.Manipulate はタイルの取得を要求しないが、拡大の基準が中心でないと位置がずれ（拡大前の解像度で補正する近似）、
        // ロックにも従う。中心基準の拡大と、拡大を伴わない移動（どちらも式が正確になる形）に分け、呼ぶ間だけロックを外す。
        var navigator = _map.Navigator;
        navigator.PanLock = false;
        navigator.ZoomLock = false;
        try
        {
            Manipulate(navigator, center, center, scaleFactor);

            // ズームの上下限で丸められた場合に基準がずれないよう、実際に反映された倍率で移動先を計算する
            var zoomed = View;
            var next = MapViewGesture.Apply(view, pivot, view.Resolution / zoomed.Resolution, translationX, translationY);
            var moved = new PanZoomPoint(
                center.X - ((next.CenterX - zoomed.CenterX) / zoomed.Resolution),
                center.Y + ((next.CenterY - zoomed.CenterY) / zoomed.Resolution));
            Manipulate(navigator, moved, center, 1);
        }
        finally
        {
            navigator.PanLock = true;
            navigator.ZoomLock = true;
        }
    }

    public void CompleteGesture()
    {
        // 矩形選択は、ボタンを離すとパンの停止を解いてからここへ来るため、操作の途中で止めたことを覚えておく
        if (!_flingBlocked && !_panSuspended)
        {
            var navigator = _map.Navigator;
            _fling.FlingIfNeeded((velocityX, velocityY) =>
            {
                navigator.PanLock = false;
                try
                {
                    navigator.Fling(velocityX, velocityY, FlingDurationMilliseconds);
                }
                finally
                {
                    navigator.PanLock = true;
                }
            });
        }

        _fling.Restart();
        _flingBlocked = false;
    }

    public void ZoomByWheel(int wheelDelta, PanZoomPoint anchor)
    {
        // 段階ズームと補間は従来どおり Mapsui に任せる。PanLock が掛かっていると、Mapsui は中心を基準にする
        var navigator = _map.Navigator;
        navigator.ZoomLock = false;
        navigator.PanLock = _panSuspended;
        try
        {
            navigator.MouseWheelZoom(wheelDelta, new ScreenPosition(anchor.X, anchor.Y));
        }
        finally
        {
            navigator.PanLock = true;
            navigator.ZoomLock = true;
        }
    }

    private static void Manipulate(Navigator navigator, PanZoomPoint current, PanZoomPoint previous, double scaleFactor)
        => navigator.Manipulate(new Manipulation(
            new ScreenPosition(current.X, current.Y),
            new ScreenPosition(previous.X, previous.Y),
            scaleFactor,
            0,
            0));

    public void ShowSelectionRectangle(GeoPoint corner1, GeoPoint corner2)
    {
        var start = SphericalMercator.FromLonLat(new MPoint(corner1.Longitude, corner1.Latitude));
        var end = SphericalMercator.FromLonLat(new MPoint(corner2.Longitude, corner2.Latitude));
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        var polygon = new Polygon(new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY)
        }));

        var feature = new GeometryFeature
        {
            Geometry = polygon
        };
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(SelectionFillColor),
            Outline = new Pen(SelectionOutlineColor, 2)
        });

        if (_selectionLayer is null)
        {
            _selectionLayer = new MemoryLayer
            {
                Name = SelectionLayerName,
                Features = new[] { feature },
                Style = null
            };
            _map.Layers.Add(_selectionLayer);
        }
        else
        {
            _selectionLayer.Features = new[] { feature };
        }

        _map.Refresh();
    }

    public void ClearSelectionRectangle()
    {
        if (_selectionLayer is null)
        {
            return;
        }

        _map.Layers.Remove(_selectionLayer);
        _selectionLayer.Dispose();
        _selectionLayer = null;
        _map.Refresh();
    }

    public double PixelDensity
    {
        get => Volatile.Read(ref _pixelDensity);
        set
        {
            if (Volatile.Read(ref _pixelDensity) == value)
            {
                return;
            }

            Volatile.Write(ref _pixelDensity, value);
            Volatile.Read(ref _background)?.OnPixelDensityChanged();
        }
    }

    public void ReplaceBackground(IMapBackground background)
    {
        ArgumentNullException.ThrowIfNull(background);
        var mapsuiBackground = background as IMapsuiBackground
            ?? throw new ArgumentException("The background is not a Mapsui background.", nameof(background));

        if (Volatile.Read(ref _background) is { } oldBackground)
        {
            foreach (var layer in oldBackground.Layers)
            {
                _map.Layers.Remove(layer);
            }
        }

        var index = 0;
        foreach (var layer in mapsuiBackground.Layers)
        {
            _map.Layers.Insert(index++, layer);
        }

        Volatile.Write(ref _background, mapsuiBackground);
        mapsuiBackground.Attach(_map, () => PixelDensity);

        if (_attributionAttached)
        {
            _attribution.Update(mapsuiBackground.SourceType);
        }
        else
        {
            _attribution.Attach(_map, mapsuiBackground.SourceType);
            _attributionAttached = true;
            _ = LoadAttributionImagesAsync();
        }

        _map.Refresh();
    }

    /// <summary>
    /// 地図の描画の開始直前に、表示中の背景へ伝える。<c>MapControl</c> の描画フックから呼ぶ。
    /// </summary>
    /// <remarks>
    /// フックの引数が Mapsui の型なので、<see cref="MapsuiMapView"/> の描画フックから呼ぶ。
    /// </remarks>
    internal Action? BeginMapRender(Viewport viewport, IReadOnlyList<ILayer> layers, RenderService renderService)
        => Volatile.Read(ref _background)?.BeginMapRender(viewport, layers, renderService);

    public async Task<Stream> RenderPngAsync(float pixelDensity, CancellationToken cancellationToken)
    {
        var background = Volatile.Read(ref _background)
            ?? throw new InvalidOperationException("The map background is not initialized.");
        var viewport = _map.Navigator.Viewport;

        using var exportLayers = await background.CreateExportLayersAsync(viewport, pixelDensity, cancellationToken).ConfigureAwait(true);
        var layers = MapExportLayers.Compose(_map.Layers, background, exportLayers);
        try
        {
            return MapsuiMapImageRenderer.RenderPng(_map, layers, viewport, pixelDensity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Failed to render the current map viewport as PNG.", ex);
        }
    }

    private async Task LoadAttributionImagesAsync()
    {
        try
        {
            if (!await _attribution.LoadImagesAsync(_map).ConfigureAwait(true))
            {
                AppLog.Error($"Failed to load the map attribution logo: {MapAttribution.MapboxLogoPath}");
                return;
            }

            _map.RefreshGraphics();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error("Failed to load the map attribution logo.", ex);
        }
    }

    public void Dispose()
    {
        ClearSelectionRectangle();
        _markerLayer.Dispose();
        if (_ownsMap)
        {
            _map.Dispose();
        }
    }

    private static MPoint ToWorld(MapMarker marker)
        => SphericalMercator.FromLonLat(new MPoint(marker.Longitude, marker.Latitude));

    private static PointFeature CreateFeature(MapMarker marker)
    {
        var feature = new PointFeature(ToWorld(marker));
        feature.Styles.Clear();
        feature.Styles.Add(MapsuiMarkerStyle.Create(marker.PinImagePath));
        feature[MarkerKey] = marker;
        return feature;
    }
}
