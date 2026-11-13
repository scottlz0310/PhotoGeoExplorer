namespace PhotoGeoExplorer.Models;

/// <summary>
/// 利用者が写真ごとに確定した道路名（最寄り道路の候補から選んだもの）
/// </summary>
/// <param name="Name">名称。名称のない道路は null</param>
/// <param name="Ref">路線番号。なければ null</param>
/// <remarks>
/// <see cref="RoadNameCandidate"/> から距離を除いたもの。<paramref name="Name"/> と <paramref name="Ref"/> の少なくとも一方がある
/// </remarks>
internal sealed record ConfirmedRoadName(string? Name, string? Ref)
{
    public static ConfirmedRoadName From(RoadNameCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ConfirmedRoadName(candidate.Name, candidate.Ref);
    }

    public bool Matches(RoadNameCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Name == candidate.Name && Ref == candidate.Ref;
    }
}
