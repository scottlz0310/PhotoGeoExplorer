using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;

namespace PhotoGeoExplorer.Services;

internal interface IMapImageService
{
    string CreateDefaultFileName(DateTimeOffset timestamp);

    Stream RenderPng(Map map, Viewport viewport, float pixelDensity);

    Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken);
}
