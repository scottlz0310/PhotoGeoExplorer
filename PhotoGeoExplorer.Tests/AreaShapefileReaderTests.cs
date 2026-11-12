using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// e-Stat の境界データと同じ属性構成の Shapefile を一時フォルダーへ書き出して読む。実データには依存しない
/// </summary>
public sealed class AreaShapefileReaderTests : IDisposable
{
    // e-Stat の令和 2 年 小地域境界データ（世界測地系・緯度経度）の .prj と同じ内容
    private const string GeographicProjection =
        "GEOGCS[\"GCS_JGD_2011\",DATUM[\"D_JGD_2011\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";

    private const string ProjectedProjection =
        "PROJCS[\"JGD_2011_Japan_Zone_5\",GEOGCS[\"GCS_JGD_2011\",DATUM[\"D_JGD_2011\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],UNIT[\"Meter\",1.0]]";

    private static readonly GeometryFactory Factory = new();
    private static readonly string[] AllAttributes = ["KEY_CODE", "PREF_NAME", "CITY_NAME", "S_NAME", "HCODE"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_AreaShapefileReaderTests_" + Guid.NewGuid().ToString("N"));

    static AreaShapefileReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public AreaShapefileReaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ReadLoadsRegionsWithShiftJisNamesAndTheWaterCategory()
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0),
            CreateFeature("312010870", "東品治町", hcode: 8101, west: 134.1),
            CreateFeature("31201999900", "水面", hcode: 8154, west: 134.2));

        var dataset = AreaShapefileReader.Read(path, "31", 2020);

        Assert.Equal("31", dataset.PrefectureCode);
        Assert.Equal(2020, dataset.CensusYear);
        Assert.Equal(3, dataset.RegionCount);

        var land = dataset.Find(35.05, 134.05);
        Assert.NotNull(land);
        Assert.Equal(("31201005001", "鳥取県", "鳥取市", "東町一丁目"), (land.Name.KeyCode, land.Name.Prefecture, land.Name.City, land.Name.Town));
        Assert.False(land.Name.SnappedToNearest);

        // 9 桁のキーコード（町丁の下位区分がない小地域）も、そのまま読む
        Assert.Equal("312010870", dataset.Find(35.05, 134.15)?.Name.KeyCode);

        // 水面の中央は、30m 以内に陸がないので水面のまま
        Assert.Equal("水面", dataset.Find(35.05, 134.25)?.Name.Town);

        // 水面の端は、隣の陸（東品治町）へ寄せる
        var nearLand = dataset.Find(35.05, 134.2001);
        Assert.NotNull(nearLand);
        Assert.Equal("東品治町", nearLand.Name.Town);
        Assert.True(nearLand.Name.SnappedToNearest);
    }

    [Theory]
    [InlineData("KEY_CODE")]
    [InlineData("PREF_NAME")]
    [InlineData("CITY_NAME")]
    [InlineData("S_NAME")]
    [InlineData("HCODE")]
    public void ReadRejectsAShapefileLackingARequiredAttribute(string missingAttribute)
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes.Where(name => name != missingAttribute).ToArray(),
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.MissingAttribute, exception.Error);
        Assert.Contains(missingAttribute, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    // 平面直角座標系の版
    [InlineData(ProjectedProjection, nameof(AreaDataFormatError.UnsupportedProjection))]
    // .prj がない
    [InlineData(null, nameof(AreaDataFormatError.MissingProjection))]
    [InlineData("  ", nameof(AreaDataFormatError.MissingProjection))]
    public void ReadRejectsAShapefileThatIsNotInLatitudeAndLongitude(string? projection, string expectedError)
    {
        var path = WriteShapefile(
            projection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(expectedError, exception.Error.ToString());
    }

    [Fact]
    public void ReadRejectsAShapefileWithoutPolygons()
    {
        var point = new Feature(Factory.CreatePoint(new Coordinate(134.0, 35.0)), CreateAttributes("31201005001", "東町一丁目", 8101));
        var path = WriteShapefile(GeographicProjection, AllAttributes, point);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.NoRegions, exception.Error);
    }

    [Fact]
    public void ReadThrowsAnIOExceptionWhenTheFileDoesNotExist()
    {
        Assert.ThrowsAny<IOException>(() => AreaShapefileReader.Read(Path.Combine(_directory, "missing.shp"), "31", 2020));
    }

    private static Feature CreateFeature(string keyCode, string town, int hcode, double west)
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

    private static AttributesTable CreateAttributes(string keyCode, string town, int hcode)
        => new()
        {
            { "KEY_CODE", keyCode },
            { "PREF_NAME", "鳥取県" },
            { "CITY_NAME", "鳥取市" },
            { "S_NAME", town },
            { "HCODE", hcode }
        };

    private string WriteShapefile(string? projection, IReadOnlyCollection<string> attributeNames, params Feature[] features)
    {
        var path = Path.Combine(_directory, "r2ka31.shp");
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

        // e-Stat の配布物には .cpg がない（文字コードは .dbf のヘッダーから判定される）
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
