using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    public void ClearMarkersClearsTheSurface()
    {
        using var surface = new FakeMapSurface();

        MapMarkerPresenter.ClearMarkers(surface);

        Assert.Equal(1, surface.ClearMarkersCount);
    }

    [Fact]
    public void SetMarkerShowsSingleMarkerWithMetadataItemAndZoomLevel()
    {
        using var surface = new FakeMapSurface();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, latitude: 35.681236, longitude: 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(surface, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        var call = Assert.Single(surface.ShowMarkerCalls);
        Assert.Equal(14, call.ZoomLevel);
        Assert.Equal(35.681236, call.Marker.Latitude);
        Assert.Equal(139.767125, call.Marker.Longitude);
        Assert.Same(metadata, call.Marker.Metadata);
        Assert.Same(photoItem, call.Marker.Item);
    }

    [Fact]
    public void SetMarkersShowsAMarkerForEachItem()
    {
        using var surface = new FakeMapSurface();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var items = new List<(double Latitude, double Longitude, PhotoMetadata Metadata, PhotoItem Item)>
        {
            (35.681236, 139.767125, new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125), new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false)),
            (34.702485, 135.495951, new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 34.702485, 135.495951), new PhotoItem(@"C:\Photos\b.jpg", 200, DateTimeOffset.UtcNow, isFolder: false)),
        };

        presenter.SetMarkers(surface, items);

        var markers = Assert.Single(surface.ShowMarkersCalls);
        Assert.Equal(2, markers.Count);
        Assert.Equal(@"C:\Photos\a.jpg", markers[0].Item.FilePath);
        Assert.Equal(34.702485, markers[1].Latitude);
    }

    [Fact]
    public void SetMarkerUsesPinImagePathWhenPinFileExists()
    {
        using var surface = new FakeMapSurface();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: true));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(surface, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        Assert.Equal(@"C:\Pins\red_pin.png", Assert.Single(surface.ShowMarkerCalls).Marker.PinImagePath);
    }

    [Fact]
    public void SetMarkerLeavesPinImagePathEmptyWhenPinFileMissing()
    {
        using var surface = new FakeMapSurface();
        var presenter = new MapMarkerPresenter(new FakeMapPaneService(pinExists: false));
        var metadata = new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 35.681236, 139.767125);
        var photoItem = new PhotoItem(@"C:\Photos\a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false);

        presenter.SetMarker(surface, 35.681236, 139.767125, metadata, photoItem, defaultZoomLevel: 14);

        Assert.Null(Assert.Single(surface.ShowMarkerCalls).Marker.PinImagePath);
    }

    private sealed class FakeMapPaneService : IMapPaneService
    {
        private readonly bool _pinExists;

        public FakeMapPaneService(bool pinExists)
        {
            _pinExists = pinExists;
        }

        public (IMapBackground Background, IMapSurface Surface) InitializeMap(MapBackgroundRequest background)
            => throw new NotSupportedException();

        public IMapImageExporter CreateImageExporter() => throw new NotSupportedException();

        public IMapBackground CreateBackground(MapBackgroundRequest background)
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
