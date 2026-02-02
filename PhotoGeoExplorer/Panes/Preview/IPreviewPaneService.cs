using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// Preview Pane専用のサービスインターフェース
/// 画像ロード、フィッティング計算などの処理を定義
/// </summary>
internal interface IPreviewPaneService
{
    /// <summary>
    /// 画像を非同期で読み込む
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <returns>BitmapImage オブジェクト</returns>
    Task<BitmapImage?> LoadImageAsync(string filePath);

    /// <summary>
    /// ビューポートに画像をフィットさせるズームファクターを計算
    /// </summary>
    /// <param name="imageWidth">画像の幅（ピクセル）</param>
    /// <param name="imageHeight">画像の高さ（ピクセル）</param>
    /// <param name="viewportWidth">ビューポートの幅（DIP）</param>
    /// <param name="viewportHeight">ビューポートの高さ（DIP）</param>
    /// <param name="minZoom">最小ズームファクター</param>
    /// <param name="maxZoom">最大ズームファクター</param>
    /// <returns>計算されたズームファクター</returns>
    float CalculateFitZoomFactor(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        float minZoom,
        float maxZoom);

    /// <summary>
    /// DPI スケーリング変更時のズームファクター補正を計算
    /// </summary>
    /// <param name="currentZoom">現在のズームファクター</param>
    /// <param name="oldScale">変更前の DPI スケール</param>
    /// <param name="newScale">変更後の DPI スケール</param>
    /// <param name="minZoom">最小ズームファクター</param>
    /// <param name="maxZoom">最大ズームファクター</param>
    /// <returns>補正されたズームファクター</returns>
    float CalculateDpiCorrectedZoom(
        float currentZoom,
        double oldScale,
        double newScale,
        float minZoom,
        float maxZoom);
}
