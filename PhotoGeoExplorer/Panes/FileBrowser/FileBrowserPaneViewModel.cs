using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// ファイルブラウザPane の ViewModel
/// ファイル一覧の表示、ナビゲーション、ソート、フィルタを管理
/// </summary>
internal sealed class FileBrowserPaneViewModel : PaneViewModelBase, IDisposable
{
    private const int ThumbnailGenerationConcurrency = 3;
    private const int ThumbnailUpdateBatchIntervalMs = 300;

    private readonly IFileBrowserPaneService _service;
    private DispatcherQueue? _dispatcherQueue;
    private readonly WorkspaceState _workspaceState;
    private readonly SemaphoreSlim _thumbnailGenerationSemaphore = new(ThumbnailGenerationConcurrency, ThumbnailGenerationConcurrency);
    private readonly HashSet<string> _thumbnailsInProgress = new();
    private readonly object _thumbnailsInProgressLock = new();
    private readonly List<(PhotoListItem Item, string? ThumbnailPath, string? Key, int Generation, int? Width, int? Height)> _pendingThumbnailUpdates = new();
    private readonly object _pendingThumbnailUpdatesLock = new();

    private string? _currentFolderPath;
    private string? _statusMessage;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private bool _showImagesOnly = true;
    private string? _searchText;
    private FileViewMode _fileViewMode = FileViewMode.Details;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool _hasActiveFilters;
    private PhotoListItem? _selectedItem;
    private readonly List<PhotoListItem> _selectedItems = new();
    private int _selectedCount;
    private PhotoMetadata? _selectedMetadata;
    private CancellationTokenSource? _metadataCts;
    private CancellationTokenSource? _loadFolderCts;
    private string? _statusTitle;
    private string? _statusDetail;
    private Symbol _statusSymbol = Symbol.Help;
    private StatusAction _statusPrimaryAction;
    private StatusAction _statusSecondaryAction;
    private string? _statusPrimaryActionLabel;
    private string? _statusSecondaryActionLabel;
    private Visibility _statusPrimaryActionVisibility = Visibility.Collapsed;
    private Visibility _statusSecondaryActionVisibility = Visibility.Collapsed;
    private string? _statusBarText;
    private string? _statusBarLocationGlyph;
    private Visibility _statusBarLocationVisibility = Visibility.Collapsed;
    private string? _statusBarLocationTooltip;
    private int _thumbnailGenerationTotal;
    private int _thumbnailGenerationCompleted;
    private CancellationTokenSource? _thumbnailGenerationCts;
    private DispatcherQueueTimer? _thumbnailUpdateTimer;

    public FileBrowserPaneViewModel()
        : this(new FileBrowserPaneService(), new WorkspaceState())
    {
    }

    internal FileBrowserPaneViewModel(IFileBrowserPaneService service, WorkspaceState workspaceState)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(workspaceState);

        _service = service;
        _workspaceState = workspaceState;
        _dispatcherQueue = TryGetDispatcherQueue();

        // WorkspaceState にナビゲーションコールバックを設定
        _workspaceState.SelectNextAction = SelectNext;
        _workspaceState.SelectPreviousAction = SelectPrevious;

        Title = "File Browser";
        Items = new ObservableCollection<PhotoListItem>();
        BreadcrumbItems = new ObservableCollection<BreadcrumbSegment>();

