using System;
using System.Linq;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class AreaTreeBuilderTests
{
    private static readonly AreaName TottoriHigashi = Area("312010870", "鳥取県", "鳥取市", "東品治町");
    private static readonly AreaName TottoriFuyo = Area("312010120", "鳥取県", "鳥取市", "扇町");
    private static readonly AreaName Iwami = Area("313010010", "鳥取県", "岩美町", "浦富");

    [Fact]
    public void BuildReturnsNothingForNoPhotos()
    {
        Assert.Empty(AreaTreeBuilder.Build([]));
    }

    [Fact]
    public void BuildGroupsPhotosByCityAndTownInKeyCodeOrder()
    {
        var nodes = AreaTreeBuilder.Build(
        [
            Resolved("/p/e.jpg", Iwami),
            Resolved("/p/b.jpg", TottoriHigashi),
            Resolved("/p/a.jpg", TottoriFuyo),
            Resolved("/p/c.jpg", TottoriHigashi)
        ]);

        Assert.Equal(["鳥取市", "岩美町"], nodes.Select(node => node.Label));
        Assert.All(nodes, node => Assert.Equal(AreaTreeNodeKind.City, node.Kind));
        Assert.Equal(["31201", "31301"], nodes.Select(node => node.Key));
        Assert.Equal([3, 1], nodes.Select(node => node.Count));

        var tottori = nodes[0];
        Assert.Equal(["扇町", "東品治町"], tottori.Children.Select(node => node.Label));
        Assert.Equal(["312010120", "312010870"], tottori.Children.Select(node => node.Key));
        Assert.Equal([1, 2], tottori.Children.Select(node => node.Count));
        Assert.All(tottori.Children, node => Assert.Equal(AreaTreeNodeKind.Town, node.Kind));
        Assert.All(tottori.Children, node => Assert.Empty(node.Children));
    }

    [Fact]
    public void BuildSortsPathsAndGivesACityThePathsOfAllItsTowns()
    {
        var nodes = AreaTreeBuilder.Build(
        [
            Resolved("/p/c.jpg", TottoriHigashi),
            Resolved("/p/A.jpg", TottoriFuyo),
            Resolved("/p/b.jpg", TottoriHigashi)
        ]);

        Assert.Equal(["/p/A.jpg", "/p/b.jpg", "/p/c.jpg"], nodes[0].FilePaths);
        Assert.Equal(["/p/b.jpg", "/p/c.jpg"], nodes[0].Children[1].FilePaths);
    }

    [Fact]
    public void BuildPlacesSpecialNodesLastInAFixedOrder()
    {
        var nodes = AreaTreeBuilder.Build(
        [
            new PhotoAreaEntry("/p/nolocation.jpg", PhotoAreaKind.NoLocation, null),
            new PhotoAreaEntry("/p/nodata.jpg", PhotoAreaKind.NoAreaData, null),
            Resolved("/p/water.jpg", Area("313010999", "鳥取県", "岩美町", "水面", isWater: true)),
            Resolved("/p/land.jpg", TottoriHigashi)
        ]);

        Assert.Equal(
            [AreaTreeNodeKind.City, AreaTreeNodeKind.Water, AreaTreeNodeKind.NoAreaData, AreaTreeNodeKind.NoLocation],
            nodes.Select(node => node.Kind));
        Assert.All(nodes.Skip(1), node =>
        {
            Assert.Equal(string.Empty, node.Label);
            Assert.Equal(string.Empty, node.Key);
            Assert.Empty(node.Children);
            Assert.Equal(1, node.Count);
        });
        Assert.Equal(["/p/water.jpg"], nodes[1].FilePaths);
    }

    [Theory]
    [InlineData("Water")]
    [InlineData("NoAreaData")]
    [InlineData("NoLocation")]
    public void BuildOmitsSpecialNodesWithoutPhotos(string kindName)
    {
        var kind = Enum.Parse<AreaTreeNodeKind>(kindName);
        var nodes = AreaTreeBuilder.Build([Resolved("/p/land.jpg", TottoriHigashi)]);

        Assert.DoesNotContain(nodes, node => node.Kind == kind);
    }

    [Fact]
    public void BuildKeepsAWaterSurfaceOutOfTheCityOfThePrefecture()
    {
        var nodes = AreaTreeBuilder.Build([Resolved("/p/water.jpg", Area("313010999", "鳥取県", "岩美町", "水面", isWater: true))]);

        var node = Assert.Single(nodes);
        Assert.Equal(AreaTreeNodeKind.Water, node.Kind);
    }

    [Fact]
    public void BuildAddsThePrefectureOnlyWhenTheSameCityNameSpansPrefectures()
    {
        var tokyoChuo = Area("131020010", "東京都", "中央区", "日本橋");
        var osakaChuo = Area("271280010", "大阪府", "中央区", "大手前");
        var nodes = AreaTreeBuilder.Build(
        [
            Resolved("/p/1.jpg", tokyoChuo),
            Resolved("/p/2.jpg", osakaChuo),
            Resolved("/p/3.jpg", TottoriHigashi)
        ]);

        Assert.Equal(["中央区（東京都）", "中央区（大阪府）", "鳥取市"], nodes.Select(node => node.Label));
    }

    [Fact]
    public void BuildDoesNotAddThePrefectureWhenCitiesWithTheSameNameAreInOnePrefecture()
    {
        var nodes = AreaTreeBuilder.Build(
        [
            Resolved("/p/1.jpg", Area("312010010", "鳥取県", "鳥取市", "A町")),
            Resolved("/p/2.jpg", Area("312010020", "鳥取県", "鳥取市", "B町"))
        ]);

        var node = Assert.Single(nodes);
        Assert.Equal("鳥取市", node.Label);
        Assert.Equal(2, node.Children.Count);
    }

    [Theory]
    [InlineData("31201", "31201")]
    [InlineData("312", "312")]
    [InlineData("31201087000", "31201")]
    public void BuildUsesTheFirstFiveDigitsOfTheKeyCodeAsTheCityKey(string keyCode, string expectedCityKey)
    {
        var nodes = AreaTreeBuilder.Build([Resolved("/p/1.jpg", Area(keyCode, "鳥取県", "鳥取市", "東品治町"))]);

        Assert.Equal(expectedCityKey, Assert.Single(nodes).Key);
    }

    [Fact]
    public void BuildRejectsAResolvedEntryWithoutAnArea()
    {
        Assert.Throws<ArgumentException>(() => AreaTreeBuilder.Build([new PhotoAreaEntry("/p/1.jpg", PhotoAreaKind.Resolved, null)]));
    }

    [Fact]
    public void BuildRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AreaTreeBuilder.Build(null!));
    }

    private static AreaName Area(string keyCode, string prefecture, string city, string town, bool isWater = false)
        => new(keyCode, prefecture, city, town, SnappedToNearest: false, CensusYear: 2020, IsWater: isWater);

    private static PhotoAreaEntry Resolved(string path, AreaName area) => new(path, PhotoAreaKind.Resolved, area);
}
