using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;
using static PhotoGeoExplorer.Tests.AreaShapefileTestData;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// e-Stat の境界データと同じ属性構成の Shapefile を一時フォルダーへ書き出して読む。実データには依存しない
/// </summary>
public sealed class AreaShapefileReaderTests : IDisposable
{
    private static readonly GeometryFactory Factory = new();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_AreaShapefileReaderTests_" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void ReadDeterminesThePrefectureFromTheKeyCodes()
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0),
            CreateFeature("312010870", "東品治町", hcode: 8101, west: 134.1));

        var dataset = AreaShapefileReader.Read(path, 2020);

        Assert.Equal("31", dataset.PrefectureCode);
        Assert.Equal(2020, dataset.CensusYear);
    }

    [Theory]
    // 県の異なるレコードが混ざっている
    [InlineData("31201005001", "32201005001", "MixedPrefectures")]
    // 先頭 2 桁が都道府県コード（01〜47）ではない
    [InlineData("31201005001", "99201005001", "InvalidKeyCode")]
    [InlineData("31201005001", "00201005001", "InvalidKeyCode")]
    [InlineData("31201005001", "48201005001", "InvalidKeyCode")]
    [InlineData("31201005001", "AB201005001", "InvalidKeyCode")]
    [InlineData("31201005001", "3", "InvalidKeyCode")]
    [InlineData("31201005001", "", "InvalidKeyCode")]
    public void ReadRejectsKeyCodesThatDoNotIdentifyASinglePrefecture(string firstKeyCode, string secondKeyCode, string expectedError)
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature(firstKeyCode, "東町一丁目", hcode: 8101, west: 134.0),
            CreateFeature(secondKeyCode, "東品治町", hcode: 8101, west: 134.1));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, 2020));

        Assert.Equal(expectedError, exception.Error.ToString());
    }

    [Fact]
    public void ReadRejectsDataOfADifferentPrefectureThanExpected()
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0));

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "32", 2020));

        Assert.Equal(AreaDataFormatError.MixedPrefectures, exception.Error);
        Assert.Contains("(32)", exception.Message, StringComparison.Ordinal);
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

    [Theory]
    // 壊れたファイルは、ライブラリの例外ではなく AreaDataFormatException（IOException）で報告する
    [InlineData("r2ka31.dbf", "broken")]
    [InlineData("r2ka31.dbf", "")]
    [InlineData("r2ka31.shp", "broken")]
    [InlineData("r2ka31.shp", "")]
    public void ReadReportsACorruptFileAsAnAreaDataFormatException(string fileName, string content)
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0));
        File.WriteAllText(Path.Combine(_directory, fileName), content);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        Assert.IsAssignableFrom<IOException>(exception);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void ReadReportsAShapefileWithATruncatedAttributeTableAsCorrupt()
    {
        var path = WriteShapefile(
            GeographicProjection,
            AllAttributes,
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0));
        var dbf = Path.Combine(_directory, "r2ka31.dbf");
        File.WriteAllBytes(dbf, File.ReadAllBytes(dbf)[..100]);

        var exception = Assert.Throws<AreaDataFormatException>(() => AreaShapefileReader.Read(path, "31", 2020));

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    [Fact]
    public void ReadThrowsAnIOExceptionWhenTheFileDoesNotExist()
    {
        Assert.ThrowsAny<IOException>(() => AreaShapefileReader.Read(Path.Combine(_directory, "missing.shp"), "31", 2020));
    }

    private string WriteShapefile(string? projection, IReadOnlyCollection<string> attributeNames, params Feature[] features)
        => AreaShapefileTestData.WriteShapefile(_directory, "r2ka31", projection, attributeNames, features);
}
