using System;
using PhotoGeoExplorer.Services.PanZoom;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 入力アダプタの受け手を、地図の表示面へ中継する。表示面が接続されていない間の入力は無視する。
/// </summary>
internal sealed class MapSurfaceInputTarget : IPanZoomTarget
{
    private readonly Func<IMapSurface?> _surface;

    internal MapSurfaceInputTarget(Func<IMapSurface?> surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public bool Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY, bool isInertial)
        => _surface()?.ApplyGesture(pivot, scaleFactor, translationX, translationY, isInertial) ?? false;

    public void ZoomByWheel(int wheelDelta, PanZoomPoint anchor)
        => _surface()?.ZoomByWheel(wheelDelta, anchor);
}
