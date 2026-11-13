using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// e-Stat の境界データと同じ属性構成の Shapefile を、テスト用に書き出す。実データには依存しない
/// </summary>
internal static class AreaShapefileTestData
{
    // e-Stat の令和 2 年 小地域境界データ（世界測地系・緯度経度）の .prj と同じ内容
    public const string GeographicProjection =
        "GEOGCS[\"GCS_JGD_2011\",DATUM[\"D_JGD_2011\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";

    public const string ProjectedProjection =
        "PROJCS[\"JGD_2011_Japan_Zone_5\",GEOGCS[\"GCS_JGD_2011\",DATUM[\"D_JGD_2011\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],UNIT[\"Meter\",1.0]]";

    public static readonly string[] AllAttributes = ["KEY_CODE", "PREF_NAME", "CITY_NAME", "S_NAME", "HCODE"];

    private static readonly GeometryFactory Factory = new();

    static AreaShapefileTestData()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Feature CreateFeature(string keyCode, string town, int hcode, double west)
    {
        // 東西 0.1 度、南北 0.1 度の区画を西から並べる
        var polygon = Factory.CreatePolygon(new[]
        {
            new Coordinate(west, 35.0),
            new Coordinate(west, 35.1),
            new Coordinate(west + 0.1, 35.1),
            new Coordinate(west + 0.1, 35.0),
            new Coordinate(west, 35.0)
        });
        return new Feature(polygon, CreateAttributes(keyCode, town, hcode));
    }

    public static AttributesTable CreateAttributes(string keyCode, string town, int hcode)
        => new()
        {
            { "KEY_CODE", keyCode },
            { "PREF_NAME", "鳥取県" },
            { "CITY_NAME", "鳥取市" },
            { "S_NAME", town },
            { "HCODE", hcode }
        };

    /// <summary>
    /// <paramref name="baseName"/>.shp などを書き出す。属性は Shift_JIS、<c>.cpg</c> はなし（e-Stat の配布物と同じ）。
    /// <paramref name="projection"/> が null なら <c>.prj</c> も書かない
    /// </summary>
    public static string WriteShapefile(
        string directory,
        string baseName,
        string? projection,
        IReadOnlyCollection<string> attributeNames,
        params Feature[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(attributeNames);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, baseName + ".shp");
        var filtered = features.Select(feature =>
        {
            var attributes = new AttributesTable();
            foreach (var name in attributeNames)
            {
                attributes.Add(name, feature.Attributes[name]);
            }

            return (IFeature)new Feature(feature.Geometry, attributes);
        });

        Shapefile.WriteAllFeatures(filtered, path, projection, Encoding.GetEncoding(932));

        File.Delete(Path.ChangeExtension(path, ".cpg"));
        if (string.IsNullOrWhiteSpace(projection))
        {
            File.Delete(Path.ChangeExtension(path, ".prj"));
            if (projection is not null)
            {
                File.WriteAllText(Path.ChangeExtension(path, ".prj"), projection);
            }
        }

        return path;
    }
}
