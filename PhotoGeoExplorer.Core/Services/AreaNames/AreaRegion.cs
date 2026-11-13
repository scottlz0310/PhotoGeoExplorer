using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 小地域（町丁・字等）1 件分のポリゴンと名称
/// </summary>
internal sealed class AreaRegion
{
    public AreaRegion(string keyCode, string prefecture, string city, string town, bool isWater, Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        KeyCode = keyCode;
        Prefecture = prefecture;
        City = city;
        Town = town;
        IsWater = isWater;
        Geometry = geometry;
        Prepared = PreparedGeometryFactory.Prepare(geometry);
    }

    public string KeyCode { get; }

    public string Prefecture { get; }

    public string City { get; }

    public string Town { get; }

    /// <summary>水面調査区か</summary>
    public bool IsWater { get; }

    public Geometry Geometry { get; }

    public IPreparedGeometry Prepared { get; }

    public AreaName ToAreaName(bool snappedToNearest, int censusYear)
        => new(KeyCode, Prefecture, City, Town, snappedToNearest, censusYear, IsWater);
}
