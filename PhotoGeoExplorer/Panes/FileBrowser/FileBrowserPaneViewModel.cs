using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly IFileBrowserPaneService _service;
    private readonly IFileOperationService _fileOperationService;
    private readonly IExifEditorService? _exifEditorService;
    private readonly IDialogService? _dialogService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly WorkspaceState _workspaceState;
    private readonly ThumbnailGenerationCoordinator _thumbnailCoordinator;

    private string? _currentFolderPath;
    private bool _showImagesOnly = true;
    private bool _showDetailsModifiedColumn = true;
    private bool _showDetailsResolutionColumn = true;
    private bool _showDetailsSizeColumn = true;
    private bool _showDetailsTakenAtColumn;
    private bool _showDetailsLocationColumn;
    private string? _searchText;
    private FileViewMode _fileViewMode = FileViewMode.Details;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool _hasActiveFilters;
    private PhotoListItem? _selectedItem;
    private readonly List<PhotoListItem> _selectedItems = new();
    private bool _batchSelectionUpdate;
    private int _selectedCount;
    private CancellationTokenSource? _loadFolderCts;
    private Func<Task>? _openFolderAction;
    private Func<Task>? _createFolderAction;
    private Func<Task>? _renameSelectionAction;
    private Func<Task>? _moveSelectionAction;
    private Func<Task>? _moveSelectionToParentAction;
    private Func<Task>? _deleteSelectionAction;
    private readonly FileOperationCoordinator _operationCoordinator;
    private readonly IFolderWatcherService _folderWatcherService;

    public FileBrowserPaneViewModel()
        : this(new FileBrowserPaneService(), new WorkspaceState())
    {
    }

    internal FileBrowserPaneViewModel(
        IFileBrowserPaneService service,
        WorkspaceState workspaceState,
        IExifEditorService? exifEditorService = null,
        IDialogService? dialogService = null,
        IFileOperationService? fileOperationService = null,
        IFolderWatcherService? folderWatcherService = null,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(workspaceState);

        _service = service;
        _fileOperationService = fileOperationService ?? new FileOperationService();
        _workspaceState = workspaceState;
        _exifEditorService = exifEditorService;
        _dialogService = dialogService;
        _folderWatcherService = folderWatcherService ?? new FolderWatcherService();
        _folderWatcherService.FolderChanged += OnFolderWatcherChanged;
        _uiDispatcher = uiDispatcher ?? new UiDispatcher();
        Status = new FileBrowserStatusViewModel(_uiDispatcher);
        _thumbnailCoordinator = new ThumbnailGenerationCoordinator(_uiDispatcher, _fileOperationService.IsJpegFile);
        _operationCoordinator = new FileOperationCoordinator(
            _fileOperationService,
            _uiDispatcher,
            onMoveInProgressChanged: _ => OnMoveInProgressChanged(),
            onCopyInProgressChanged: _ => OnCopyInProgressChanged(),
            onStatusBarTextChanged: Status.SetStatusBarText,
            onClipboardChanged: OnClipboardChanged);

        // WorkspaceState にナビゲーションコールバックを設定
        _workspaceState.SelectNextAction = SelectNext;
        _workspaceState.SelectPreviousAction = SelectPrevious;
        _workspaceState.PhotoFocusRequested += OnWorkspacePhotoFocusRequested;

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
        ToggleImagesOnlyCommand = new RelayCommand(async () =>
        {
            // setter の UpdateFilterState がフォルダ再読み込みまで行うため、ここでの Refresh は不要
            // （重複させると LoadFolderAsync が並行実行され、先行側がキャンセルされる）
            ShowImagesOnly = !ShowImagesOnly;
            await Task.CompletedTask.ConfigureAwait(false);
        });
        EditExifCommand = new RelayCommand(async () => await EditExifAsync().ConfigureAwait(true), () => CanEditExif);
        SetViewModeCommand = new RelayCommand<string>(tag =>
        {
            if (tag is not null && Enum.TryParse(tag, out FileViewMode mode))
            {
                FileViewMode = mode;
            }

            return Task.CompletedTask;
        });
        OpenFolderCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_openFolderAction).ConfigureAwait(false),
            () => _openFolderAction is not null);
        CreateFolderCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_createFolderAction).ConfigureAwait(false),
            () => _createFolderAction is not null && CanCreateFolder);
        RenameSelectionCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_renameSelectionAction).ConfigureAwait(false),
            () => _renameSelectionAction is not null && CanRenameSelection);
        MoveSelectionCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_moveSelectionAction).ConfigureAwait(false),
            () => _moveSelectionAction is not null && CanModifySelection);
        MoveSelectionToParentCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_moveSelectionToParentAction).ConfigureAwait(false),
            () => _moveSelectionToParentAction is not null && CanMoveToParentSelection);
        DeleteSelectionCommand = new RelayCommand(
            async () => await ExecuteUiActionAsync(_deleteSelectionAction).ConfigureAwait(false),
            () => _deleteSelectionAction is not null && CanModifySelection);
        CancelMoveCommand = new RelayCommand(async () => await _operationCoordinator.CancelMoveAsync().ConfigureAwait(false), () => IsMoveInProgress);
        CancelCopyCommand = new RelayCommand(async () => await _operationCoordinator.CancelCopyAsync().ConfigureAwait(false), () => IsCopyInProgress);
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
                OnPropertyChanged(nameof(CanOpenInExplorer));
                OnPropertyChanged(nameof(CanPasteSelection));
                UpdateNavigationCommands();
                RaiseFileOperationCommandCanExecuteChanged();
                UpdateStatusBar();

                // WorkspaceState に反映
                _workspaceState.CurrentFolderPath = value;
            }
        }
    }

    /// <summary>ステータスオーバーレイ・ステータスバー・メタデータロードを担う子 ViewModel。</summary>
    public FileBrowserStatusViewModel Status { get; }

    public bool ShowImagesOnly
    {
        get => _showImagesOnly;
        set
        {
            if (SetProperty(ref _showImagesOnly, value))
            {
                UpdateFilterState();
                OnPropertyChanged(nameof(ToggleImagesOnlyMenuText));
            }
        }
    }

    public string ToggleImagesOnlyMenuText => ShowImagesOnly
        ? LocalizationService.GetString("MenuViewAllFiles.Text")
        : LocalizationService.GetString("MenuViewImagesOnly.Text");

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

    public bool ShowDetailsModifiedColumn
    {
        get => _showDetailsModifiedColumn;
        set
        {
            if (SetProperty(ref _showDetailsModifiedColumn, value))
            {
                OnPropertyChanged(nameof(DetailsModifiedColumnVisibility));
            }
        }
    }

    public bool ShowDetailsResolutionColumn
    {
        get => _showDetailsResolutionColumn;
        set
        {
            if (SetProperty(ref _showDetailsResolutionColumn, value))
            {
                OnPropertyChanged(nameof(DetailsResolutionColumnVisibility));
            }
        }
    }

    public bool ShowDetailsSizeColumn
    {
        get => _showDetailsSizeColumn;
        set
        {
            if (SetProperty(ref _showDetailsSizeColumn, value))
            {
                OnPropertyChanged(nameof(DetailsSizeColumnVisibility));
            }
        }
    }

    public bool ShowDetailsTakenAtColumn
    {
        get => _showDetailsTakenAtColumn;
        set
        {
            if (SetProperty(ref _showDetailsTakenAtColumn, value))
            {
                OnPropertyChanged(nameof(DetailsTakenAtColumnVisibility));
            }
        }
    }

    public bool ShowDetailsLocationColumn
    {
        get => _showDetailsLocationColumn;
        set
        {
            if (SetProperty(ref _showDetailsLocationColumn, value))
            {
                OnPropertyChanged(nameof(DetailsLocationColumnVisibility));
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
    public Visibility DetailsModifiedColumnVisibility => _showDetailsModifiedColumn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsResolutionColumnVisibility => _showDetailsResolutionColumn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsSizeColumnVisibility => _showDetailsSizeColumn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsTakenAtColumnVisibility => _showDetailsTakenAtColumn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsLocationColumnVisibility => _showDetailsLocationColumn ? Visibility.Visible : Visibility.Collapsed;

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
                OnPropertyChanged(nameof(CanEditExif));
                OnPropertyChanged(nameof(CanCopySelection));
                OnPropertyChanged(nameof(CanOpenInGoogleMaps));
                RaiseFileOperationCommandCanExecuteChanged();
                (EditExifCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(CanEditExif));
                OnPropertyChanged(nameof(CanOpenInGoogleMaps));
                (EditExifCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMoveInProgress => _operationCoordinator.IsMoveInProgress;

    public Visibility CancelMoveVisibility => IsMoveInProgress ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCopyInProgress => _operationCoordinator.IsCopyInProgress;

    public Visibility CancelCopyVisibility => IsCopyInProgress ? Visibility.Visible : Visibility.Collapsed;

    public bool CanPasteSelection => _operationCoordinator.HasClipboardItems && !string.IsNullOrWhiteSpace(CurrentFolderPath);
    public bool IsCutClipboard => _operationCoordinator.IsCutClipboard;

    public ICommand CancelMoveCommand { get; private set; }
    public ICommand CancelCopyCommand { get; private set; }

    public ICommand NavigateBackCommand { get; }
    public ICommand NavigateForwardCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand NavigateHomeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleSortCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand ToggleImagesOnlyCommand { get; }
    public ICommand EditExifCommand { get; }
    public ICommand SetViewModeCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand RenameSelectionCommand { get; }
    public ICommand MoveSelectionCommand { get; }
    public ICommand MoveSelectionToParentCommand { get; }
    public ICommand DeleteSelectionCommand { get; }

    public bool CanNavigateBack => _service.CanNavigateBack;
    public bool CanNavigateForward => _service.CanNavigateForward;
    public bool CanNavigateUp => !string.IsNullOrWhiteSpace(CurrentFolderPath) && _fileOperationService.GetParentPath(CurrentFolderPath) is not null;
    public bool CanCreateFolder => !string.IsNullOrWhiteSpace(CurrentFolderPath);
    public bool CanModifySelection => SelectedCount > 0;
    public bool CanRenameSelection => SelectedCount == 1;
    public bool CanMoveToParentSelection
        => SelectedCount > 0
           && !string.IsNullOrWhiteSpace(CurrentFolderPath)
           && _fileOperationService.GetParentPath(CurrentFolderPath) is not null;
    public bool CanEditExif => SelectedCount == 1 && IsJpegFile(SelectedItem);
    public bool CanCopySelection => SelectedCount > 0;
    public bool CanOpenInExplorer => !string.IsNullOrWhiteSpace(CurrentFolderPath);
    public bool CanOpenInGoogleMaps => SelectedCount == 1 && SelectedItem?.HasLocation == true;

    public string NameSortIndicator => GetSortIndicator(FileSortColumn.Name);
    public string TakenAtSortIndicator => GetSortIndicator(FileSortColumn.TakenAt);
    public string ModifiedSortIndicator => GetSortIndicator(FileSortColumn.ModifiedAt);
    public string ResolutionSortIndicator => GetSortIndicator(FileSortColumn.Resolution);
    public string SizeSortIndicator => GetSortIndicator(FileSortColumn.Size);
    public string LocationSortIndicator => GetSortIndicator(FileSortColumn.Location);

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

    internal void ConfigureUiActionHandlers(
        Func<Task>? openFolderAction,
        Func<Task>? createFolderAction,
        Func<Task>? renameSelectionAction,
        Func<Task>? moveSelectionAction,
        Func<Task>? moveSelectionToParentAction,
        Func<Task>? deleteSelectionAction)
    {
        _openFolderAction = openFolderAction;
        _createFolderAction = createFolderAction;
        _renameSelectionAction = renameSelectionAction;
        _moveSelectionAction = moveSelectionAction;
        _moveSelectionToParentAction = moveSelectionToParentAction;
        _deleteSelectionAction = deleteSelectionAction;
        RaiseFileOperationCommandCanExecuteChanged();
    }

    internal async Task<FileOperationResult> ExecuteCreateFolderAsync(string folderName)
    {
        var result = _operationCoordinator.CreateFolder(CurrentFolderPath, folderName);
        if (result.IsSuccess)
        {
            await RefreshAsync().ConfigureAwait(false);
            if (result.ResultPath is not null)
            {
                SelectItemByPath(result.ResultPath);
            }
        }

        return result;
    }

    internal async Task<FileOperationResult> ExecuteRenameAsync(PhotoListItem item, string newName)
    {
        var result = _operationCoordinator.Rename(item, newName);

        // 同名リネーム（何もしない成功）は ResultPath が null のため Refresh しない
        if (result.IsSuccess && result.ResultPath is not null)
        {
            await RefreshAsync().ConfigureAwait(false);
            SelectItemByPath(result.ResultPath);
        }

        return result;
    }

    internal async Task<FileOperationSummary> ExecuteMoveItemsToFolderAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync = null)
    {
        var summary = await _operationCoordinator.MoveItemsToFolderAsync(items, destinationFolder, resolveConflictAsync).ConfigureAwait(false);
        await FinishTransferAsync(summary, resolveConflictAsync is null ? null : "Message.MoveDone").ConfigureAwait(false);
        return summary;
    }

    internal async Task<FileOperationSummary> ExecuteCopyItemsToFolderAsync(
        IReadOnlyList<PhotoListItem> items,
        string destinationFolder,
        Func<string, bool, Task<ConflictResolution>>? resolveConflictAsync = null)
    {
        var summary = await _operationCoordinator.CopyItemsToFolderAsync(items, destinationFolder, resolveConflictAsync).ConfigureAwait(false);
        await FinishTransferAsync(summary, resolveConflictAsync is null ? null : "Message.CopyDone").ConfigureAwait(false);
        return summary;
    }

    /// <summary>
    /// 転送（Move/Copy）完了後の共通処理。1 件でも変化があれば再読み込みし、
    /// 進捗管理付きパス（競合解決コールバックあり）の場合は完了メッセージを表示する。
    /// </summary>
    private async Task FinishTransferAsync(FileOperationSummary summary, string? doneMessageResourceKey)
    {
        if (summary.SuccessCount > 0 || summary.SkipCount > 0)
        {
            await RefreshAsync().ConfigureAwait(false);
        }

        if (doneMessageResourceKey is not null)
        {
            var doneText = LocalizationService.Format(
                doneMessageResourceKey, summary.SuccessCount, summary.SkipCount, summary.FailureCount);
            await _uiDispatcher.RunAsync(() => Status.SetStatusBarText(doneText)).ConfigureAwait(false);
        }
    }

    internal void SetClipboard(IReadOnlyList<PhotoListItem> items, ClipboardOperation operation)
    {
        _operationCoordinator.SetClipboard(items, operation);
    }

    internal async Task<FileOperationSummary> ExecutePasteAsync(
        Func<string, bool, Task<ConflictResolution>>? resolveMoveConflictAsync = null,
        Func<string, bool, Task<ConflictResolution>>? resolveCopyConflictAsync = null)
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return FileOperationCoordinator.EmptySummary;
        }

        var (summary, operation) = await _operationCoordinator.PasteAsync(
            CurrentFolderPath, resolveMoveConflictAsync, resolveCopyConflictAsync).ConfigureAwait(false);

        if (operation == ClipboardOperation.None)
        {
            return summary;
        }

        var isCopy = operation == ClipboardOperation.Copy;
        var resolveConflictAsync = isCopy ? resolveCopyConflictAsync : resolveMoveConflictAsync;
        await FinishTransferAsync(
            summary,
            resolveConflictAsync is null ? null : (isCopy ? "Message.CopyDone" : "Message.MoveDone")).ConfigureAwait(false);

        return summary;
    }

    internal async Task<FileOperationSummary> ExecuteMoveToParentAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolderPath) || SelectedItems.Count == 0)
        {
            return FileOperationCoordinator.EmptySummary;
        }

        var parentPath = _fileOperationService.GetParentPath(CurrentFolderPath);
        if (parentPath is null)
        {
            return FileOperationCoordinator.EmptySummary;
        }

        return await ExecuteMoveItemsToFolderAsync(SelectedItems, parentPath).ConfigureAwait(false);
    }

    internal async Task<FileOperationSummary> ExecuteDeleteItemsAsync(
        IReadOnlyList<PhotoListItem> items)
    {
        var summary = await _operationCoordinator.DeleteItemsAsync(items).ConfigureAwait(false);
        if (summary.SuccessCount > 0)
        {
            await RefreshAsync().ConfigureAwait(false);
        }

        return summary;
    }

    internal async Task HandleExternalFileDropAsync(string filePath)
    {
        var directory = _fileOperationService.GetParentPath(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        await LoadFolderAsync(directory).ConfigureAwait(false);
        SelectItemByPath(filePath);
    }

    public async Task LoadFolderAsync(string folderPath, bool updateHistory = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.FolderPathEmpty"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
            return;
        }

        if (!_fileOperationService.FolderExistsAtPath(folderPath))
        {
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.FolderNotFound"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
            return;
        }

        // 既存の読み込み処理をキャンセル
        var previousCts = _loadFolderCts;
        var cts = new CancellationTokenSource();
        // 後続の呼び出しや CancelFolderLoad が CTS を Dispose した後に Token getter へ触れると
        // ObjectDisposedException になるため、await 前に CancellationToken を保持する
        var token = cts.Token;
        _loadFolderCts = cts;

        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(false);
            previousCts.Dispose();
        }

        try
        {
            var previousPath = CurrentFolderPath;
            await _uiDispatcher.RunAsync(() =>
            {
                CurrentFolderPath = folderPath;
                UpdateBreadcrumbs(folderPath);
                Status.SetStatus(null, InfoBarSeverity.Informational);
                SelectedItem = null;
                UpdateSelection(Array.Empty<PhotoListItem>());
            }).ConfigureAwait(false);

            var items = await _service.LoadFolderAsync(folderPath, ShowImagesOnly, SearchText).ConfigureAwait(false);

            // キャンセルされた場合は処理を中断
            token.ThrowIfCancellationRequested();

            var sorted = _service.ApplySort(items, SortColumn, SortDirection);

            await _uiDispatcher.RunAsync(() =>
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

                Status.SetStatus(
                    Items.Count == 0 ? LocalizationService.GetString("Message.NoFilesFound") : null,
                    InfoBarSeverity.Informational);
                UpdateStatusBar();

                // バックグラウンドでサムネイル生成を開始
                // 注意: StartGeneration 内部で UI スレッドタイマーを構成しているため、
                //       ここでは明示的に UI スレッド上から呼び出している（UI 更新と意図を揃えるため）
                _thumbnailCoordinator.StartGeneration(Items);
                _folderWatcherService.Watch(folderPath);
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
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.AccessDeniedSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException ex)
        {
            AppLog.Error($"Folder not found: {folderPath}", ex);
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.FolderNotFoundSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read folder: {folderPath}", ex);
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.FailedReadFolderSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.Error($"LoadFolderAsync: Unexpected exception for '{folderPath}'", ex);
            await _uiDispatcher.RunAsync(() =>
            {
                Items.Clear();
                Status.SetStatus(LocalizationService.GetString("Message.FailedReadFolderSeeLog"), InfoBarSeverity.Error);
            }).ConfigureAwait(false);
        }
    }

    public async Task OpenHomeAsync()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(homePath) || !_fileOperationService.FolderExistsAtPath(homePath))
        {
            await _uiDispatcher.RunAsync(() =>
            {
                Status.SetStatus(LocalizationService.GetString("Message.PicturesFolderNotFound"), InfoBarSeverity.Error);
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
            await _uiDispatcher.RunAsync(UpdateNavigationCommands).ConfigureAwait(false);
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
            await _uiDispatcher.RunAsync(UpdateNavigationCommands).ConfigureAwait(false);
        }
    }

    public async Task NavigateUpAsync()
    {
        if (!CanNavigateUp || string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            return;
        }

        var parentPath = _fileOperationService.GetParentPath(CurrentFolderPath);
        if (parentPath is not null)
        {
            await LoadFolderAsync(parentPath).ConfigureAwait(false);
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

    public async Task EditExifAsync()
    {
        if (_exifEditorService is null)
        {
            AppLog.Info("EditExifAsync skipped because IExifEditorService is not configured.");
            return;
        }

        var validation = await _exifEditorService
            .ValidateExifEditableAsync(SelectedItems, CancellationToken.None)
            .ConfigureAwait(true);

        if (!validation.IsValid)
        {
            if (_dialogService is not null && !string.IsNullOrWhiteSpace(validation.ErrorMessageKey))
            {
                await _dialogService.ShowMessageDialogAsync(
                    LocalizationService.GetString("ExifEditor.Title"),
                    LocalizationService.GetString(validation.ErrorMessageKey),
                    CancellationToken.None).ConfigureAwait(true);
            }

            return;
        }

        if (validation.TargetItem is null)
        {
            return;
        }

        var success = await _exifEditorService.EditExifAsync(validation.TargetItem, CancellationToken.None).ConfigureAwait(true);
        if (success)
        {
            await RefreshAsync().ConfigureAwait(true);
        }
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

        var match = _service.FindItemByFilePath(Items, filePath);
        if (match is not null)
        {
            SelectedItem = match;
        }
    }

    internal IReadOnlyList<PhotoListItem> ResolveItemsByFilePaths(IReadOnlyList<string> filePaths)
    {
        var resolvedItems = _service.ResolveItemsByFilePaths(Items, filePaths);
        return resolvedItems
            .Where(item => !item.IsFolder)
            .ToList();
    }

    public void ResetFilters()
    {
        // setter 経由で 2 つのフィルタを更新すると UpdateFilterState が二重発火し、
        // LoadFolderAsync が並行実行される（#164 と同じパターン）ため、
        // フィールドを直接更新して再読み込みを 1 回に合流させる
        var searchTextChanged = SetProperty(ref _searchText, null, nameof(SearchText));
        var showImagesOnlyChanged = SetProperty(ref _showImagesOnly, true, nameof(ShowImagesOnly));
        if (showImagesOnlyChanged)
        {
            OnPropertyChanged(nameof(ToggleImagesOnlyMenuText));
        }

        if (searchTextChanged || showImagesOnlyChanged)
        {
            UpdateFilterState();
        }
    }

    // View が listView.SelectedItems を一括操作する間、TwoWay バインディング経由の
    // SelectedItem 変化による UpdateSelection の副作用を抑制するために使う。
    internal void BeginBatchSelectionUpdate() => _batchSelectionUpdate = true;
    internal void EndBatchSelectionUpdate() => _batchSelectionUpdate = false;

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
        _workspaceState.PhotoFocusRequested -= OnWorkspacePhotoFocusRequested;
        ConfigureUiActionHandlers(null, null, null, null, null, null);
        _folderWatcherService.FolderChanged -= OnFolderWatcherChanged;
        _folderWatcherService.Dispose();
        _thumbnailCoordinator.Dispose();
        Status.Dispose();
        CancelFolderLoad();
        _operationCoordinator.Dispose();
    }

    private void OnMoveInProgressChanged()
    {
        OnPropertyChanged(nameof(IsMoveInProgress));
        OnPropertyChanged(nameof(CancelMoveVisibility));
        (CancelMoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnCopyInProgressChanged()
    {
        OnPropertyChanged(nameof(IsCopyInProgress));
        OnPropertyChanged(nameof(CancelCopyVisibility));
        (CancelCopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnClipboardChanged()
    {
        OnPropertyChanged(nameof(CanPasteSelection));
        OnPropertyChanged(nameof(IsCutClipboard));
    }

    private void OnWorkspacePhotoFocusRequested(object? sender, WorkspacePhotoFocusRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var target = _service.FindItemByFilePath(Items, e.FilePath);
        if (target is null)
        {
            return;
        }

        SelectedItem = target;
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
        OnPropertyChanged(nameof(TakenAtSortIndicator));
        OnPropertyChanged(nameof(ModifiedSortIndicator));
        OnPropertyChanged(nameof(ResolutionSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
        OnPropertyChanged(nameof(LocationSortIndicator));
    }

    private void UpdateFilterState()
    {
        HasActiveFilters = !string.IsNullOrWhiteSpace(SearchText) || !ShowImagesOnly;
        Status.NotifyFiltersChanged(HasActiveFilters);

        // フィルタ変更時は現在のフォルダを再読み込み（履歴には追加しない）
        if (!string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            _ = LoadFolderAsync(CurrentFolderPath, updateHistory: false);
        }
    }

    private void UpdateNavigationCommands()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        (NavigateBackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NavigateForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NavigateUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static Task ExecuteUiActionAsync(Func<Task>? action)
    {
        return action is null ? Task.CompletedTask : action();
    }

    private void RaiseFileOperationCommandCanExecuteChanged()
    {
        (OpenFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CreateFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RenameSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveSelectionToParentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnSelectedItemChanged()
    {
        if (_batchSelectionUpdate)
        {
            return;
        }

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
        _ = Status.LoadMetadataAsync(SelectedItem);
    }

    private void UpdateStatusBar()
        => Status.UpdateStatusBar(CurrentFolderPath, Items.Count, SelectedCount, SelectedItem);

    private bool IsJpegFile(PhotoListItem? item)
        => item is { IsFolder: false } && _fileOperationService.IsJpegFile(item.FilePath);

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

    private void OnFolderWatcherChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.TryEnqueue(async () =>
        {
            if (IsMoveInProgress || IsCopyInProgress)
            {
                return;
            }

            await RefreshAsync().ConfigureAwait(false);
        });
    }

}
