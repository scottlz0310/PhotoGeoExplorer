using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal sealed class ExifMetadataService : IExifMetadataService
{
    public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        return ExifService.GetMetadataAsync(filePath, cancellationToken);
    }

    public Task<bool> UpdateMetadataAsync(
        string filePath,
        DateTimeOffset? takenAt,
        double? latitude,
        double? longitude,
        bool updateFileModifiedDate,
        CancellationToken cancellationToken)
    {
        return ExifService.UpdateMetadataAsync(
            filePath,
            takenAt,
            latitude,
            longitude,
            updateFileModifiedDate,
            cancellationToken);
    }
}
