using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 座標から字地名を判定する
/// </summary>
internal interface IAreaNameResolver
{
    /// <returns>判定できなければ（データ未導入・日本国外など）null</returns>
    /// <exception cref="IOException">地域データを読めなかった</exception>
    Task<AreaName?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
