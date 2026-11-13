using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaTree;

internal enum PhotoAreaKind
{
    NoLocation,
    NoAreaData,
    Resolved
}

/// <summary>
/// 1 枚の写真の字地名の判定結果
/// </summary>
/// <param name="FilePath">写真のパス</param>
/// <param name="Kind">判定結果の種類</param>
/// <param name="Area"><see cref="PhotoAreaKind.Resolved"/> のときの字地名（水面を含む）。それ以外は null</param>
internal sealed record PhotoAreaEntry(string FilePath, PhotoAreaKind Kind, AreaName? Area);

/// <summary>
/// 判定の対象にする写真
/// </summary>
/// <param name="FilePath">写真のパス</param>
/// <param name="Version">ファイルが変わったかを判別するための値（更新日時とサイズなど）。位置情報の読み取り結果の保持に使う</param>
/// <param name="HasLocation">位置情報の有無が分かっていれば、その値。false なら、ファイルを読まずに位置情報なしとする</param>
internal readonly record struct PhotoRef(string FilePath, string Version, bool? HasLocation);

internal readonly record struct PhotoCoordinate(double Latitude, double Longitude);

/// <summary>
/// 判定の途中経過
/// </summary>
/// <param name="Completed">判定が済んだ写真の数</param>
/// <param name="Total">判定する写真の数</param>
/// <param name="NewEntries">前回の通知から新たに済んだ写真の結果</param>
internal sealed record AreaClassificationProgress(int Completed, int Total, IReadOnlyList<PhotoAreaEntry> NewEntries);
