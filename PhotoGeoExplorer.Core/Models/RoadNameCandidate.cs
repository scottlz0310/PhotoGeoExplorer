namespace PhotoGeoExplorer.Models;

/// <summary>
/// 撮影地点の最寄り道路名の候補。最寄りが撮影対象の道路とは限らないため、確定値としては扱わない
/// </summary>
/// <param name="Name">データにある名称（国道は「国道53号」の形で入っている）。名称がなく路線番号だけの道路は null</param>
/// <param name="Ref">路線番号。複数の路線が重なる道路は「・」でつなぐ（「53・373」）。なければ null。種別（国道・県道など）は含まない</param>
/// <param name="DistanceMeters">地点から道路の線までの最短距離（メートル）</param>
/// <remarks>
/// <paramref name="Name"/> と <paramref name="Ref"/> の少なくとも一方がある。名称のない道路の表記（「路線番号 203」）は、表示側が言語に合わせて整える
/// </remarks>
internal sealed record RoadNameCandidate(string? Name, string? Ref, double DistanceMeters);
