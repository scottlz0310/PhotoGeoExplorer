using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal sealed class MapImageService : IMapImageService
{
    private const string DefaultFileNamePrefix = "PhotoGeoExplorer_Map_";
    private readonly Func<string, Stream> _outputStreamFactory;

    public MapImageService()
        : this(CreateOutputStream)
    {
    }

    internal MapImageService(Func<string, Stream> outputStreamFactory)
    {
        _outputStreamFactory = outputStreamFactory ?? throw new ArgumentNullException(nameof(outputStreamFactory));
    }

    public string CreateDefaultFileName(DateTimeOffset timestamp)
    {
        return string.Concat(
            DefaultFileNamePrefix,
            timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture),
            MapImageSavePickerOptions.DefaultFileExtension);
    }


    public async Task SavePngAsync(Stream pngStream, string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngStream);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Output file path is required.", nameof(filePath));
        }

        string? temporaryFilePath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(filePath);
            temporaryFilePath = CreateTemporaryFilePath(destinationPath);
            var outputStream = _outputStreamFactory(temporaryFilePath);
            try
            {
                await pngStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await outputStream.DisposeAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryFilePath, destinationPath, overwrite: true);
            temporaryFilePath = null;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            throw new IOException($"Failed to write the map PNG to '{filePath}'.", ex);
        }
        finally
        {
            if (temporaryFilePath is not null)
            {
                File.Delete(temporaryFilePath);
            }
        }
    }

    private static Stream CreateOutputStream(string filePath)
    {
        return new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
    }

    private static string CreateTemporaryFilePath(string destinationPath)
    {
        var directoryPath = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Output file path must include a directory.", nameof(destinationPath));
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directoryPath, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }
}
