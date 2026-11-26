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
    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, 1f)]
    [InlineData(MapTileSourceType.EsriWorldImagery, 1f)]
    [InlineData(MapTileSourceType.MapboxSatellite, 1f)]
    [InlineData(MapTileSourceType.GsiStandard, 1f)]
    [InlineData(MapTileSourceType.GsiPale, 1.5f)]
    [InlineData(MapTileSourceType.GsiPhoto, 1f)]
    [InlineData(MapTileSourceType.GsiPhoto, 1.5f)]
    internal async Task RasterBackgroundShowsOnlyTheApplicationAttribution(MapTileSourceType sourceType, float pixelDensity)
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

            using var stream = await surface.RenderPngAsync(pixelDensity, CancellationToken.None).ConfigureAwait(true);

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
