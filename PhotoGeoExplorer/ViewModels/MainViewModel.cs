using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly List<PhotoListItem> _selectedItems = new();
    private readonly Stack<string> _navigationBackStack = new();
    private readonly Stack<string> _navigationForwardStack = new();
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
    private string? _statusBarLocationGlyph;
    private Visibility _statusBarLocationVisibility = Visibility.Collapsed;
    private string? _statusBarLocationTooltip;
    private string? _notificationMessage;
    private InfoBarSeverity _notificationSeverity = InfoBarSeverity.Informational;
    private bool _isNotificationOpen;
    private string? _notificationActionLabel;
    private string? _notificationActionUrl;
    private Visibility _notificationActionVisibility = Visibility.Collapsed;
    private FileViewMode _fileViewMode = FileViewMode.Details;
    private string? _statusTitle;
    private string? _statusDetail;
    private Symbol _statusSymbol = Symbol.Help;
    private StatusAction _statusPrimaryAction;
    private StatusAction _statusSecondaryAction;
    private string? _statusPrimaryActionLabel;
    private string? _statusSecondaryActionLabel;
    private Visibility _statusPrimaryActionVisibility = Visibility.Collapsed;
    private Visibility _statusSecondaryActionVisibility = Visibility.Collapsed;
    private bool _hasActiveFilters;
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private int _selectedCount;
    private bool _isNavigating;

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
        private set
        {
            if (SetProperty(ref _currentFolderPath, value))
            {
                OnPropertyChanged(nameof(CanCreateFolder));
                OnPropertyChanged(nameof(CanMoveToParentSelection));
            }
        }
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
        set
        {
            if (SetProperty(ref _showImagesOnly, value))
            {
                UpdateFilterState();
            }
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                UpdateFilterState();
            }
        }
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
                OnPropertyChanged(nameof(IsIconView));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsDetailsView));
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
    public bool IsIconView
    {
        get => _fileViewMode == FileViewMode.Icon;
        set
        {
            if (value)
            {
                FileViewMode = FileViewMode.Icon;
            }
        }
    }

    public bool IsListView
    {
        get => _fileViewMode == FileViewMode.List;
        set
        {
            if (value)
            {
                FileViewMode = FileViewMode.List;
            }
        }
    }

    public bool IsDetailsView
    {
        get => _fileViewMode == FileViewMode.Details;
        set
        {
            if (value)
            {
                FileViewMode = FileViewMode.Details;
            }
        }
    }

    public string? StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string? StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public Symbol StatusSymbol
    {
        get => _statusSymbol;
        private set => SetProperty(ref _statusSymbol, value);
    }

    public StatusAction StatusPrimaryAction
    {
        get => _statusPrimaryAction;
        private set => SetProperty(ref _statusPrimaryAction, value);
    }

    public StatusAction StatusSecondaryAction
    {
        get => _statusSecondaryAction;
        private set => SetProperty(ref _statusSecondaryAction, value);
    }

    public string? StatusPrimaryActionLabel
    {
        get => _statusPrimaryActionLabel;
        private set => SetProperty(ref _statusPrimaryActionLabel, value);
    }

    public string? StatusSecondaryActionLabel
    {
        get => _statusSecondaryActionLabel;
        private set => SetProperty(ref _statusSecondaryActionLabel, value);
    }

    public Visibility StatusPrimaryActionVisibility
    {
        get => _statusPrimaryActionVisibility;
        private set => SetProperty(ref _statusPrimaryActionVisibility, value);
    }

    public Visibility StatusSecondaryActionVisibility
    {
        get => _statusSecondaryActionVisibility;
        private set => SetProperty(ref _statusSecondaryActionVisibility, value);
    }

    public bool HasActiveFilters
    {
        get => _hasActiveFilters;
        private set => SetProperty(ref _hasActiveFilters, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(CanModifySelection));
                OnPropertyChanged(nameof(CanRenameSelection));
                OnPropertyChanged(nameof(CanMoveToParentSelection));
            }
        }
    }

    public bool CanCreateFolder => !string.IsNullOrWhiteSpace(CurrentFolderPath);
    public bool CanModifySelection => SelectedCount > 0;
    public bool CanRenameSelection => SelectedCount == 1;
    public bool CanMoveToParentSelection
        => SelectedCount > 0
           && !string.IsNullOrWhiteSpace(CurrentFolderPath)
           && Directory.GetParent(CurrentFolderPath) is not null;
    public bool CanNavigateBack => _navigationBackStack.Count > 0;
    public bool CanNavigateForward => _navigationForwardStack.Count > 0;
    public IReadOnlyList<PhotoListItem> SelectedItems => _selectedItems;

    public string NameSortIndicator => GetSortIndicator(FileSortColumn.Name);
    public string ModifiedSortIndicator => GetSortIndicator(FileSortColumn.ModifiedAt);
    public string ResolutionSortIndicator => GetSortIndicator(FileSortColumn.Resolution);
    public string SizeSortIndicator => GetSortIndicator(FileSortColumn.Size);

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

    public string? StatusBarLocationGlyph
    {
        get => _statusBarLocationGlyph;
        private set => SetProperty(ref _statusBarLocationGlyph, value);
    }

    public Visibility StatusBarLocationVisibility
    {
        get => _statusBarLocationVisibility;
        private set => SetProperty(ref _statusBarLocationVisibility, value);
    }

    public string? StatusBarLocationTooltip
    {
        get => _statusBarLocationTooltip;
        private set => SetProperty(ref _statusBarLocationTooltip, value);
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

    public string? NotificationActionLabel
    {
        get => _notificationActionLabel;
        private set => SetProperty(ref _notificationActionLabel, value);
    }

    public string? NotificationActionUrl
    {
        get => _notificationActionUrl;
        private set => SetProperty(ref _notificationActionUrl, value);
    }

    public Visibility NotificationActionVisibility
    {
        get => _notificationActionVisibility;
        private set => SetProperty(ref _notificationActionVisibility, value);
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
            SetStatus(LocalizationService.GetString("Message.PicturesFolderNotFound"), InfoBarSeverity.Error);
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

    public async Task NavigateBackAsync()
    {
        if (_navigationBackStack.Count == 0)
        {
            return;
        }

        var previousPath = _navigationBackStack.Pop();
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            _navigationForwardStack.Push(CurrentFolderPath);
        }

        _isNavigating = true;
        try
        {
            await LoadFolderAsync(previousPath).ConfigureAwait(true);
        }
        finally
        {
            _isNavigating = false;
        }

        UpdateNavigationProperties();
    }

    public async Task NavigateForwardAsync()
    {
        if (_navigationForwardStack.Count == 0)
        {
            return;
        }

        var nextPath = _navigationForwardStack.Pop();
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            _navigationBackStack.Push(CurrentFolderPath);
        }

        _isNavigating = true;
        try
        {
            await LoadFolderAsync(nextPath).ConfigureAwait(true);
        }
        finally
        {
            _isNavigating = false;
        }

        UpdateNavigationProperties();
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        await LoadFolderAsync(CurrentFolderPath).ConfigureAwait(true);
    }

    public void ToggleSort(FileSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = SortDirection.Ascending;
        }

        ApplySorting();
        NotifySortIndicators();
    }

    public void SelectNext()
    {
        SelectRelative(1);
    }

    public void SelectPrevious()
    {
        SelectRelative(-1);
    }

    public void SelectItemByPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var match = Items.FirstOrDefault(item =>
            string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedItem = match;
        }
    }

    public async Task LoadFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            SetStatus(LocalizationService.GetString("Message.FolderPathEmpty"), InfoBarSeverity.Error);
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            SetStatus(LocalizationService.GetString("Message.FolderNotFound"), InfoBarSeverity.Error);
            return;
        }

        try
        {
            // 履歴管理: 通常のナビゲーション時のみ履歴に追加
            if (!_isNavigating && !string.IsNullOrWhiteSpace(CurrentFolderPath))
            {
                _navigationBackStack.Push(CurrentFolderPath);
                _navigationForwardStack.Clear();
                UpdateNavigationProperties();
            }

            CurrentFolderPath = folderPath;
            UpdateBreadcrumbs(folderPath);
            SetStatus(null, InfoBarSeverity.Informational);
            SelectedItem = null;
            SelectedMetadata = null;
            SetMetadataSummary(null, hasSelection: false);
            UpdateSelection(Array.Empty<PhotoListItem>());

            var items = await _fileSystemService
                .GetPhotoItemsAsync(folderPath, ShowImagesOnly, SearchText)
                .ConfigureAwait(true);
            Items.Clear();
            var listItems = items.Select(CreateListItem).ToList();
            foreach (var item in SortItems(listItems))
            {
                Items.Add(item);
            }

            SetStatus(
                Items.Count == 0 ? LocalizationService.GetString("Message.NoFilesFound") : null,
                InfoBarSeverity.Informational);
            UpdateStatusBar();

            // 履歴プロパティを更新
            if (!_isNavigating)
            {
                UpdateNavigationProperties();
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to access folder: {folderPath}", ex);
            SetStatus(LocalizationService.GetString("Message.AccessDeniedSeeLog"), InfoBarSeverity.Error);
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error($"Folder not found: {folderPath}", ex);
            SetStatus(LocalizationService.GetString("Message.FolderNotFoundSeeLog"), InfoBarSeverity.Error);
        }
        catch (PathTooLongException ex)
        {
            AppLog.Error($"Folder path too long: {folderPath}", ex);
            SetStatus(LocalizationService.GetString("Message.FolderPathTooLongSeeLog"), InfoBarSeverity.Error);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read folder: {folderPath}", ex);
            SetStatus(LocalizationService.GetString("Message.FailedReadFolderSeeLog"), InfoBarSeverity.Error);
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
        MetadataSummary = LocalizationService.GetString("Message.MetadataLoading");
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
            UpdateStatusBar();
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
        SetStatus(message, InfoBarSeverity.Informational);
    }

    private void SetStatus(string? message, InfoBarSeverity severity)
    {
        _statusSeverity = severity;
        StatusMessage = message;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        SetNotification(message, severity);
        UpdateStatusOverlay(message, severity);
    }

    private void UpdateStatusOverlay(string? message, InfoBarSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusTitle = null;
            StatusDetail = null;
            StatusSymbol = Symbol.Help;
            SetStatusActions(StatusAction.None, StatusAction.None);
            return;
        }

        if (message == LocalizationService.GetString("Message.NoFilesFound"))
        {
            StatusTitle = LocalizationService.GetString("Overlay.NoFilesFoundTitle");
            StatusDetail = HasActiveFilters
                ? LocalizationService.GetString("Overlay.NoFilesFoundDetailWithFilters")
                : LocalizationService.GetString("Overlay.NoFilesFoundDetail");
            StatusSymbol = Symbol.Pictures;
            SetStatusActions(StatusAction.OpenFolder, HasActiveFilters ? StatusAction.ResetFilters : StatusAction.None);
            return;
        }

        if (severity == InfoBarSeverity.Error)
        {
            StatusTitle = LocalizationService.GetString("Overlay.LoadFolderErrorTitle");
            StatusDetail = message;
            StatusSymbol = Symbol.Folder;
            SetStatusActions(StatusAction.OpenFolder, StatusAction.GoHome);
            return;
        }

        StatusTitle = message;
        StatusDetail = null;
        StatusSymbol = Symbol.Help;
        SetStatusActions(StatusAction.None, StatusAction.None);
    }

    private void SetStatusActions(StatusAction primary, StatusAction secondary)
    {
        StatusPrimaryAction = primary;
        StatusSecondaryAction = secondary;
        StatusPrimaryActionLabel = GetActionLabel(primary);
        StatusSecondaryActionLabel = GetActionLabel(secondary);
        StatusPrimaryActionVisibility = primary == StatusAction.None ? Visibility.Collapsed : Visibility.Visible;
        StatusSecondaryActionVisibility = secondary == StatusAction.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? GetActionLabel(StatusAction action)
    {
        return action switch
        {
            StatusAction.OpenFolder => LocalizationService.GetString("Action.OpenFolder"),
            StatusAction.GoHome => LocalizationService.GetString("Action.GoHome"),
            StatusAction.ResetFilters => LocalizationService.GetString("Action.ResetFilters"),
            _ => null
        };
    }

    private void UpdateFilterState()
    {
        HasActiveFilters = !string.IsNullOrWhiteSpace(SearchText) || !ShowImagesOnly;
        UpdateStatusOverlay(StatusMessage, _statusSeverity);
    }

    public void UpdateSelection(IReadOnlyList<PhotoListItem> items)
    {
        _selectedItems.Clear();
        if (items.Count > 0)
        {
            _selectedItems.AddRange(items);
        }

        SelectedCount = _selectedItems.Count;
    }

    private void ApplySorting()
    {
        if (Items.Count <= 1)
        {
            return;
        }

        var sorted = SortItems(Items);
        for (var index = 0; index < sorted.Count; index++)
        {
            var item = sorted[index];
            var currentIndex = Items.IndexOf(item);
            if (currentIndex >= 0 && currentIndex != index)
            {
                Items.Move(currentIndex, index);
            }
        }
    }

    private List<PhotoListItem> SortItems(IEnumerable<PhotoListItem> items)
    {
        var ordered = items.OrderByDescending(item => item.IsFolder);
        ordered = _sortColumn switch
        {
            FileSortColumn.Name => _sortDirection == SortDirection.Ascending
                ? ordered.ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
                : ordered.ThenByDescending(item => item.FileName, StringComparer.CurrentCultureIgnoreCase),
            FileSortColumn.ModifiedAt => _sortDirection == SortDirection.Ascending
                ? ordered.ThenBy(item => item.Item.ModifiedAt)
                : ordered.ThenByDescending(item => item.Item.ModifiedAt),
            FileSortColumn.Resolution => _sortDirection == SortDirection.Ascending
                ? ordered.ThenBy(item => GetResolutionSortKey(item, ascending: true))
                : ordered.ThenByDescending(item => GetResolutionSortKey(item, ascending: false)),
            FileSortColumn.Size => _sortDirection == SortDirection.Ascending
                ? ordered.ThenBy(item => item.Item.SizeBytes)
                : ordered.ThenByDescending(item => item.Item.SizeBytes),
            _ => ordered
        };

        if (_sortColumn != FileSortColumn.Name)
        {
            ordered = ordered.ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase);
        }

        return ordered.ToList();
    }

    private static long GetResolutionSortKey(PhotoListItem item, bool ascending)
    {
        if (item.Item.PixelWidth is not int width || item.Item.PixelHeight is not int height)
        {
            return ascending ? long.MaxValue : long.MinValue;
        }

        if (width <= 0 || height <= 0)
        {
            return ascending ? long.MaxValue : long.MinValue;
        }

        return (long)width * height;
    }

    private string GetSortIndicator(FileSortColumn column)
    {
        if (_sortColumn != column)
        {
            return string.Empty;
        }

        return _sortDirection == SortDirection.Ascending ? "▲" : "▼";
    }

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(ModifiedSortIndicator));
        OnPropertyChanged(nameof(ResolutionSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
    }

    public void ResetFilters()
    {
        SearchText = null;
        ShowImagesOnly = true;
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
            MetadataSummary = LocalizationService.GetString("Message.MetadataNotAvailable");
            MetadataVisibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(metadata.TakenAtText) && string.IsNullOrWhiteSpace(metadata.CameraSummary))
        {
            MetadataSummary = LocalizationService.GetString("Message.MetadataNotAvailable");
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
        var folderLabel = string.IsNullOrWhiteSpace(CurrentFolderPath)
            ? LocalizationService.GetString("StatusBar.NoFolderSelected")
            : CurrentFolderPath;
        var itemCount = Items.Count;
        var selectedLabel = SelectedItem is null
            ? null
            : LocalizationService.Format("StatusBar.Selected", SelectedItem.FileName);
        var resolutionLabel = SelectedItem is null || SelectedItem.IsFolder ? null : SelectedItem.ResolutionText;

        var itemsLabel = LocalizationService.Format("StatusBar.Items", itemCount);
        var statusText = selectedLabel is null
            ? $"{folderLabel} | {itemsLabel}"
            : $"{folderLabel} | {itemsLabel} | {selectedLabel}";
        if (!string.IsNullOrWhiteSpace(resolutionLabel))
        {
            statusText = $"{statusText} | {resolutionLabel}";
        }

        StatusBarText = statusText;
        UpdateStatusBarLocation();
    }

    private void UpdateStatusBarLocation()
    {
        if (SelectedItem is null || SelectedItem.IsFolder)
        {
            StatusBarLocationVisibility = Visibility.Collapsed;
            StatusBarLocationGlyph = null;
            StatusBarLocationTooltip = null;
            return;
        }

        if (SelectedMetadata?.HasLocation == true)
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationGlyph = "\uE707";
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsAvailable");
        }
        else if (SelectedMetadata is null)
        {
            StatusBarLocationVisibility = Visibility.Collapsed;
            StatusBarLocationGlyph = null;
            StatusBarLocationTooltip = null;
        }
        else
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationGlyph = "\uE711";
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsMissing");
        }
    }

    private void SetNotification(string? message, InfoBarSeverity severity)
    {
        ClearNotificationAction();
        if (string.IsNullOrWhiteSpace(message))
        {
            NotificationMessage = null;
            IsNotificationOpen = false;
            NotificationSeverity = InfoBarSeverity.Informational;
            return;
        }

        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;
    }

    public void ShowNotificationMessage(string message, InfoBarSeverity severity)
    {
        SetNotification(message, severity);
    }

    public void ShowNotificationWithAction(string message, InfoBarSeverity severity, string actionLabel, string actionUrl)
    {
        SetNotification(message, severity);
        NotificationActionLabel = actionLabel;
        NotificationActionUrl = actionUrl;
        NotificationActionVisibility = Visibility.Visible;
    }

    private void ClearNotificationAction()
    {
        NotificationActionLabel = null;
        NotificationActionUrl = null;
        NotificationActionVisibility = Visibility.Collapsed;
    }

    private void UpdateNavigationProperties()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
    }
}
