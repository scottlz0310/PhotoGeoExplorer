using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <param name="DistanceMeters">地点から小地域までの距離。内包していれば 0</param>
/// <param name="IsWater">水面調査区か</param>
internal sealed record AreaMatch(AreaName Name, double DistanceMeters, bool IsWater);
