using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class PhotoListItem : BindableBase
{
    private BitmapImage? _thumbnail;
    private string? _thumbnailKey;
    private int _generation;

    public PhotoListItem(PhotoItem item, BitmapImage? thumbnail, string? toolTipText = null, string? thumbnailKey = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _thumbnail = thumbnail;
        ToolTipText = toolTipText;
        _thumbnailKey = thumbnailKey;
        _generation = 0;
    }

    public PhotoItem Item { get; }
    public string FilePath => Item.FilePath;
    public string FileName => Item.FileName;
    public string SizeText => Item.SizeText;
    public string ModifiedAtText => Item.ModifiedAtText;
    public string ResolutionText => Item.ResolutionText;
    public bool IsFolder => Item.IsFolder;
    public string? ToolTipText { get; }
    
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public string? ThumbnailKey
    {
        get => _thumbnailKey;
        private set => SetProperty(ref _thumbnailKey, value);
    }

    public int Generation => _generation;

    public bool HasThumbnail => _thumbnail is not null;
    
    public Visibility ThumbnailVisibility => IsFolder || _thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlaceholderVisibility => IsFolder || _thumbnail is not null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FolderIconVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public bool UpdateThumbnail(BitmapImage? thumbnail, string? expectedKey, int expectedGeneration)
    {
        // 世代とキーが一致する場合のみ更新
        if (_generation != expectedGeneration || _thumbnailKey != expectedKey)
        {
            return false;
        }

        Thumbnail = thumbnail;
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(ThumbnailVisibility));
        OnPropertyChanged(nameof(PlaceholderVisibility));
        return true;
    }

    public void SetThumbnailKey(string? key)
    {
        ThumbnailKey = key;
        _generation++;
    }
}
