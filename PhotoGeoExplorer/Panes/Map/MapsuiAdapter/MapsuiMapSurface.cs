using System;
using Mapsui;
using Mapsui.Extensions;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// <see cref="IMapSurface"/> の Mapsui 実装。Mapsui の <see cref="Navigator"/> との変換をここに閉じ込める。
/// </summary>
/// <remarks>
/// 購読先（<see cref="Navigator"/>）は <see cref="Mapsui.Map"/> の一部で、この表面と同じ寿命なので、解除は要らない。
/// </remarks>
internal sealed class MapsuiMapSurface : IMapSurface
{
    private readonly Mapsui.Map _map;

    internal MapsuiMapSurface(Mapsui.Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
        map.Navigator.ViewportChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
    }

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

    public void SetView(double centerX, double centerY, double resolution)
    {
        _map.Navigator.CenterOnAndZoomTo(new MPoint(centerX, centerY), resolution, duration: 0);
        _map.Refresh();
    }
}
