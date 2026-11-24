using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// Preview Pane専用のサービス
/// I/O処理とビジネスロジックを分離
/// </summary>
internal sealed class PreviewPaneService : IPreviewPaneService
{
    /// <summary>
    /// 画像を非同期で読み込む
    /// </summary>
    public async Task<BitmapImage?> LoadImageAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            AppLog.Error($"Image file not found: {filePath}");
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(filePath);
            using var randomAccessStream = stream.AsRandomAccessStream();
            await bitmap.SetSourceAsync(randomAccessStream).AsTask().ConfigureAwait(false);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Error($"Failed to load image: {filePath}", ex);
            return null;
        }
    }

    /// <summary>
    /// 指定ファイルの EXIF メタデータを非同期で読み込む
    /// </summary>
    public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken)
        => ExifReader.GetMetadataAsync(filePath, cancellationToken);
}
