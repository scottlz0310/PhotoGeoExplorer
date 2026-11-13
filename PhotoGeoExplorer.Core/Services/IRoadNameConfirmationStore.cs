using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 利用者が写真ごとに確定した道路名を、アプリのローカルな注釈として保存する。写真のファイルには書かない
/// </summary>
internal interface IRoadNameConfirmationStore
{
    /// <returns>確定した道路名。確定していない、または写真のサイズ・撮影日時が確定したときと違うときは null</returns>
    /// <exception cref="IOException">保存先を読めなかった</exception>
    Task<ConfirmedRoadName?> FindAsync(PhotoIdentity photo, CancellationToken cancellationToken);

    /// <summary>
    /// 道路名を確定する。同じパスの確定済みの値があれば置き換える
    /// </summary>
    /// <exception cref="IOException">保存先へ書けなかった</exception>
    Task SaveAsync(PhotoIdentity photo, ConfirmedRoadName road, CancellationToken cancellationToken);

    /// <summary>
    /// 確定を解除する。確定していなければ何もしない
    /// </summary>
    /// <exception cref="IOException">保存先へ書けなかった</exception>
    Task RemoveAsync(string filePath, CancellationToken cancellationToken);
}
