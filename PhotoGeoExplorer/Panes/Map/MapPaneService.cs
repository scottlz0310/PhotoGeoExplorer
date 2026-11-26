using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// Map Pane 専用のサービス
/// I/O処理とビジネスロジックを分離
/// </summary>
internal sealed class MapPaneService : IMapPaneService
{
    private const int MetadataLoadMaxConcurrency = 4;
    private readonly string _tileCacheRootDirectory;
    private readonly ICrashReportService _crashReportService;

    public MapPaneService()
        : this(GetDefaultTileCacheRootDirectory(), new CrashReportService())
    {
    }

    internal MapPaneService(string tileCacheRootDirectory)
        : this(tileCacheRootDirectory, new CrashReportService())
    {
    }

    internal MapPaneService(string tileCacheRootDirectory, ICrashReportService crashReportService)
    {
        if (string.IsNullOrWhiteSpace(tileCacheRootDirectory))
        {
            throw new ArgumentException("Tile cache root directory is required.", nameof(tileCacheRootDirectory));
        }

        _tileCacheRootDirectory = tileCacheRootDirectory;
        _crashReportService = crashReportService ?? throw new ArgumentNullException(nameof(crashReportService));
    }

    /// <inheritdoc/>
    public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
    {
        // 背景の生成は失敗し得る（引数の不備）ので、地図を作る前に先に作る
        var mapBackground = CreateBackground(background);
        return (mapBackground, MapsuiMapFactory.CreateSurface());
    }

    /// <inheritdoc/>
    public IMapBackground CreateBackground(MapBackgroundRequest background)
        => MapsuiMapFactory.CreateBackground(background, _tileCacheRootDirectory);

    /// <inheritdoc/>
    public IMapImageExporter CreateImageExporter()
        => new MapsuiMapImageExporter(_tileCacheRootDirectory);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
        IReadOnlyList<PhotoListItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return Array.Empty<(PhotoListItem Item, PhotoMetadata? Metadata)>();
        }

        var concurrency = Math.Clamp(Environment.ProcessorCount, 1, MetadataLoadMaxConcurrency);
        concurrency = Math.Min(concurrency, items.Count);

        // semaphore は WhenAll で全タスク完了後に破棄されるため安全
#pragma warning disable CA2025
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new Task<(PhotoListItem Item, PhotoMetadata? Metadata)>[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            tasks[index] = LoadMetadataForItemAsync(item, semaphore, cancellationToken);
        }
#pragma warning restore CA2025

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    /// <inheritdoc/>
    public string GetTileCacheRootDirectory()
    {
        return _tileCacheRootDirectory;
    }

    public string GetPinImagePath(PhotoMetadata metadata)
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "MapPins");
        if (metadata.TakenAt is DateTimeOffset takenAt)
        {
            var age = DateTimeOffset.Now - takenAt;
            if (age <= TimeSpan.FromDays(30))
            {
                return Path.Combine(assetsRoot, "green_pin.png");
            }

            if (age <= TimeSpan.FromDays(365))
            {
                return Path.Combine(assetsRoot, "blue_pin.png");
            }
        }

        return Path.Combine(assetsRoot, "red_pin.png");
    }

    public bool FileExistsAtPath(string path)
        => File.Exists(path);

    private async Task<(PhotoListItem Item, PhotoMetadata? Metadata)> LoadMetadataForItemAsync(
        PhotoListItem item,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            if (cancellationToken.IsCancellationRequested)
            {
                return (item, null);
            }

            var metadata = await ExifReader.GetMetadataAsync(item.Item.FilePath, cancellationToken).ConfigureAwait(false);
            return (item, metadata);
        }
        catch (OperationCanceledException)
        {
            return (item, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Error($"Failed to load metadata for {item.Item.FilePath}", ex);
            return (item, null);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // MetadataExtractor 等、想定外の例外でクラッシュする代わりに当該ファイルをスキップする。
            // WriteCrashLog で running.lock を維持し、次回起動時の報告フローへ情報を引き渡す。
            AppLog.Error($"Unexpected exception loading metadata for {item.Item.FilePath}", ex);
            _crashReportService.WriteCrashLog(ex);
            return (item, null);
        }
#pragma warning restore CA1031
        finally
        {
            if (acquired)
            {
                semaphore.Release();
            }
        }
    }

    private static string GetDefaultTileCacheRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoGeoExplorer",
            "Cache",
            "Tiles");
    }
}
