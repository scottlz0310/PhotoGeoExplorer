namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 端末に導入済みの地域データを、都道府県単位で読み込む
/// </summary>
internal interface IAreaDataSource
{
    /// <param name="prefectureCode">都道府県コード（2 桁）</param>
    /// <returns>その都道府県のデータが導入されていなければ null</returns>
    /// <exception cref="IOException">データを読めなかった（形式の不正を含む）</exception>
    Task<AreaDataset?> LoadAsync(string prefectureCode, CancellationToken cancellationToken);
}
