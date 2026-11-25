using System;
using System.Collections.Generic;
using System.Linq;
using Mapsui.Layers;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// PNG 保存用の背景レイヤー。保存が終わったら破棄する。
/// </summary>
internal interface IMapExportLayers : IDisposable
{
    /// <summary>保存用の背景レイヤー（下から順）。</summary>
    IReadOnlyList<ILayer> Layers { get; }
}

internal static class MapExportLayers
{
    /// <summary>
    /// 表示中のレイヤーをそのまま保存に使う。レイヤーの所有者は背景のままなので、破棄しない。
    /// </summary>
    internal static IMapExportLayers Borrowed(IReadOnlyList<ILayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return new BorrowedLayers(layers);
    }

    /// <summary>
    /// 保存に使うレイヤーの並び（下から順）を作る。背景のレイヤーを保存用に差し替え、
    /// 写真マーカーなど背景以外のレイヤーは、地図上の順序のまま上に重ねる。
    /// </summary>
    internal static IReadOnlyList<ILayer> Compose(
        IEnumerable<ILayer> mapLayers,
        IMapsuiBackground background,
        IMapExportLayers exportLayers)
    {
        ArgumentNullException.ThrowIfNull(mapLayers);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(exportLayers);

        return exportLayers.Layers
            .Concat(mapLayers.Where(layer => !background.Layers.Contains(layer)))
            .ToArray();
    }

    private sealed class BorrowedLayers(IReadOnlyList<ILayer> layers) : IMapExportLayers
    {
        public IReadOnlyList<ILayer> Layers { get; } = layers;

        public void Dispose()
        {
        }
    }
}
