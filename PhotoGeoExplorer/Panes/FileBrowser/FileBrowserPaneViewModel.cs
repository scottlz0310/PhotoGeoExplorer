using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly WorkspaceState _workspaceState;
    private readonly SemaphoreSlim _thumbnailGenerationSemaphore = new(ThumbnailGenerationConcurrency, ThumbnailGenerationConcurrency);
    private readonly HashSet<string> _thumbnailsInProgress = new();
    private readonly object _thumbnailsInProgressLock = new();
    private readonly List<(PhotoListItem Item, string? ThumbnailPath, string? Key, int Generation, int? Width, int? Height)> _pendingThumbnailUpdates = new();
    private readonly object _pendingThumbnailUpdatesLock = new();

    private string? _currentFolderPath;
    private string? _statusMessage;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private bool _showImagesOnly = true;
    private string? _searchText;
    private FileViewMode _fileViewMode = FileViewMode.Details;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool _hasActiveFilters;
    private PhotoListItem? _selectedItem;
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

        Title = "File Browser";
        Items = new ObservableCollection<PhotoListItem>();
        BreadcrumbItems = new ObservableCollection<BreadcrumbSegment>();

        NavigateBackCommand = new RelayCommand(async () => await NavigateBackAsync().ConfigureAwait(false), () => CanNavigateBack);
        NavigateForwardCommand = new RelayCommand(async () => await NavigateForwardAsync().ConfigureAwait(false), () => CanNavigateForward);
        NavigateUpCommand = new RelayCommand(async () => await NavigateUpAsync().ConfigureAwait(false), () => CanNavigateUp);
        NavigateHomeCommand = new RelayCommand(async () => await OpenHomeAsync().ConfigureAwait(false));
        RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(false));
        ToggleSortCommand = new RelayCommand<FileSortColumn>(column => ToggleSort(column));
        ResetFiltersCommand = new RelayCommand(ResetFilters);
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
                UpdateNavigationCommands();

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
        set => SetProperty(ref _fileViewMode, value);
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
            var previousPath = CurrentFolderPath;
            CurrentFolderPath = folderPath;
            UpdateBreadcrumbs(folderPath);
            SetStatus(null, InfoBarSeverity.Informational);
            SelectedItem = null;

            var items = await _service.LoadFolderAsync(folderPath, ShowImagesOnly, SearchText).ConfigureAwait(false);
            var sorted = _service.ApplySort(items, SortColumn, SortDirection);

            // UI スレッドでコレクションを更新
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue is not null)
            {
                var tcs = new TaskCompletionSource<bool>();
                dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        Items.Clear();
                        foreach (var item in sorted)
                        {
                            Items.Add(item);
                        }

                        // 履歴管理
                        if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != folderPath)
                        {
                            _service.PushToBackStack(previousPath);
                            _service.ClearForwardStack();
                            UpdateNavigationCommands();
                        }

                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                        throw;
                    }
                });
                await tcs.Task.ConfigureAwait(false);
            }
            else
            {
                // テスト環境の場合
                Items.Clear();
                foreach (var item in sorted)
                {
                    Items.Add(item);
                }

                // 履歴管理
                if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != folderPath)
                {
                    _service.PushToBackStack(previousPath);
                    _service.ClearForwardStack();
                    UpdateNavigationCommands();
                }
            }

            SetStatus(
                Items.Count == 0 ? LocalizationService.GetString("Message.NoFilesFound") : null,
                InfoBarSeverity.Informational);

            // バックグラウンドでサムネイル生成を開始
            StartBackgroundThumbnailGeneration();

            AppLog.Info($"LoadFolderAsync: Folder '{folderPath}' loaded successfully. Item count: {Items.Count}");
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
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read folder: {folderPath}", ex);
            SetStatus(LocalizationService.GetString("Message.FailedReadFolderSeeLog"), InfoBarSeverity.Error);
        }
    }

    public async Task OpenHomeAsync()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(homePath) || !Directory.Exists(homePath))
        {
            SetStatus(LocalizationService.GetString("Message.PicturesFolderNotFound"), InfoBarSeverity.Error);
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
            await LoadFolderAsync(previousPath).ConfigureAwait(false);
            UpdateNavigationCommands();
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
            await LoadFolderAsync(nextPath).ConfigureAwait(false);
            UpdateNavigationCommands();
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

        await LoadFolderAsync(CurrentFolderPath).ConfigureAwait(false);
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

    public void ResetFilters()
    {
        SearchText = null;
        ShowImagesOnly = true;
    }

    public void Dispose()
    {
        CancelThumbnailGeneration();
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
    }

    private void SetStatus(string? message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
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
        // 選択された写真を WorkspaceState に反映
        var selectedPhotos = SelectedItem is not null && !SelectedItem.IsFolder
            ? new List<PhotoListItem> { SelectedItem }
            : new List<PhotoListItem>();

        _workspaceState.SelectedPhotos = selectedPhotos;
        _workspaceState.SelectedPhotoCount = selectedPhotos.Count;

        // インデックスを計算（画像のみ）
        if (SelectedItem is not null && !SelectedItem.IsFolder)
        {
            var photoItems = Items.Where(item => !item.IsFolder).ToList();
            var index = photoItems.IndexOf(SelectedItem);
            _workspaceState.CurrentPhotoIndex = index;
            _workspaceState.PhotoListCount = photoItems.Count;
        }
        else
        {
            _workspaceState.CurrentPhotoIndex = -1;
            _workspaceState.PhotoListCount = 0;
        }
    }

    private void StartBackgroundThumbnailGeneration()
    {
        // 既存の生成処理をキャンセル
        CancelThumbnailGeneration();

        // テスト環境またはUIスレッドがない場合はスキップ
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
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
        _thumbnailUpdateTimer = dispatcherQueue.CreateTimer();
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
}
