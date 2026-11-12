using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 座標から最寄り道路名の候補を求める
/// </summary>
internal interface IRoadNameSuggester
{
    /// <returns>近い順の候補。なければ空</returns>
    /// <exception cref="IOException">キャッシュを読めなかった</exception>
    /// <exception cref="HttpRequestException">タイルを取得できなかった</exception>
    Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
