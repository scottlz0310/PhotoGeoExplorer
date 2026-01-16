using System;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
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
}
