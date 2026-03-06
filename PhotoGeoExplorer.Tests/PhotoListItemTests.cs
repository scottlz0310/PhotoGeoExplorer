using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Tests;

public sealed class PhotoListItemTests
{
    [Fact]
    public void ConstructorInitializesProperties()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: "Test", thumbnailKey: "key123");

        Assert.Equal(photoItem, listItem.Item);
        Assert.Null(listItem.Thumbnail);
        Assert.Equal("Test", listItem.ToolTipText);
        Assert.Equal("key123", listItem.ThumbnailKey);
        Assert.Equal(0, listItem.Generation);
    }

    [Fact]
    public void UpdateThumbnailReturnsFalseForMismatchedGeneration()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key123");

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "key123", expectedGeneration: 1);

        Assert.False(result);
        Assert.Null(listItem.Thumbnail);
    }

    [Fact]
    public void UpdateThumbnailReturnsFalseForMismatchedKey()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key123");

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "wrongkey", expectedGeneration: 0);

        Assert.False(result);
        Assert.Null(listItem.Thumbnail);
    }

    [Fact]
    public void UpdateThumbnailReturnsFalseWhenNoThumbnailAndNoResolutionUpdate()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key123");

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "key123", expectedGeneration: 0);

        Assert.False(result);
        Assert.Null(listItem.Thumbnail);
    }

    [Fact]
    public void SetThumbnailKeyIncrementsGeneration()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key1");

        Assert.Equal(0, listItem.Generation);
        Assert.Equal("key1", listItem.ThumbnailKey);

        listItem.SetThumbnailKey("key2");

        Assert.Equal(1, listItem.Generation);
        Assert.Equal("key2", listItem.ThumbnailKey);

        listItem.SetThumbnailKey("key3");

        Assert.Equal(2, listItem.Generation);
        Assert.Equal("key3", listItem.ThumbnailKey);
    }

    [Fact]
    public void HasThumbnailReturnsFalseWhenThumbnailIsNull()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.False(listItem.HasThumbnail);
    }

    [Fact]
    public void ThumbnailVisibilityCollapsedWhenIsFolder()
    {
        var photoItem = new PhotoItem("C:\\test\\folder", 0, DateTimeOffset.UtcNow, isFolder: true);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Collapsed, listItem.ThumbnailVisibility);
    }

    [Fact]
    public void ThumbnailVisibilityCollapsedWhenThumbnailIsNull()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Collapsed, listItem.ThumbnailVisibility);
    }

    [Fact]
    public void PlaceholderVisibilityCollapsedWhenIsFolder()
    {
        var photoItem = new PhotoItem("C:\\test\\folder", 0, DateTimeOffset.UtcNow, isFolder: true);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Collapsed, listItem.PlaceholderVisibility);
    }

    [Fact]
    public void PlaceholderVisibilityVisibleWhenThumbnailIsNull()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Visible, listItem.PlaceholderVisibility);
    }

    [Fact]
    public void FolderIconVisibilityVisibleWhenIsFolder()
    {
        var photoItem = new PhotoItem("C:\\test\\folder", 0, DateTimeOffset.UtcNow, isFolder: true);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Visible, listItem.FolderIconVisibility);
    }

    [Fact]
    public void FolderIconVisibilityCollapsedWhenNotFolder()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(Visibility.Collapsed, listItem.FolderIconVisibility);
    }

    [Fact]
    public void UpdateThumbnailUpdatesResolution()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key123");

        Assert.Equal(string.Empty, listItem.ResolutionText);

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "key123", expectedGeneration: 0, width: 1920, height: 1080);

        Assert.True(result);
        Assert.Equal("1920 x 1080", listItem.ResolutionText);
    }

    [Fact]
    public void ResolutionTextReturnsFormattedValue()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false, pixelWidth: 1920, pixelHeight: 1080);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal("1920 x 1080", listItem.ResolutionText);
    }

    [Fact]
    public void PixelWidthAndHeightAreExposed()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false, pixelWidth: 1920, pixelHeight: 1080);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);

        Assert.Equal(1920, listItem.PixelWidth);
        Assert.Equal(1080, listItem.PixelHeight);
    }

    [Fact]
    public void PixelWidthAndHeightUpdateWithThumbnail()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: null, thumbnailKey: "key123");

        Assert.Null(listItem.PixelWidth);
        Assert.Null(listItem.PixelHeight);

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "key123", expectedGeneration: 0, width: 1920, height: 1080);

        Assert.True(result);
        Assert.Equal(1920, listItem.PixelWidth);
        Assert.Equal(1080, listItem.PixelHeight);
    }

    [Fact]
    public void UpdateMetadataUpdatesTakenAtAndLocationProperties()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);
        var takenAt = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.Zero);

        var result = listItem.UpdateMetadata(takenAt, hasLocation: true);

        Assert.True(result);
        Assert.Equal(takenAt, listItem.TakenAt);
        Assert.Equal("2024-01-02 03:04", listItem.TakenAtText);
        Assert.True(listItem.HasLocation);
        Assert.Equal(Symbol.Map, listItem.LocationStatusSymbol);
        Assert.Equal(LocalizationService.GetString("StatusBar.GpsAvailable"), listItem.LocationStatusTooltip);
        Assert.Equal(Visibility.Visible, listItem.LocationStatusVisibility);
    }

    [Fact]
    public void UpdateMetadataSetsFixFailedIconAndTooltip()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null);
        var takenAt = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.Zero);

        var result = listItem.UpdateMetadata(takenAt, hasLocation: false, isLocationFixFailed: true);

        Assert.True(result);
        Assert.False(listItem.HasLocation);
        Assert.Equal(Symbol.Important, listItem.LocationStatusSymbol);
        Assert.Equal(LocalizationService.GetString("StatusBar.GpsFixFailed"), listItem.LocationStatusTooltip);
        Assert.Equal(Visibility.Visible, listItem.LocationStatusVisibility);
    }

    [Fact]
    public void UpdateThumbnailRegeneratesGeneratedToolTipWhenResolutionChanges()
    {
        var photoItem = new PhotoItem("C:\\test\\file.jpg", 1024, DateTimeOffset.UtcNow, isFolder: false);
        var listItem = new PhotoListItem(photoItem, thumbnail: null, toolTipText: "initial", thumbnailKey: "key123", isGeneratedToolTip: true);

        var result = listItem.UpdateThumbnail(thumbnail: null, expectedKey: "key123", expectedGeneration: 0, width: 1920, height: 1080);

        Assert.True(result);
        Assert.NotNull(listItem.ToolTipText);
        Assert.Contains("1920 x 1080", listItem.ToolTipText, StringComparison.Ordinal);
    }
}
