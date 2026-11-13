using System;
using System.IO;
using System.Linq;
using System.Text;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class GeoCsvWriterTests
{
    private static readonly GeoCsvLabels Labels = new(
        "ファイル名", "都道府県", "市区町村", "町丁字", "キーコード", "緯度", "経度", "撮影日時", "判定",
        "判定済み", "水面", "地域データなし", "位置情報なし");

    private static readonly AreaName Higashi = new("312010870", "鳥取県", "鳥取市", "東品治町", SnappedToNearest: false, CensusYear: 2020);

    [Fact]
    public void WriteStartsWithABomSoThatExcelOpensItAsUtf8()
    {
        var bytes = Write([]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Single(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void WriteOutputsTheHeaderThenOneRowPerPhotoWithCrlfLineEndings()
    {
        var text = WriteText(
        [
            new PhotoGeoRecord(
                "/src/IMG_0001.jpg",
                new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)),
                new PhotoCoordinate(35.4943, 134.225),
                PhotoAreaKind.Resolved,
                Higashi)
        ]);

        Assert.Equal(
            "ファイル名,都道府県,市区町村,町丁字,キーコード,緯度,経度,撮影日時,判定\r\n" +
            "IMG_0001.jpg,鳥取県,鳥取市,東品治町,312010870,35.494300,134.225000,2024-01-02 03:04:05,判定済み\r\n",
            text);
    }

    [Fact]
    public void WriteLeavesTheAddressBlankForPhotosWithoutAnArea()
    {
        var text = WriteText(
        [
            new PhotoGeoRecord("/src/nodata.jpg", null, new PhotoCoordinate(35.68, 139.76), PhotoAreaKind.NoAreaData, null),
            new PhotoGeoRecord("/src/nogps.jpg", null, null, PhotoAreaKind.NoLocation, null),
            new PhotoGeoRecord(
                "/src/sea.jpg",
                null,
                new PhotoCoordinate(35.8, 134.2),
                PhotoAreaKind.Resolved,
                new AreaName("313010999", "鳥取県", "岩美町", "水面", SnappedToNearest: false, CensusYear: 2020, IsWater: true))
        ]);

        var rows = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.Equal("nodata.jpg,,,,,35.680000,139.760000,,地域データなし", rows[0]);
        Assert.Equal("nogps.jpg,,,,,,,,位置情報なし", rows[1]);
        Assert.Equal("sea.jpg,,,,,35.800000,134.200000,,水面", rows[2]);
    }

    [Theory]
    [InlineData("a,b.jpg", "\"a,b.jpg\"")]
    [InlineData("say \"hi\".jpg", "\"say \"\"hi\"\".jpg\"")]
    [InlineData("two\nlines.jpg", "\"two\nlines.jpg\"")]
    [InlineData("plain.jpg", "plain.jpg")]
    public void WriteQuotesCellsThatContainSpecialCharacters(string fileName, string expectedCell)
    {
        // ファイル名に使えない文字は、パスの区切りを含むものを除けば、実際には , " などが残りうる
        var text = WriteText([new PhotoGeoRecord("/src/" + fileName, null, null, PhotoAreaKind.NoLocation, null)]);

        Assert.StartsWith(expectedCell + ",", text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=1+1.jpg")]
    [InlineData("+cmd.jpg")]
    [InlineData("-2.jpg")]
    [InlineData("@SUM(A1).jpg")]
    public void WriteNeutralizesTextCellsThatExcelCouldTakeAsAFormula(string fileName)
    {
        var text = WriteText([new PhotoGeoRecord("/src/" + fileName, null, null, PhotoAreaKind.NoLocation, null)]);

        Assert.StartsWith("'" + fileName + ",", text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteNeutralizesAnAreaNameThatStartsWithAFormulaCharacter()
    {
        var area = new AreaName("312010001", "鳥取県", "鳥取市", "=HYPERLINK(\"x\")", SnappedToNearest: false, CensusYear: 2020);

        var text = WriteText([new PhotoGeoRecord("/src/a.jpg", null, new PhotoCoordinate(35, 134), PhotoAreaKind.Resolved, area)]);

        Assert.Contains("'=HYPERLINK(\"\"x\"\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteKeepsANegativeCoordinateAsANumber()
    {
        // 緯度経度は数値の列なので、- で始まっても ' を足さない（南半球・西半球の座標を数値のまま読めるように）
        var text = WriteText([new PhotoGeoRecord("/src/a.jpg", null, new PhotoCoordinate(-33.8688, -70.5), PhotoAreaKind.NoAreaData, null)]);

        Assert.Contains(",-33.868800,-70.500000,", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteUsesADotAsTheDecimalSeparatorRegardlessOfTheCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var text = WriteText([new PhotoGeoRecord("/src/a.jpg", null, new PhotoCoordinate(35.5, 134.25), PhotoAreaKind.NoAreaData, null)]);

            Assert.Contains(",35.500000,134.250000,", text, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void WriteDoesNotCloseTheStream()
    {
        using var stream = new MemoryStream();

        GeoCsvWriter.Write(stream, [], Labels);

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void WriteRejectsNull()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => GeoCsvWriter.Write(null!, [], Labels));
        Assert.Throws<ArgumentNullException>(() => GeoCsvWriter.Write(stream, null!, Labels));
        Assert.Throws<ArgumentNullException>(() => GeoCsvWriter.Write(stream, [], null!));
    }

    private static byte[] Write(PhotoGeoRecord[] records)
    {
        using var stream = new MemoryStream();
        GeoCsvWriter.Write(stream, records, Labels);
        return stream.ToArray();
    }

    // BOM を除いた本文
    private static string WriteText(PhotoGeoRecord[] records)
    {
        var bytes = Write(records);
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }
}
