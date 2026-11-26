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
/// 地図に重ねる案内カードが避ける高さが、実際に描かれる帰属表示を覆うことを保つ（#395）
/// </summary>
public sealed class MapAttributionLayoutTests
{
    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap, 21)]
    [InlineData(MapTileSourceType.MapboxSatellite, 35)]
    [InlineData(MapTileSourceType.OpenFreeMapVector, 21)]
    [InlineData(MapTileSourceType.GsiStandard, 21)]
    [InlineData(MapTileSourceType.GsiPale, 38)]
    [InlineData(MapTileSourceType.GsiPhoto, 89)]
    internal void ReservedHeightFollowsTheNumberOfStackedLines(MapTileSourceType sourceType, double expected)
    {
        Assert.Equal(expected, MapAttributionLayout.GetReservedHeight(MapBackgroundCatalog.Get(sourceType)));
    }

    [Theory]
    [InlineData(MapTileSourceType.OpenStreetMap)]
    [InlineData(MapTileSourceType.EsriWorldImagery)]
    [InlineData(MapTileSourceType.MapboxSatellite)]
    [InlineData(MapTileSourceType.GsiStandard)]
    [InlineData(MapTileSourceType.GsiPale)]
    [InlineData(MapTileSourceType.GsiPhoto)]
    internal async Task DrawnAttributionStaysWithinTheReservedHeight(MapTileSourceType sourceType)
    {
        const int height = 600;
        var cacheDirectory = Directory.CreateTempSubdirectory("pge-attribution-layout-");
        try
        {
            using var map = new Map();
            map.Navigator.SetSize(800, height);
            using var surface = new MapsuiMapSurface(map);
            using var background = MapsuiMapFactory.CreateBackground(
                new MapBackgroundRequest(sourceType, "PhotoGeoExplorer.Tests", MapboxAccessToken: "test-token"),
                cacheDirectory.FullName);
            surface.ReplaceBackground(background);

            using var stream = await surface.RenderPngAsync(1f, CancellationToken.None).ConfigureAwait(true);

            var drawn = map.GetWidgetsOfMapAndLayers()
                .OfType<HyperlinkWidget>()
                .Where(widget => widget.Envelope is not null && !string.IsNullOrEmpty(widget.Text))
                .ToArray();
            Assert.NotEmpty(drawn);

            var top = drawn.Min(widget => widget.Envelope!.MinY);
            var reserved = MapAttributionLayout.GetReservedHeight(MapBackgroundCatalog.Get(sourceType));
            Assert.InRange(height - top, 0, reserved);
        }
        finally
        {
            cacheDirectory.Delete(recursive: true);
        }
    }
}
