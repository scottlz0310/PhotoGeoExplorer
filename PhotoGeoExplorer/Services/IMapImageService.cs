using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal delegate Task<string?> MapImageSavePickerAsync(
    MapImageSavePickerOptions options,
    CancellationToken cancellationToken);


internal interface IMapImageService
{
    string CreateDefaultFileName(DateTimeOffset timestamp);

    Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken);
}
