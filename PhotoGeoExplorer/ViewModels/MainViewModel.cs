using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.ViewModels;

internal sealed class MainViewModel : BindableBase
{
    private readonly FileSystemService _fileSystemService;
    private CancellationTokenSource? _metadataCts;
    private string? _currentFolderPath;
    private string? _statusMessage;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private PhotoListItem? _selectedItem;
    private PhotoMetadata? _selectedMetadata;
    private BitmapImage? _selectedPreview;
    private Visibility _previewPlaceholderVisibility = Visibility.Visible;
    private bool _showImagesOnly = true;
    private string? _searchText;
    private string? _metadataSummary;
    private Visibility _metadataVisibility = Visibility.Collapsed;
    private string? _statusBarText;
    private string? _notificationMessage;
    private InfoBarSeverity _notificationSeverity = InfoBarSeverity.Informational;
    private bool _isNotificationOpen;
    private FileViewMode _fileViewMode = FileViewMode.Details;

    public MainViewModel(FileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        Items = new ObservableCollection<PhotoListItem>();
        BreadcrumbItems = new ObservableCollection<BreadcrumbSegment>();
    }

    public ObservableCollection<PhotoListItem> Items { get; }
    public ObservableCollection<BreadcrumbSegment> BreadcrumbItems { get; }

    public string? CurrentFolderPath
    {
        get => _currentFolderPath;
        private set => SetProperty(ref _currentFolderPath, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set => SetProperty(ref _statusVisibility, value);
    }

    public bool ShowImagesOnly
    {
        get => _showImagesOnly;
        set => SetProperty(ref _showImagesOnly, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public FileViewMode FileViewMode
    {
        get => _fileViewMode;
        set
        {
            if (SetProperty(ref _fileViewMode, value))
            {
                OnPropertyChanged(nameof(FileViewModeIndex));
                OnPropertyChanged(nameof(IconViewVisibility));
                OnPropertyChanged(nameof(ListViewVisibility));
                OnPropertyChanged(nameof(DetailsViewVisibility));
            }
        }
    }

    public int FileViewModeIndex
    {
        get => (int)_fileViewMode;
        set
        {
            if (value < 0 || value > 2)
            {
                return;
            }

            FileViewMode = (FileViewMode)value;
        }
    }

    public Visibility IconViewVisibility => _fileViewMode == FileViewMode.Icon ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListViewVisibility => _fileViewMode == FileViewMode.List ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsViewVisibility => _fileViewMode == FileViewMode.Details ? Visibility.Visible : Visibility.Collapsed;

    public PhotoListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                UpdatePreview(value);
                _ = LoadMetadataAsync(value);
            }
        }
    }

    public PhotoMetadata? SelectedMetadata
    {
        get => _selectedMetadata;
        private set => SetProperty(ref _selectedMetadata, value);
    }

    public BitmapImage? SelectedPreview
    {
        get => _selectedPreview;
        private set => SetProperty(ref _selectedPreview, value);
    }

    public Visibility PreviewPlaceholderVisibility
    {
        get => _previewPlaceholderVisibility;
        private set => SetProperty(ref _previewPlaceholderVisibility, value);
    }

    public string? MetadataSummary
    {
        get => _metadataSummary;
        private set => SetProperty(ref _metadataSummary, value);
    }

    public Visibility MetadataVisibility
    {
        get => _metadataVisibility;
        private set => SetProperty(ref _metadataVisibility, value);
    }

    public string? StatusBarText
    {
        get => _statusBarText;
        private set => SetProperty(ref _statusBarText, value);
    }

    public string? NotificationMessage
    {
        get => _notificationMessage;
        private set => SetProperty(ref _notificationMessage, value);
    }

    public InfoBarSeverity NotificationSeverity
    {
        get => _notificationSeverity;
        private set => SetProperty(ref _notificationSeverity, value);
    }

