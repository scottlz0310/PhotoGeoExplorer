namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 利用者が取り込んだ地域データ（e-Stat の小地域境界データ）の管理
/// </summary>
internal interface IAreaDataStore
{
    Task<IReadOnlyList<AreaDataPackInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// e-Stat からダウンロードした ZIP を検証して取り込む。同じ都道府県のデータがあれば置き換える
    /// </summary>
    /// <exception cref="AreaDataFormatException">小地域の境界データとして取り込めない形式</exception>
    /// <exception cref="IOException">ファイルを読み書きできなかった</exception>
    Task<AreaDataPackInfo> ImportAsync(string archivePath, CancellationToken cancellationToken);

    /// <returns>削除した場合は true。取り込まれていなければ false</returns>
    Task<bool> DeleteAsync(string prefectureCode, CancellationToken cancellationToken);
}
