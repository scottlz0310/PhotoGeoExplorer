using System.Collections.Generic;
using System.IO;
using Mapsui;
using Mapsui.Layers;

namespace PhotoGeoExplorer.Services;

internal interface IMapImageRenderer
{
    Stream RenderPng(Map map, IReadOnlyList<ILayer> layers, Viewport viewport, float pixelDensity);
}
