using System;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class PhotoListItem
{
    public PhotoListItem(PhotoItem item, BitmapImage? thumbnail)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Thumbnail = thumbnail;
    }

    public PhotoItem Item { get; }
    public string FilePath => Item.FilePath;
    public string FileName => Item.FileName;
    public string SizeText => Item.SizeText;
    public string ModifiedAtText => Item.ModifiedAtText;
    public BitmapImage? Thumbnail { get; }
}
