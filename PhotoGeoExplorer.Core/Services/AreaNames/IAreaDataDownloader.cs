namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 都道府県単位の地域データを e-Stat から取得して取り込む
/// </summary>
internal interface IAreaDataDownloader
{
    /// <summary>
    /// ZIP を一時ファイルへ保存し、<see cref="IAreaDataStore.ImportAsync"/> に渡す。成功・失敗・中止のいずれでも一時ファイルを残さない
    /// </summary>
    /// <param name="prefectureCode">都道府県コード（2 桁）</param>
    /// <param name="progress">受信済みのバイト数（サイズは取得前には分からない）</param>
    /// <exception cref="AreaDataDownloadException">ダウンロードに失敗した</exception>
    /// <exception cref="AreaDataFormatException">取得した内容を地域データとして取り込めない</exception>
    Task<AreaDataPackInfo> DownloadAsync(string prefectureCode, IProgress<long>? progress, CancellationToken cancellationToken);
}
