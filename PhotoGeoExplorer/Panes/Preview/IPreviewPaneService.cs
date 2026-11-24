using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// Preview Pane専用のサービスインターフェース
/// 画像ロードなどの処理を定義
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
    /// 指定ファイルの EXIF メタデータを非同期で読み込む
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>PhotoMetadata オブジェクト</returns>
    Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken);
}
