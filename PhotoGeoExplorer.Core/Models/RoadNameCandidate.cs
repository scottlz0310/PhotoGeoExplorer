namespace PhotoGeoExplorer.Models;

/// <summary>
/// 撮影地点の最寄り道路名の候補。最寄りが撮影対象の道路とは限らないため、確定値としては扱わない
/// </summary>
internal sealed record RoadNameCandidate(string Name, string? Ref, double DistanceMeters);
