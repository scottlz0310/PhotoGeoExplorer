using System.Linq;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.UI;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// Mapsui のタップ（<see cref="MapInfoEventArgs"/>）から、当たったマーカーを取り出す。
/// </summary>
/// <remarks>
/// <c>MapControl.Info</c> の引数は Mapsui の型なので、View の薄型化（#362 §11.6-3 の PR ⑤）までは、View のハンドラからこの関数を呼ぶ。
/// </remarks>
internal static class MapsuiMarkerHitTest
{
    internal static MapMarker? FindMarker(Mapsui.Map? map, MapInfoEventArgs? e)
    {
        if (map is null || e is null)
        {
            return null;
        }

        var mapInfo = e.GetMapInfo(map.Layers);
        if (mapInfo?.Feature is not PointFeature feature)
        {
            return null;
        }

        // 写真マーカー以外（帰属表示や矩形選択の枠）に当たったタップは無視する
        var onMarkerLayer = map.Layers
            .OfType<MemoryLayer>()
            .Any(layer => layer.Name == MapsuiMapSurface.MarkerLayerName && layer.Features.Contains(feature));
        return onMarkerLayer ? feature[MapsuiMapSurface.MarkerKey] as MapMarker : null;
    }
}