        NavigateBackCommand = new RelayCommand(async () => await NavigateBackAsync().ConfigureAwait(false), () => CanNavigateBack);
        NavigateForwardCommand = new RelayCommand(async () => await NavigateForwardAsync().ConfigureAwait(false), () => CanNavigateForward);
        NavigateUpCommand = new RelayCommand(async () => await NavigateUpAsync().ConfigureAwait(false), () => CanNavigateUp);
        NavigateHomeCommand = new RelayCommand(async () => await OpenHomeAsync().ConfigureAwait(false));
        RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(false));
        ToggleSortCommand = new RelayCommand<FileSortColumn>(column =>
        {
            ToggleSort(column);
            return Task.CompletedTask;
        });
        ResetFiltersCommand = new RelayCommand(async () =>
        {
            ResetFilters();
            await Task.CompletedTask.ConfigureAwait(false);
        });
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
                OnPropertyChanged(nameof(CanNavigateUp));
                OnPropertyChanged(nameof(CanCreateFolder));
                OnPropertyChanged(nameof(CanMoveToParentSelection));
                UpdateNavigationCommands();
                UpdateStatusBar();

                // WorkspaceState に反映
                _workspaceState.CurrentFolderPath = value;
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

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
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

    public FileSortColumn SortColumn
    {
        get => _sortColumn;
        private set => SetProperty(ref _sortColumn, value);
    }

    public SortDirection SortDirection
    {
        get => _sortDirection;
        private set => SetProperty(ref _sortDirection, value);
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

    public IReadOnlyList<PhotoListItem> SelectedItems => _selectedItems;

    public PhotoListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnSelectedItemChanged();
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

    public ICommand NavigateBackCommand { get; }
    public ICommand NavigateForwardCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand NavigateHomeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleSortCommand { get; }
    public ICommand ResetFiltersCommand { get; }

    public bool CanNavigateBack => _service.CanNavigateBack;
    public bool CanNavigateForward => _service.CanNavigateForward;
    public bool CanNavigateUp => !string.IsNullOrWhiteSpace(CurrentFolderPath) && Directory.GetParent(CurrentFolderPath) is not null;
    public bool CanCreateFolder => !string.IsNullOrWhiteSpace(CurrentFolderPath);
    public bool CanModifySelection => SelectedCount > 0;
    public bool CanRenameSelection => SelectedCount == 1;
    public bool CanMoveToParentSelection
        => SelectedCount > 0
           && !string.IsNullOrWhiteSpace(CurrentFolderPath)
           && Directory.GetParent(CurrentFolderPath) is not null;

    public string NameSortIndicator => GetSortIndicator(FileSortColumn.Name);
    public string ModifiedSortIndicator => GetSortIndicator(FileSortColumn.ModifiedAt);
    public string ResolutionSortIndicator => GetSortIndicator(FileSortColumn.Resolution);
    public string SizeSortIndicator => GetSortIndicator(FileSortColumn.Size);

    protected override async Task OnInitializeAsync()
    {
        // 初期化処理（必要に応じて実装）
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        await OpenHomeAsync().ConfigureAwait(false);
    }

    protected override void OnCleanup()
    {
        Dispose();
    }

    protected override void OnActiveChanged()
    {
        // Paneがアクティブになったときの処理
    }

    public async Task LoadFolderAsync(string folderPath, bool updateHistory = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.FolderPathEmpty"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.FolderNotFound"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
            return;
        }

        // 既存の読み込み処理をキャンセル
        var previousCts = _loadFolderCts;
        var cts = new CancellationTokenSource();
        _loadFolderCts = cts;

        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(false);
            previousCts.Dispose();
        }

        try
        {
            var previousPath = CurrentFolderPath;
            await RunOnUIThreadAsync(() =>
            {
                CurrentFolderPath = folderPath;
                UpdateBreadcrumbs(folderPath);
                SetStatus(null, InfoBarSeverity.Informational);
                SelectedItem = null;
                UpdateSelection(Array.Empty<PhotoListItem>());
            }).ConfigureAwait(false);

            var items = await _service.LoadFolderAsync(folderPath, ShowImagesOnly, SearchText).ConfigureAwait(false);

            // キャンセルされた場合は処理を中断
            cts.Token.ThrowIfCancellationRequested();

            var sorted = _service.ApplySort(items, SortColumn, SortDirection);

            await RunOnUIThreadAsync(() =>
            {
                Items.Clear();
                foreach (var item in sorted)
                {
                    Items.Add(item);
                }

                // 履歴管理（updateHistory が true で、かつ前のパスと異なる場合のみ）
                if (updateHistory && !string.IsNullOrWhiteSpace(previousPath) && previousPath != folderPath)
                {
                    _service.PushToBackStack(previousPath);
                    _service.ClearForwardStack();
                    UpdateNavigationCommands();
                }

                SetStatus(
                    Items.Count == 0 ? LocalizationService.GetString("Message.NoFilesFound") : null,
                    InfoBarSeverity.Informational);
                UpdateStatusBar();

                // バックグラウンドでサムネイル生成を開始
                // 注意: StartBackgroundThumbnailGeneration 内部で DispatcherQueue を使用してタイマーを構成しているため、
                //       ここでは明示的に UI スレッド上から呼び出している（UI 更新と意図を揃えるため）
                StartBackgroundThumbnailGeneration();
            }).ConfigureAwait(false);

            AppLog.Info($"LoadFolderAsync: Folder '{folderPath}' loaded successfully. Item count: {Items.Count}");
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は想定された動作のため、何もしない
            AppLog.Info($"LoadFolderAsync: Folder load cancelled for '{folderPath}'");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to access folder: {folderPath}", ex);
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.AccessDeniedSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error($"Folder not found: {folderPath}", ex);
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.FolderNotFoundSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read folder: {folderPath}", ex);
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.FailedReadFolderSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
    }

    public async Task OpenHomeAsync()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(homePath) || !Directory.Exists(homePath))
        {
            await RunOnUIThreadAsync(() =>
            {
                SetStatus(LocalizationService.GetString("Message.PicturesFolderNotFound"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
            return;
        }

        await LoadFolderAsync(homePath).ConfigureAwait(false);
    }

    public async Task NavigateBackAsync()
    {
        if (!CanNavigateBack || string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        var previousPath = _service.NavigateBack(CurrentFolderPath);
        if (previousPath is not null)
        {
            await LoadFolderAsync(previousPath, updateHistory: false).ConfigureAwait(false);
            await RunOnUIThreadAsync(UpdateNavigationCommands).ConfigureAwait(false);
        }
    }

    public async Task NavigateForwardAsync()
    {
        if (!CanNavigateForward || string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        var nextPath = _service.NavigateForward(CurrentFolderPath);
        if (nextPath is not null)
        {
            await LoadFolderAsync(nextPath, updateHistory: false).ConfigureAwait(false);
            await RunOnUIThreadAsync(UpdateNavigationCommands).ConfigureAwait(false);
        }
    }

    public async Task NavigateUpAsync()
    {
        if (!CanNavigateUp || string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        var parent = Directory.GetParent(CurrentFolderPath);
        if (parent is not null)
        {
            await LoadFolderAsync(parent.FullName).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        await LoadFolderAsync(CurrentFolderPath, updateHistory: false).ConfigureAwait(false);
    }

    public void ToggleSort(FileSortColumn column)
    {
        if (SortColumn == column)
        {
            SortDirection = SortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        else
        {
            SortColumn = column;
            SortDirection = SortDirection.Ascending;
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

    public void ResetFilters()
    {
        SearchText = null;
        ShowImagesOnly = true;
    }

    public void UpdateSelection(IReadOnlyList<PhotoListItem> items)
    {
        _selectedItems.Clear();
        if (items.Count > 0)
        {
            _selectedItems.AddRange(items);
        }

        SelectedCount = _selectedItems.Count;

        // WorkspaceState に選択状態を反映（写真のみ）
        var selectedPhotos = _selectedItems
            .Where(item => !item.IsFolder)
            .ToList();
        _workspaceState.SelectedPhotos = selectedPhotos;
        _workspaceState.SelectedPhotoCount = selectedPhotos.Count;

        UpdatePhotoListInfo();
        UpdateStatusBar();
    }

    private void UpdatePhotoListInfo()
    {
        var photoItems = Items.Where(item => !item.IsFolder).ToList();
        _workspaceState.PhotoListCount = photoItems.Count;

        if (_selectedItems.Count == 1 && !_selectedItems[0].IsFolder)
        {
            var selectedPhoto = _selectedItems[0];
            var index = photoItems.FindIndex(item =>
                string.Equals(item.FilePath, selectedPhoto.FilePath, StringComparison.OrdinalIgnoreCase));
            _workspaceState.CurrentPhotoIndex = index;
        }
        else if (SelectedItem is not null && !SelectedItem.IsFolder)
        {
            var index = photoItems.FindIndex(item =>
                string.Equals(item.FilePath, SelectedItem.FilePath, StringComparison.OrdinalIgnoreCase));
            _workspaceState.CurrentPhotoIndex = index;
        }
        else
        {
            _workspaceState.CurrentPhotoIndex = -1;
        }
    }

    private void SelectRelative(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var photoItems = Items.Where(item => !item.IsFolder).ToList();
        if (photoItems.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedItem is null
            ? (delta > 0 ? -1 : photoItems.Count)
            : photoItems.FindIndex(item =>
                string.Equals(item.FilePath, SelectedItem.FilePath, StringComparison.OrdinalIgnoreCase));
        var targetIndex = currentIndex + delta;
        if (targetIndex < 0 || targetIndex >= photoItems.Count)
        {
            return;
        }

        SelectedItem = photoItems[targetIndex];
    }

    public void Dispose()
    {
        CancelThumbnailGeneration();
        CancelMetadataLoad();
        CancelFolderLoad();
        _thumbnailGenerationSemaphore.Dispose();
    }

    private void UpdateBreadcrumbs(string folderPath)
    {
        var breadcrumbs = _service.GetBreadcrumbs(folderPath);
        BreadcrumbItems.Clear();
        foreach (var segment in breadcrumbs)
        {
            BreadcrumbItems.Add(segment);
        }
    }

    private void ApplySorting()
    {
        if (Items.Count == 0)
        {
            return;
        }

        var sorted = _service.ApplySort(Items, SortColumn, SortDirection);
        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }
    }

    private string GetSortIndicator(FileSortColumn column)
    {
        if (SortColumn != column)
        {
            return string.Empty;
        }

        return SortDirection == SortDirection.Ascending ? "▲" : "▼";
    }

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(ModifiedSortIndicator));
        OnPropertyChanged(nameof(ResolutionSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
    }

    private void UpdateFilterState()
    {
        HasActiveFilters = !string.IsNullOrWhiteSpace(SearchText) || !ShowImagesOnly;
        UpdateStatusOverlay(StatusMessage, _statusSeverity);

        // フィルタ変更時は現在のフォルダを再読み込み（履歴には追加しない）
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            _ = LoadFolderAsync(CurrentFolderPath, updateHistory: false);
        }
    }

    private void SetStatus(string? message, InfoBarSeverity severity)
    {
        _statusSeverity = severity;
        StatusMessage = message;
        StatusSeverity = severity;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
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

    private void UpdateNavigationCommands()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        (NavigateBackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NavigateForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NavigateUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnSelectedItemChanged()
    {
        if (SelectedItem is null)
        {
            if (_selectedItems.Count > 0)
            {
                UpdateSelection(Array.Empty<PhotoListItem>());
            }
        }
        else if (_selectedItems.Count == 0 || !_selectedItems.Contains(SelectedItem))
        {
            UpdateSelection(new List<PhotoListItem> { SelectedItem });
        }

        UpdateStatusBar();
        _ = LoadMetadataAsync(SelectedItem);
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

        if (_selectedMetadata?.HasLocation == true)
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationGlyph = "\uE707";
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsAvailable");
        }
        else if (_selectedMetadata is null)
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

    private async Task LoadMetadataAsync(PhotoListItem? item)
    {
        var previousCts = _metadataCts;
        _metadataCts = null;
        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(false);
            previousCts.Dispose();
        }

        if (item is null || item.IsFolder)
        {
            _selectedMetadata = null;
            await RunOnUIThreadAsync(UpdateStatusBarLocation).ConfigureAwait(false);
            return;
        }

        _selectedMetadata = null;
        await RunOnUIThreadAsync(UpdateStatusBarLocation).ConfigureAwait(false);

        var cts = new CancellationTokenSource();
        _metadataCts = cts;

        try
        {
            var metadata = await ExifService.GetMetadataAsync(item.FilePath, cts.Token).ConfigureAwait(false);
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            _selectedMetadata = metadata;
            await RunOnUIThreadAsync(UpdateStatusBarLocation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // メタデータ読み込み処理がキャンセルされた場合は想定された動作のため、何もしない
        }
    }

    private void CancelMetadataLoad()
    {
        var previousCts = _metadataCts;
        _metadataCts = null;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済み
            }
        }
    }

    private void CancelFolderLoad()
    {
        var previousCts = _loadFolderCts;
        _loadFolderCts = null;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済み
            }
        }
    }

    private void StartBackgroundThumbnailGeneration()
    {
        // 既存の生成処理をキャンセル
        CancelThumbnailGeneration();

        // テスト環境またはUIスレッドがない場合はスキップ
        if (_dispatcherQueue is null)
        {
            return;
        }

        // サムネイルが未生成のアイテムを収集
        var itemsNeedingThumbnails = Items
            .Where(item => !item.IsFolder && item.Thumbnail is null && item.ThumbnailKey is not null)
            .ToList();

        if (itemsNeedingThumbnails.Count == 0)
        {
            return;
        }

        // カウンターを初期化
        _thumbnailGenerationTotal = itemsNeedingThumbnails.Count;
        _thumbnailGenerationCompleted = 0;

        // 更新タイマーの初期化
        _thumbnailUpdateTimer = _dispatcherQueue.CreateTimer();
        _thumbnailUpdateTimer.Interval = TimeSpan.FromMilliseconds(ThumbnailUpdateBatchIntervalMs);
        _thumbnailUpdateTimer.Tick += OnThumbnailUpdateTimerTick;
        _thumbnailUpdateTimer.Start();

        // 新しいキャンセルトークンを作成
        var cts = new CancellationTokenSource();
        _thumbnailGenerationCts = cts;

        AppLog.Info($"StartBackgroundThumbnailGeneration: Starting generation for {itemsNeedingThumbnails.Count} items");

        // バックグラウンドで並列生成開始
        _ = Task.Run(async () =>
        {
            var tasks = itemsNeedingThumbnails.Select(listItem => GenerateThumbnailAsync(listItem, cts.Token));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            AppLog.Info("StartBackgroundThumbnailGeneration: Completed");
        }, cts.Token);
    }

    private async Task GenerateThumbnailAsync(PhotoListItem listItem, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var key = listItem.ThumbnailKey;
        if (key is null)
        {
            return;
        }

        // 重複生成を防止
        lock (_thumbnailsInProgressLock)
        {
            if (_thumbnailsInProgress.Contains(key))
            {
                return;
            }

            _thumbnailsInProgress.Add(key);
        }

        try
        {
            // セマフォで並列数を制限
            await _thumbnailGenerationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // サムネイル生成（バックグラウンドスレッド）
                var fileInfo = new FileInfo(listItem.FilePath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var result = ThumbnailService.GenerateThumbnail(listItem.FilePath, fileInfo.LastWriteTimeUtc);
                if (result.ThumbnailPath is null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // UIスレッドで BitmapImage を作成して更新をキューに追加
                lock (_pendingThumbnailUpdatesLock)
                {
                    _pendingThumbnailUpdates.Add((listItem, result.ThumbnailPath, key, listItem.Generation, result.Width, result.Height));
                }
            }
            finally
            {
                _thumbnailGenerationSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: Access denied for {listItem.FileName}", ex);
        }
        catch (IOException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: IO error for {listItem.FileName}", ex);
        }
        catch (NotSupportedException ex)
        {
            AppLog.Error($"GenerateThumbnailAsync: Unsupported operation for {listItem.FileName}", ex);
        }
        finally
        {
            lock (_thumbnailsInProgressLock)
            {
                _thumbnailsInProgress.Remove(key);
            }

            // 完了カウントをインクリメント
            Interlocked.Increment(ref _thumbnailGenerationCompleted);
        }
    }

    private void OnThumbnailUpdateTimerTick(DispatcherQueueTimer sender, object args)
    {
        ApplyPendingThumbnailUpdates();
    }

    private void ApplyPendingThumbnailUpdates()
    {
        // まず、生成完了チェックを実行（キューの有無に関わらず）
        var shouldStopTimer = Volatile.Read(ref _thumbnailGenerationCompleted) >= _thumbnailGenerationTotal;

        List<(PhotoListItem Item, string? ThumbnailPath, string? Key, int Generation, int? Width, int? Height)> updates;

        lock (_pendingThumbnailUpdatesLock)
        {
            // キューが空の場合、完了チェックのみ実行
            if (_pendingThumbnailUpdates.Count == 0)
            {
                if (shouldStopTimer && _thumbnailUpdateTimer is not null)
                {
                    _thumbnailUpdateTimer.Stop();
                    AppLog.Info("ApplyPendingThumbnailUpdates: All thumbnail generation tasks finished, stopping timer (queue empty)");
                }
                return;
            }

            updates = new List<(PhotoListItem, string?, string?, int, int?, int?)>(_pendingThumbnailUpdates);
            _pendingThumbnailUpdates.Clear();
        }

        var successCount = 0;
        foreach (var (item, thumbnailPath, key, generation, width, height) in updates)
        {
            // UIスレッドでBitmapImageを作成
            var thumbnail = CreateThumbnailImage(thumbnailPath);
            if (thumbnail is not null && item.UpdateThumbnail(thumbnail, key, generation, width, height))
            {
                successCount++;
            }
        }

        if (successCount > 0)
        {
            AppLog.Info($"ApplyPendingThumbnailUpdates: Applied {successCount} thumbnail updates");
        }

        // 生成完了チェック後、キューも確認してタイマーを停止
        if (shouldStopTimer)
        {
            lock (_pendingThumbnailUpdatesLock)
            {
                if (_pendingThumbnailUpdates.Count == 0 && _thumbnailUpdateTimer is not null)
                {
                    _thumbnailUpdateTimer.Stop();
                    AppLog.Info("ApplyPendingThumbnailUpdates: All thumbnail generation tasks finished, stopping timer");
                }
            }
        }
    }

    private void CancelThumbnailGeneration()
    {
        // タイマーを停止
        if (_thumbnailUpdateTimer is not null)
        {
            _thumbnailUpdateTimer.Stop();
            _thumbnailUpdateTimer.Tick -= OnThumbnailUpdateTimerTick;
            _thumbnailUpdateTimer = null;
        }

        // 保留中の更新をクリア
        lock (_pendingThumbnailUpdatesLock)
        {
            _pendingThumbnailUpdates.Clear();
        }

        // 生成中リストをクリア
        lock (_thumbnailsInProgressLock)
        {
            _thumbnailsInProgress.Clear();
        }

        // キャンセルトークンをキャンセル
        var previousCts = _thumbnailGenerationCts;
        _thumbnailGenerationCts = null;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済み
            }
        }
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
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
        catch (UriFormatException ex)
        {
            AppLog.Error($"Failed to load thumbnail image. Path: '{thumbnailPath}'", ex);
            return null;
        }
    }

    private static DispatcherQueue? TryGetDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (COMException ex)
        {
            AppLog.Info($"DispatcherQueue is unavailable in this environment: {ex.Message}");
            return null;
        }
        catch (TypeInitializationException ex)
        {
            AppLog.Info($"DispatcherQueue initialization failed: {ex.Message}");
            return null;
        }
    }

    internal void SetDispatcherQueue(DispatcherQueue? dispatcherQueue)
    {
        if (dispatcherQueue is null)
        {
            return;
        }

        _dispatcherQueue = dispatcherQueue;
    }

    private Task RunOnUIThreadAsync(Action action)
    {
        if (_dispatcherQueue is null)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
            }))
        {
            var ex = new InvalidOperationException("DispatcherQueue へのエンキューに失敗しました。");
            AppLog.Error("RunOnUIThreadAsync: DispatcherQueue.TryEnqueue が false を返しました。", ex);
            tcs.SetException(ex);
        }
        return tcs.Task;
    }
}
