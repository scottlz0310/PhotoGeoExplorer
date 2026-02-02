using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

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
    /// ビューポートに画像をフィットさせるズームファクターを計算
    /// </summary>
    public float CalculateFitZoomFactor(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        float minZoom,
        float maxZoom)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0f;
        }

        var scaleX = viewportWidth / imageWidth;
        var scaleY = viewportHeight / imageHeight;
        var target = (float)Math.Min(scaleX, scaleY);
        return Math.Clamp(target, minZoom, maxZoom);
    }

    /// <summary>
    /// DPI スケーリング変更時のズームファクター補正を計算
    /// </summary>
    public float CalculateDpiCorrectedZoom(
        float currentZoom,
        double oldScale,
        double newScale,
        float minZoom,
        float maxZoom)
    {
        if (oldScale <= 0 || newScale <= 0)
        {
            return currentZoom;
        }

        var correctedZoom = (float)(currentZoom * oldScale / newScale);
        return Math.Clamp(correctedZoom, minZoom, maxZoom);
    }
}
