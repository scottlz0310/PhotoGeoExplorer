using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class PhotoListItem
{
    public PhotoListItem(PhotoItem item, BitmapImage? thumbnail, string? toolTipText = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Thumbnail = thumbnail;
        ToolTipText = toolTipText;
    }

    public PhotoItem Item { get; }
    public string FilePath => Item.FilePath;
    public string FileName => Item.FileName;
    public string SizeText => Item.SizeText;
    public string ModifiedAtText => Item.ModifiedAtText;
    public string ResolutionText => Item.ResolutionText;
    public bool IsFolder => Item.IsFolder;
    public BitmapImage? Thumbnail { get; }
    public string? ToolTipText { get; }
    public Visibility ThumbnailVisibility => IsFolder ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FolderIconVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;
}
