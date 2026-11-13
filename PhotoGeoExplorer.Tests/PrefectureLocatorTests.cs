using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PrefectureLocatorTests
{
    // 経度 139〜140.1 の A 県（01）と、経度 140〜141 の B 県（02）。経度 140〜140.1 は重なる（県境付近）
    private const string SyntheticData =
        "# comment\n"
        + "\n"
        + "01\tPOLYGON ((139 35, 140.1 35, 140.1 36, 139 36, 139 35))\n"
        + "02\tMULTIPOLYGON (((140 35, 141 35, 141 36, 140 36, 140 35)), ((150 35, 151 35, 151 36, 150 36, 150 35)))\n";

    private static readonly PrefectureLocator EmbeddedLocator = PrefectureLocator.CreateFromEmbeddedData();

    [Theory]
    [InlineData(35.5, 139.5, "01")]
    [InlineData(35.5, 140.5, "02")]
    // 県境付近は両方が候補になり、コード順に並ぶ
    [InlineData(35.5, 140.05, "01,02")]
    // 境界上
    [InlineData(35.0, 139.0, "01")]
    [InlineData(35.5, 140.1, "01,02")]
    // 飛び地（マルチポリゴンの 2 つ目）
    [InlineData(35.5, 150.5, "02")]
    // どの都道府県にも入らない
    [InlineData(35.5, 138.9, "")]
    [InlineData(36.1, 139.5, "")]
    [InlineData(35.5, 145.0, "")]
    [InlineData(-35.5, 139.5, "")]
    public void FindCandidatesReturnsThePrefecturesCoveringThePoint(double latitude, double longitude, string expected)
    {
        var locator = new PrefectureLocator(() => new StringReader(SyntheticData));

        Assert.Equal(expected, string.Join(',', locator.FindCandidates(latitude, longitude)));
    }

    [Fact]
    public void DataIsLoadedOnceOnTheFirstSearch()
    {
        var openCount = 0;
        var locator = new PrefectureLocator(() =>
        {
            openCount++;
            return new StringReader(SyntheticData);
        });

        Assert.Equal(0, openCount);
        locator.FindCandidates(35.5, 139.5);
        locator.FindCandidates(35.5, 140.5);

        Assert.Equal(1, openCount);
    }

    [Theory]
    // タブがない
    [InlineData("01 POLYGON ((139 35, 140 35, 140 36, 139 36, 139 35))")]
    // コードがない
    [InlineData("\tPOLYGON ((139 35, 140 35, 140 36, 139 36, 139 35))")]
    // WKT として読めない
    [InlineData("01\tPOLYGON ((139 35, 140 35")]
    [InlineData("01\tnot a geometry")]
    public void FindCandidatesRejectsInvalidData(string data)
    {
        var locator = new PrefectureLocator(() => new StringReader(data));

        Assert.Throws<InvalidDataException>(() => locator.FindCandidates(35.5, 139.5));
    }

    [Fact]
    public void EmbeddedDataContainsAll47Prefectures()
    {
        using var stream = typeof(PrefectureLocator).Assembly.GetManifestResourceStream(PrefectureLocator.EmbeddedResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var codes = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
            {
                codes.Add(line[..line.IndexOf('\t', StringComparison.Ordinal)]);
            }
        }

        Assert.Equal(Enumerable.Range(1, 47).Select(code => code.ToString("00", System.Globalization.CultureInfo.InvariantCulture)), codes);

        // 同梱するデータなので、大きくなりすぎていないことを確かめる
        Assert.InRange(stream.Length, 1, 1024 * 1024);
    }

    [Theory]
    // 県境から離れた地点は、その都道府県だけが候補になる
    [InlineData(43.0621, 141.3544, "01")] // 札幌市役所
    [InlineData(45.4157, 141.6731, "01")] // 稚内市役所
    [InlineData(35.5039, 134.2381, "31")] // 鳥取県庁
    [InlineData(35.6812, 139.7671, "13")] // 東京駅
    [InlineData(26.2124, 127.6809, "47")] // 那覇市役所
    // 離島
    [InlineData(24.4678, 123.0045, "47")] // 与那国島
    [InlineData(27.0949, 142.1918, "13")] // 小笠原 父島
    [InlineData(34.2027, 129.2875, "42")] // 対馬 厳原
    [InlineData(38.0183, 138.3683, "15")] // 佐渡
    // 県境（多摩川の丸子橋付近）は、東京都と神奈川県の両方が候補になる
    [InlineData(35.5822, 139.6690, "13,14")]
    // 国外・海上
    [InlineData(37.5665, 126.9780, "")] // ソウル
    [InlineData(35.1796, 129.0756, "")] // 釜山
    [InlineData(25.0330, 121.5654, "")] // 台北
    [InlineData(46.9590, 142.7380, "")] // ユジノサハリンスク
    [InlineData(51.5074, -0.1278, "")] // ロンドン
    [InlineData(30.0, 150.0, "")] // 太平洋上
    public void EmbeddedDataLocatesKnownPlaces(double latitude, double longitude, string expected)
    {
        Assert.Equal(expected, string.Join(',', EmbeddedLocator.FindCandidates(latitude, longitude)));
    }
}
