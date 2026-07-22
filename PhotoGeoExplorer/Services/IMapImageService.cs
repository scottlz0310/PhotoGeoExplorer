using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;

namespace PhotoGeoExplorer.Services;

internal delegate Task<string?> MapImageSavePickerAsync(
    MapImageSavePickerOptions options,
    CancellationToken cancellationToken);

internal delegate Task<Stream> MapImageSnapshotProviderAsync(CancellationToken cancellationToken);

internal interface IMapImageService
{
    string CreateDefaultFileName(DateTimeOffset timestamp);

    Stream RenderPng(Map map, Viewport viewport, float pixelDensity);

    Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken);
}
