using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 地図ライブラリなしで呼び出しを記録する <see cref="IMapSurface"/>。
/// </summary>
internal sealed class FakeMapSurface : IMapSurface
{
    private EventHandler? _viewChanged;
    private IReadOnlyList<MapMarker> _markers = [];

    internal List<(double CenterX, double CenterY, double Resolution)> SetViewCalls { get; } = [];

    internal List<(MapMarker Marker, int ZoomLevel)> ShowMarkerCalls { get; } = [];

    internal List<IReadOnlyList<MapMarker>> ShowMarkersCalls { get; } = [];

    internal List<(GeoPoint Corner1, GeoPoint Corner2)> SelectionRectangles { get; } = [];

    internal int ClearMarkersCount { get; private set; }

    internal int LockPanCount { get; private set; }

    internal int RestorePanCount { get; private set; }

    internal int ClearSelectionRectangleCount { get; private set; }

    internal int DisposeCount { get; private set; }

    internal int SubscriberCount => _viewChanged?.GetInvocationList().Length ?? 0;

    /// <summary>画面上の位置から地球上の位置への変換。null を返すと変換できない扱い。</summary>
    internal Func<double, double, GeoPoint?> GeoProjection { get; set; } = static (x, y) => new GeoPoint(y, x);

    public MapViewState View { get; set; }

    public IReadOnlyList<MapMarker> Markers => _markers;

    public event EventHandler? ViewChanged
    {
        add => _viewChanged += value;
        remove => _viewChanged -= value;
    }

    public void SetView(double centerX, double centerY, double resolution)
        => SetViewCalls.Add((centerX, centerY, resolution));

    public void ShowMarker(MapMarker marker, int zoomLevel)
    {
        _markers = [marker];
        ShowMarkerCalls.Add((marker, zoomLevel));
    }

    public void ShowMarkers(IReadOnlyList<MapMarker> markers)
    {
        _markers = markers;
        ShowMarkersCalls.Add(markers);
    }

    public void ClearMarkers()
    {
        _markers = [];
        ClearMarkersCount++;
    }

    public bool TryGetGeoPosition(double screenX, double screenY, out GeoPoint position)
    {
        var projected = GeoProjection(screenX, screenY);
        position = projected.GetValueOrDefault();
        return projected is not null;
    }

    public void LockPan() => LockPanCount++;

    public void RestorePan() => RestorePanCount++;

    public void ShowSelectionRectangle(GeoPoint corner1, GeoPoint corner2)
        => SelectionRectangles.Add((corner1, corner2));

    public void ClearSelectionRectangle() => ClearSelectionRectangleCount++;

    public void Dispose() => DisposeCount++;

    internal void RaiseViewChanged() => _viewChanged?.Invoke(this, EventArgs.Empty);
}
