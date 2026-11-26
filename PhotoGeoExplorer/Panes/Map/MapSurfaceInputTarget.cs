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

    public void Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY)
        => _surface()?.ApplyGesture(pivot, scaleFactor, translationX, translationY);

    public void CompleteManipulation()
        => _surface()?.CompleteGesture();

    // 地図は、従来の MapControl と同じく、横のホイールでもズームする
    public bool ZoomByWheel(int wheelDelta, PanZoomPoint anchor, bool isHorizontal)
    {
        _surface()?.ZoomByWheel(wheelDelta, anchor);
        return true;
    }
}
