using System;
using System.Collections.Generic;
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
