using System;
using System.Collections.Generic;
using System.IO;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;

namespace PhotoGeoExplorer.Services;

internal static class MapsuiMapImageRenderer
{
    public static Stream RenderPng(Map map, IReadOnlyList<ILayer> layers, Viewport viewport, float pixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layers);
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
            layers,
            map.RenderService,
            map.BackColor,
            pixelDensity,
            map.GetWidgetsOfMapAndLayers(),
            RenderFormat.Png);
        stream.Position = 0;
        return stream;
    }
}