    public bool IsNotificationOpen
    {
        get => _isNotificationOpen;
        set => SetProperty(ref _isNotificationOpen, value);
    }

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        await OpenHomeAsync().ConfigureAwait(true);
    }

    public async Task OpenHomeAsync()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(homePath) || !Directory.Exists(homePath))
        {
            SetStatus("Pictures folder not found.");
            return;
        }

        await LoadFolderAsync(homePath).ConfigureAwait(true);
    }

    public async Task NavigateUpAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        var parent = Directory.GetParent(CurrentFolderPath);
        if (parent is null)
        {
            return;
        }

        await LoadFolderAsync(parent.FullName).ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        await LoadFolderAsync(CurrentFolderPath).ConfigureAwait(true);
    }

    public void SelectNext()
    {
        SelectRelative(1);
    }

    public void SelectPrevious()
    {
        SelectRelative(-1);
    }

    public async Task LoadFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            SetStatus("Folder path is empty.");
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            SetStatus("Folder not found.");
            return;
        }

        try
        {
            CurrentFolderPath = folderPath;
            UpdateBreadcrumbs(folderPath);
            SetStatus(null);
            SelectedItem = null;
            SelectedMetadata = null;
            SetMetadataSummary(null, hasSelection: false);

            var items = await _fileSystemService
                .GetPhotoItemsAsync(folderPath, ShowImagesOnly, SearchText)
                .ConfigureAwait(true);
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(CreateListItem(item));
            }

            SetStatus(Items.Count == 0 ? "No files found." : null);
            UpdateStatusBar();
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to access folder: {folderPath}", ex);
            SetStatus("Access denied. See log.");
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error($"Folder not found: {folderPath}", ex);
            SetStatus("Folder not found. See log.");
        }
        catch (PathTooLongException ex)
        {
            AppLog.Error($"Folder path too long: {folderPath}", ex);
            SetStatus("Folder path too long. See log.");
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read folder: {folderPath}", ex);
            SetStatus("Failed to read folder. See log.");
        }
    }

    private static PhotoListItem CreateListItem(PhotoItem item)
    {
        var thumbnail = CreateThumbnailImage(item.ThumbnailPath);
        return new PhotoListItem(item, thumbnail);
    }

    private static BitmapImage? CreateThumbnailImage(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(thumbnailPath));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to load thumbnail image.", ex);
            return null;
        }
        catch (IOException ex)
        {
            AppLog.Error("Failed to load thumbnail image.", ex);
            return null;
        }
        catch (UriFormatException ex)
        {
            AppLog.Error("Failed to load thumbnail image.", ex);
            return null;
        }
    }

    private void UpdatePreview(PhotoListItem? item)
    {
        if (item is null || item.IsFolder)
        {
            SelectedPreview = null;
            PreviewPlaceholderVisibility = Visibility.Visible;
            UpdateStatusBar();
            return;
        }

        try
        {
            SelectedPreview = new BitmapImage(new Uri(item.FilePath));
            PreviewPlaceholderVisibility = Visibility.Collapsed;
            UpdateStatusBar();
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to load preview image.", ex);
            SelectedPreview = null;
            PreviewPlaceholderVisibility = Visibility.Visible;
            UpdateStatusBar();
        }
        catch (UriFormatException ex)
        {
            AppLog.Error("Failed to load preview image.", ex);
            SelectedPreview = null;
            PreviewPlaceholderVisibility = Visibility.Visible;
            UpdateStatusBar();
        }
    }

    private async Task LoadMetadataAsync(PhotoListItem? item)
    {
        var previousCts = _metadataCts;
        _metadataCts = null;
        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(true);
            previousCts.Dispose();
        }

        if (item is null || item.IsFolder)
        {
            SelectedMetadata = null;
            SetMetadataSummary(null, hasSelection: false);
            return;
        }

        SelectedMetadata = null;
        MetadataSummary = "Loading metadata...";
        MetadataVisibility = Visibility.Visible;

        var cts = new CancellationTokenSource();
        _metadataCts = cts;

        try
        {
            var metadata = await ExifService.GetMetadataAsync(item.FilePath, cts.Token).ConfigureAwait(true);
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            SelectedMetadata = metadata;
            SetMetadataSummary(metadata, hasSelection: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateBreadcrumbs(string folderPath)
    {
        BreadcrumbItems.Clear();

        var root = Path.GetPathRoot(folderPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentPath = root;
        BreadcrumbItems.Add(new BreadcrumbSegment(trimmedRoot, root, FileSystemService.GetChildDirectories(root)));

        var remaining = folderPath[root.Length..];
        if (string.IsNullOrWhiteSpace(remaining))
        {
            return;
        }

        var segments = remaining.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            currentPath = Path.Combine(currentPath, segment);
            BreadcrumbItems.Add(new BreadcrumbSegment(segment, currentPath, FileSystemService.GetChildDirectories(currentPath)));
        }
    }

    private void SetStatus(string? message)
    {
        StatusMessage = message;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        SetNotification(message);
    }

    private void SelectRelative(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedItem is null ? (delta > 0 ? -1 : Items.Count) : Items.IndexOf(SelectedItem);
        var step = delta > 0 ? 1 : -1;
        var targetIndex = currentIndex + step;
        while (targetIndex >= 0 && targetIndex < Items.Count)
        {
            if (!Items[targetIndex].IsFolder)
            {
                SelectedItem = Items[targetIndex];
                return;
            }

            targetIndex += step;
        }
    }

    private void SetMetadataSummary(PhotoMetadata? metadata, bool hasSelection)
    {
        if (!hasSelection)
        {
            MetadataSummary = null;
            MetadataVisibility = Visibility.Collapsed;
            return;
        }

        if (metadata is null)
        {
            MetadataSummary = "Metadata not available.";
            MetadataVisibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(metadata.TakenAtText) && string.IsNullOrWhiteSpace(metadata.CameraSummary))
        {
            MetadataSummary = "Metadata not available.";
            MetadataVisibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(metadata.TakenAtText))
        {
            MetadataSummary = metadata.CameraSummary;
            MetadataVisibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(metadata.CameraSummary))
        {
            MetadataSummary = metadata.TakenAtText;
            MetadataVisibility = Visibility.Visible;
            return;
        }

        MetadataSummary = $"{metadata.TakenAtText} | {metadata.CameraSummary}";
        MetadataVisibility = Visibility.Visible;
    }

    private void UpdateStatusBar()
    {
        var folderLabel = string.IsNullOrWhiteSpace(CurrentFolderPath) ? "No folder selected." : CurrentFolderPath;
        var itemCount = Items.Count;
        var selectedLabel = SelectedItem is null ? null : $"Selected: {SelectedItem.FileName}";
        StatusBarText = selectedLabel is null
            ? $"{folderLabel} | {itemCount} items"
            : $"{folderLabel} | {itemCount} items | {selectedLabel}";
    }

    private void SetNotification(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            NotificationMessage = null;
            IsNotificationOpen = false;
            NotificationSeverity = InfoBarSeverity.Informational;
            return;
        }

        NotificationMessage = message;
        NotificationSeverity = GetNotificationSeverity(message);
        IsNotificationOpen = true;
    }

    private static InfoBarSeverity GetNotificationSeverity(string message)
    {
        if (message.Contains("see log", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return InfoBarSeverity.Error;
        }

        if (message.Contains("no files", StringComparison.OrdinalIgnoreCase))
        {
            return InfoBarSeverity.Informational;
        }

        return InfoBarSeverity.Warning;
    }
}
