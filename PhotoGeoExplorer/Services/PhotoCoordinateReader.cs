using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 写真の EXIF から撮影位置を読む。位置情報がない・読めない写真は null
/// （読めなかった理由は <see cref="IExifMetadataService"/> の実装がログに残す）
/// </summary>
internal sealed class PhotoCoordinateReader
{
    private readonly IExifMetadataService _exifMetadataService;

    public PhotoCoordinateReader(IExifMetadataService exifMetadataService)
    {
        _exifMetadataService = exifMetadataService ?? throw new ArgumentNullException(nameof(exifMetadataService));
    }

    public async Task<PhotoCoordinate?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var metadata = await _exifMetadataService.GetMetadataAsync(filePath, cancellationToken).ConfigureAwait(false);
        return metadata is { HasValidLocation: true, Latitude: { } latitude, Longitude: { } longitude }
            ? new PhotoCoordinate(latitude, longitude)
            : null;
    }
}
