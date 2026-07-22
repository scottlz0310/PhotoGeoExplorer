using System;
using System.IO;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;

namespace PhotoGeoExplorer.Services;

internal sealed class MapsuiMapImageRenderer : IMapImageRenderer
{
    public Stream RenderPng(Map map, Viewport viewport, float pixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new ArgumentException("Viewport dimensions must be positive.", nameof(viewport));
        }

        if (!float.IsFinite(pixelDensity) || pixelDensity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelDensity), "Pixel density must be positive and finite.");
        }

        var renderer = new MapRenderer();
        var stream = renderer.RenderToBitmapStream(
            viewport,
            map.Layers,
            map.RenderService,
            map.BackColor,
            pixelDensity,
            map.GetWidgetsOfMapAndLayers(),
            RenderFormat.Png);
        stream.Position = 0;
        return stream;
    }
}
