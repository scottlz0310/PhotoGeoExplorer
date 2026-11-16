using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;

namespace PhotoGeoExplorer.Services;

internal delegate Task<string?> MapImageSavePickerAsync(
    MapImageSavePickerOptions options,
    CancellationToken cancellationToken);

internal delegate Task<Stream> MapImageSnapshotProviderAsync(CancellationToken cancellationToken);

internal interface IMapImageService
{
    string CreateDefaultFileName(DateTimeOffset timestamp);

    Stream RenderPng(Map map, IReadOnlyList<ILayer> layers, Viewport viewport, float pixelDensity);

    Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken);
}
