using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class PhotoListItem : BindableBase
{
    private BitmapImage? _thumbnail;
    private string? _thumbnailKey;
    private int _generation;
    private int? _pixelWidth;
    private int? _pixelHeight;
    private DateTimeOffset? _takenAt;
    private bool? _hasLocation;

    public PhotoListItem(PhotoItem item, BitmapImage? thumbnail, string? toolTipText = null, string? thumbnailKey = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _thumbnail = thumbnail;
        ToolTipText = toolTipText;
        _thumbnailKey = thumbnailKey;
        _generation = 0;
        _pixelWidth = item.PixelWidth;
        _pixelHeight = item.PixelHeight;
        _takenAt = item.TakenAt;
        _hasLocation = item.HasLocation;
    }

    public PhotoItem Item { get; }
    public string FilePath => Item.FilePath;
    public string FileName => Item.FileName;
    public string SizeText => Item.SizeText;
    public string ModifiedAtText => Item.ModifiedAtText;
    public DateTimeOffset? TakenAt => _takenAt;
    public bool? HasLocation => _hasLocation;
    public string TakenAtText => _takenAt?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
    public string? LocationStatusGlyph => _hasLocation switch
    {
        true => "\uE707",
        false => "\uE711",
        _ => null
    };
    public string? LocationStatusTooltip => _hasLocation switch
    {
        true => LocalizationService.GetString("StatusBar.GpsAvailable"),
        false => LocalizationService.GetString("StatusBar.GpsMissing"),
        _ => null
    };
    public Visibility LocationStatusVisibility => IsFolder || _hasLocation is null
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string ResolutionText
    {
        get
        {
            if (IsFolder || _pixelWidth is null || _pixelHeight is null)
            {
                return string.Empty;
            }

            if (_pixelWidth <= 0 || _pixelHeight <= 0)
            {
                return string.Empty;
            }

            return $"{_pixelWidth} x {_pixelHeight}";
        }
    }
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

    public int? PixelWidth => _pixelWidth;
    public int? PixelHeight => _pixelHeight;

    public bool HasThumbnail => _thumbnail is not null;

    public Visibility ThumbnailVisibility => IsFolder || _thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlaceholderVisibility => IsFolder || _thumbnail is not null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FolderIconVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public bool UpdateThumbnail(BitmapImage? thumbnail, string? expectedKey, int expectedGeneration, int? width = null, int? height = null)
    {
        // 世代とキーが一致する場合のみ更新
        if (_generation != expectedGeneration || _thumbnailKey != expectedKey)
        {
            return false;
        }

        Thumbnail = thumbnail;

        // 解像度を更新
        if (width.HasValue && height.HasValue)
        {
            _pixelWidth = width;
            _pixelHeight = height;
            OnPropertyChanged(nameof(PixelWidth));
            OnPropertyChanged(nameof(PixelHeight));
            OnPropertyChanged(nameof(ResolutionText));
        }

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

    public bool UpdateMetadata(DateTimeOffset? takenAt, bool? hasLocation)
    {
        if (_takenAt == takenAt && _hasLocation == hasLocation)
        {
            return false;
        }

        _takenAt = takenAt;
        _hasLocation = hasLocation;
        OnPropertyChanged(nameof(TakenAt));
        OnPropertyChanged(nameof(HasLocation));
        OnPropertyChanged(nameof(TakenAtText));
        OnPropertyChanged(nameof(LocationStatusGlyph));
        OnPropertyChanged(nameof(LocationStatusTooltip));
        OnPropertyChanged(nameof(LocationStatusVisibility));
        return true;
    }
}
