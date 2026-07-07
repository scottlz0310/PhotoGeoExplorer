using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.ViewModels;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapMarkerPresenterTests
{
    [Fact]
    public void ConstructorThrowsWhenServiceIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new MapMarkerPresenter(null!));
    }

    [Fact]
    public void ClearMarkersEmptiesFeatures()
    {
        var (map, markerLayer) = CreateMapWithLayer();
        markerLayer.Features = new[] { new PointFeature(new MPoint(0, 0)) };

        MapMarkerPresenter.ClearMarkers(map, markerLayer);

        Assert.Empty(markerLayer.Features);
    }

    [Fact]
    public void SetMarkerAddsSingleFeatureWithMetadataAndItem()
    {
        var (map, markerLayer) = CreateMapWithLayer();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude: 35.681236, longitude: 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(map, markerLayer, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        var feature = Assert.Single(markerLayer.Features);
        Assert.Same(metadata, feature["PhotoMetadata"]);
        Assert.Same(photoItem, feature["PhotoItem"]);
    }

    [Fact]
    public void SetMarkersAddsFeatureForEachItem()
    {
        var (map, markerLayer) = CreateMapWithLayer();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var items = new List<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)>
        {
            (35.681236, 139.767125, new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125), new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false)),
            (34.702485, 135.495951, new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 34.702485, 135.495951), new PhotoItem(@"C:\Photos\b.jpg", 200, DateTimeOffset.UtcNow, isFolder: false)),
        };

        presenter.SetMarkers(map, markerLayer, items);

        Assert.Equal(2, ((IReadOnlyList<IFeature>)markerLayer.Features).Count);
    }

    [Fact]
    public void SetMarkerUsesPinImageStyleWhenPinFileExists()
    {
        var (map, markerLayer) = CreateMapWithLayer();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: true));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(map, markerLayer, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        var feature = Assert.Single(markerLayer.Features);
        Assert.IsType<ImageStyle>(Assert.Single(feature.Styles));
    }

    [Fact]
    public void SetMarkerUsesFallbackStyleWhenPinFileMissing()
    {
        var (map, markerLayer) = CreateMapWithLayer();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(map, markerLayer, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        var feature = Assert.Single(markerLayer.Features);
        Assert.IsType<SymbolStyle>(Assert.Single(feature.Styles));
    }

    private static (Mapsui.Map Map, MemoryLayer MarkerLayer) CreateMapWithLayer()
    {
#pragma warning disable CA2000 // test-scoped, no explicit disposal required
        var map = new Mapsui.Map();
        var markerLayer = new MemoryLayer { Features = Array.Empty<IFeature>() };
#pragma warning restore CA2000
        map.Layers.Add(markerLayer);
        return (map, markerLayer);
    }

    private sealed class FakeMapPaneService : IMapPaneService
    {
        private readonly bool _pinExists;

        public FakeMapPaneService(bool pinExists)
        {
            _pinExists = pinExists;
        }

        public (Mapsui.Map Map, TileLayer TileLayer, MemoryLayer MarkerLayer) InitializeMap(MapTileSourceType tileSource, string userAgent)
            => throw new NotSupportedException();

        public TileLayer CreateTileLayer(MapTileSourceType sourceType, string userAgent)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(PhotoListItem Item, PhotoMetadata? Metadata)>> LoadPhotoMetadataAsync(
            IReadOnlyList<PhotoListItem> items,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public string GetTileCacheRootDirectory() => throw new NotSupportedException();

        public string GetPinImagePath(PhotoMetadata metadata) => @"C:\Pins\red_pin.png";

        public bool FileExistsAtPath(string path) => _pinExists;
    }
}
