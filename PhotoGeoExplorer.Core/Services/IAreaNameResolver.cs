using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 座標から字地名を判定する
/// </summary>
internal interface IAreaNameResolver
{
    /// <summary>
    /// 地域データが追加・置換・削除され、これまでの判定結果が古くなった
    /// </summary>
    event EventHandler? ResultsInvalidated;

    /// <returns>判定できなければ（データ未導入・日本国外など）null</returns>
    /// <exception cref="IOException">地域データを読めなかった</exception>
    Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
