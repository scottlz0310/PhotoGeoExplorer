using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal interface IExifMetadataService
{
    Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken);

    Task<bool> UpdateMetadataAsync(
        string filePath,
        DateTimeOffset? takenAt,
        double? latitude,
        double? longitude,
        bool updateFileModifiedDate,
        CancellationToken cancellationToken);
}
