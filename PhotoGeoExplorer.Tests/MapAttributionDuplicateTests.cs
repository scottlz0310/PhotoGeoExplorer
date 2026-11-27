using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Widgets.ButtonWidgets;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// タイル層は自分の帰属表示を持つため、そのままだとアプリの帰属表示と二重に描かれ、
/// アプリ側のリンクが下に隠れて押せなくなる（#385）
/// </summary>
public sealed class MapAttributionDuplicateTests
{
    // 合成の背景は、航空写真の出典の行の下に、道路データの出典の行を置く（背景を地図へ載せず、通信しない）
    [Theory]
    [InlineData(MapTileSourceType.EsriWorldImageryRoads, 1f)]
    [InlineData(MapTileSourceType.MapboxSatelliteRoads, 1.5f)]
    internal void CompositeBackgroundShowsImageryAndRoadCreditsOnSeparateRows(MapTileSourceType sourceType, float pixelDensity)
    {
        var descriptor = MapBackgroundCatalog.Get(sourceType);
        using var map = new Map();
        map.Navigator.SetSize(800, 600);
        new MapAttributionPresenter().Attach(map, sourceType);

        using var stream = MapsuiMapImageRenderer.RenderPng(map, [.. map.Layers], map.Navigator.Viewport, pixelDensity);

        var drawn = map.GetWidgetsOfMapAndLayers()
            .OfType<HyperlinkWidget>()
            .Where(widget => widget.Envelope is not null && !string.IsNullOrEmpty(widget.Text))
            .ToArray();
        Assert.Equal(descriptor.AttributionLinks.Select(link => link.Text), drawn.Select(widget => widget.Text));
        var imageryCount = descriptor.AttributionRows[0];
        var imagery = drawn.Take(imageryCount).ToArray();
        var roads = drawn.Skip(imageryCount).ToArray();
        Assert.Single(imagery.Select(widget => widget.Envelope!.MinY).Distinct());
        Assert.Single(roads.Select(widget => widget.Envelope!.MinY).Distinct());
        Assert.True(imagery[0].Envelope!.MaxY <= roads[0].Envelope!.MinY);
        Assert.All(drawn, widget => Assert.InRange(widget.Envelope!.MinX, 0, 800));
        for (var first = 0; first < drawn.Length; first++)
        {
            for (var second = first + 1; second < drawn.Length; second++)
            {
                var a = drawn[first].Envelope!;
                var b = drawn[second].Envelope!;
                Assert.False(a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY);
            }
        }
    }

    // 示した最小の大きさで、文言とロゴが画像の中に収まり、互いに重ならないことを、すべての背景で確かめる
    [Theory]
    [MemberData(nameof(AllBackgrounds))]
    public void AttributionFitsTheRequiredSizeWithoutOverlap(int type)
    {
        var sourceType = (MapTileSourceType)type;
        var descriptor = MapBackgroundCatalog.Get(sourceType);
        var (width, height) = MapAttributionPresenter.GetRequiredSize(descriptor);
        using var map = new Map();
        map.Navigator.SetSize(width, height);
        new MapAttributionPresenter().Attach(map, sourceType);

        using var stream = MapsuiMapImageRenderer.RenderPng(map, [.. map.Layers], map.Navigator.Viewport, 2f);

        var drawn = map.GetWidgetsOfMapAndLayers()
            .OfType<HyperlinkWidget>()
            .Where(widget => widget.Envelope is not null && !string.IsNullOrEmpty(widget.Text))
            .Select(widget => widget.Envelope!)
            .ToArray();
        Assert.Equal(descriptor.AttributionLinks.Count, drawn.Length);
        Assert.All(drawn, box =>
        {
            Assert.True(box.MinX >= 0 && box.MaxX <= width, $"x: {box.MinX}-{box.MaxX} / {width}");
            Assert.True(box.MinY >= 0 && box.MaxY <= height, $"y: {box.MinY}-{box.MaxY} / {height}");
        });
        if (descriptor.ShowsMapboxLogo)
        {
            var logoRight = MapAttributionLayout.LogoMargin + MapAttributionLayout.LogoWidth;
            var logoTop = height - MapAttributionLayout.LogoMargin - MapAttributionLayout.LogoHeight;
            Assert.All(drawn, box => Assert.True(box.MinX >= logoRight || box.MaxY <= logoTop));
        }
    }

    public static TheoryData<int> AllBackgrounds => [.. MapBackgroundCatalog.All.Select(descriptor => (int)descriptor.Type)];

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, 1f)]
    [InlineData(MapTileSourceType.EsriWorldImagery, 1f)]
    [InlineData(MapTileSourceType.MapboxSatellite, 1f)]
    [InlineData(MapTileSourceType.GsiStandard, 1f)]
    [InlineData(MapTileSourceType.GsiPale, 1.5f)]
    [InlineData(MapTileSourceType.GsiPhoto, 1f)]
    [InlineData(MapTileSourceType.GsiPhoto, 1.5f)]
    internal void RasterBackgroundShowsOnlyTheApplicationAttribution(MapTileSourceType sourceType, float pixelDensity)
    {
        var cacheDirectory = Directory.CreateTempSubdirectory("pge-attribution-");
        try
        {
            using var map = new Map();
            map.Navigator.SetSize(800, 600);
            using var surface = new MapsuiMapSurface(map);
            using var background = MapsuiMapFactory.CreateBackground(
                new MapBackgroundRequest(sourceType, "PhotoGeoExplorer.Tests", MapboxAccessToken: "test-token"),
                cacheDirectory.FullName);
            surface.ReplaceBackground(background);

            using var stream = MapsuiMapImageRenderer.RenderPng(map, [.. map.Layers], map.Navigator.Viewport, pixelDensity);

            // 描かれたウィジェットだけが位置を持つ。クリックはこの位置で受けるため、描かれていなければ受けない
            var drawn = map.GetWidgetsOfMapAndLayers()
                .OfType<HyperlinkWidget>()
                .Where(widget => widget.Envelope is not null && !string.IsNullOrEmpty(widget.Text))
                .ToArray();

            Assert.Equal(MapBackgroundCatalog.Get(sourceType).AttributionLinks.Select(link => link.Text), drawn.Select(widget => widget.Text));
            foreach (var (first, second) in drawn.Zip(drawn.Skip(1)))
            {
                Assert.False(first.Envelope!.Intersects(second.Envelope));
            }
        }
        finally
        {
            cacheDirectory.Delete(recursive: true);
        }
    }
}
