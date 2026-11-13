using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Places;

/// <summary>
/// 現在のフォルダーの写真を、撮影地の字地名（市区町村 &gt; 町丁・字）で束ねて見せる。
/// 物理的なフォルダーは変えない。ノードを選ぶと、該当する写真をファイル一覧で選択する
/// （地図とプレビューは、既存の「選択への追従」で更新される）。
/// 全写真の位置情報を読むため、ペインが表示されている間（<see cref="PaneViewModelBase.IsActive"/>）だけ判定する
/// </summary>
internal sealed class PlacesPaneViewModel : PaneViewModelBase
{
    private readonly WorkspaceState _workspaceState;
    private readonly PhotoAreaClassifier _classifier;
    private readonly IAreaNameResolver _areaNameResolver;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);
    private readonly object _entriesLock = new();
    private CancellationTokenSource? _runCancellation;
    private int _runGeneration;
    private List<PhotoAreaEntry> _entries = [];
    private int _completed;
    private int _total;
    private bool _isDirty = true;
    private bool _refreshQueued;
    private bool _isBusy;
    private string _statusText = string.Empty;

    public PlacesPaneViewModel(
        WorkspaceState workspaceState,
        PhotoAreaClassifier classifier,
        IAreaNameResolver areaNameResolver,
        IUiDispatcher uiDispatcher)
    {
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _areaNameResolver = areaNameResolver ?? throw new ArgumentNullException(nameof(areaNameResolver));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        Title = "Places";
        CancelCommand = new RelayCommand(() =>
        {
            Cancel();
            return Task.CompletedTask;
        });
        _workspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
        _areaNameResolver.ResultsInvalidated += OnAreaDataChanged;
    }

    public ObservableCollection<PlacesNodeViewModel> Roots { get; } = [];

    public RelayCommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ProgressVisibility));
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Visibility ProgressVisibility => _isBusy ? Visibility.Visible : Visibility.Collapsed;

    public int ProgressMaximum => Math.Max(_total, 1);

    public int ProgressValue => _completed;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public Visibility StatusVisibility => _statusText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// ノードの写真をファイル一覧で選択する
    /// </summary>
    public void SelectNode(PlacesNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _workspaceState.RequestPhotoSelection(node.FilePaths);
    }

    /// <summary>
    /// 判定を中止する。そこまでに判定した分のツリーは残す
    /// </summary>
    public void Cancel()
    {
        if (!IsBusy)
        {
            return;
        }

        CancelRun();
        SetStatus("Places.Status.Canceled", _completed, _total);
        IsBusy = false;

        // 中止した結果は、写真やデータが変わるまで求め直さない
        _isDirty = false;
    }

    protected override void OnActiveChanged()
    {
        if (IsActive)
        {
            if (_isDirty)
            {
                _ = StartAsync();
            }

            return;
        }

        // 表示されていない間は、位置情報の読み取りを続けない（再び表示されたら続きから判定する）
        if (IsBusy)
        {
            CancelRun();
            IsBusy = false;
            _isDirty = true;
        }
    }

    protected override void OnCleanup()
    {
        _workspaceState.PropertyChanged -= OnWorkspaceStatePropertyChanged;
        _areaNameResolver.ResultsInvalidated -= OnAreaDataChanged;
        CancelRun();
    }

    private void OnWorkspaceStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceState.PhotoItems))
        {
            RequestRefresh();
        }
    }

    private void OnAreaDataChanged(object? sender, EventArgs e) => RequestRefresh();

    private void RequestRefresh()
    {
        _isDirty = true;
        if (IsActive)
        {
            _ = StartAsync();
        }
    }

    private async Task StartAsync()
    {
        CancelRun();
        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        var generation = ++_runGeneration;
        _isDirty = false;

        var photos = _workspaceState.PhotoItems
            .Select(item => new PhotoRef(item.FilePath, GetVersion(item), item.HasLocation))
            .ToList();
        lock (_entriesLock)
        {
            _entries = new List<PhotoAreaEntry>(photos.Count);
        }

        _completed = 0;
        _total = photos.Count;
        NotifyProgressChanged();
        if (photos.Count == 0)
        {
            ApplyTree([]);
            SetStatus("Places.Status.NoPhotos");
            IsBusy = false;
            return;
        }

        IsBusy = true;
        SetStatus("Places.Status.Progress", 0, photos.Count);
        try
        {
            await _classifier.ClassifyAsync(photos, new EntryCollector(this, generation), cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException ex)
        {
            if (IsCurrent(generation))
            {
                AppLog.Error("Failed to classify the photos by area name.", ex);
                ApplyTree([]);
                SetStatus("Places.Status.Error");
                IsBusy = false;
            }

            return;
        }

        if (!IsCurrent(generation))
        {
            return;
        }

        _completed = _total;
        NotifyProgressChanged();
        var nodes = ApplyTree(SnapshotEntries());

        // どの写真も字地名を判定できなかったときは、地域データの取り込みを案内する
        var noneResolved = nodes.Any(node => node.Kind == AreaTreeNodeKind.NoAreaData) && nodes.All(node => node.Kind != AreaTreeNodeKind.City);
        SetStatus(noneResolved ? "Places.Status.DoneNoAreaData" : "Places.Status.Done", _total);
        IsBusy = false;
    }

    private bool IsCurrent(int generation) => generation == _runGeneration;

    private void CancelRun()
    {
        // 実行中の判定の結果を、以後は受け付けない
        _runGeneration++;
        var previous = _runCancellation;
        _runCancellation = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private List<PhotoAreaEntry> SnapshotEntries()
    {
        lock (_entriesLock)
        {
            return [.. _entries];
        }
    }

    // 判定は背景のスレッドで進むので、結果は集めておき、画面の更新は UI スレッドで一度にまとめて行う
    private void OnEntriesReported(int generation, AreaClassificationProgress progress)
    {
        bool shouldQueue;
        lock (_entriesLock)
        {
            if (!IsCurrent(generation))
            {
                return;
            }

            _entries.AddRange(progress.NewEntries);
            _completed = progress.Completed;
            shouldQueue = !_refreshQueued;
            _refreshQueued = true;
        }

        if (shouldQueue && !_uiDispatcher.TryEnqueue(() => RefreshWhileRunning(generation)))
        {
            lock (_entriesLock)
            {
                _refreshQueued = false;
            }
        }
    }

    private void RefreshWhileRunning(int generation)
    {
        lock (_entriesLock)
        {
            _refreshQueued = false;
        }

        if (!IsCurrent(generation) || !IsBusy)
        {
            return;
        }

        NotifyProgressChanged();
        ApplyTree(SnapshotEntries());
        SetStatus("Places.Status.Progress", _completed, _total);
    }

    private IReadOnlyList<AreaTreeNode> ApplyTree(IReadOnlyList<PhotoAreaEntry> entries)
    {
        var nodes = AreaTreeBuilder.Build(entries);
        Roots.Clear();
        foreach (var node in nodes)
        {
            Roots.Add(new PlacesNodeViewModel(node, key => _expandedKeys.Contains(key), OnExpansionChanged));
        }

        return nodes;
    }

    private void OnExpansionChanged(string key, bool isExpanded)
    {
        if (isExpanded)
        {
            _expandedKeys.Add(key);
        }
        else
        {
            _expandedKeys.Remove(key);
        }
    }

    private void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressMaximum));
    }

    private void SetStatus(string key, params object[] args)
        => StatusText = args.Length == 0 ? LocalizationService.GetString(key) : LocalizationService.Format(key, args);

    // 更新日時とサイズが変わったら、位置情報を読み直す
    private static string GetVersion(PhotoListItem item)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{item.Item.ModifiedAt.UtcTicks}:{item.Item.SizeBytes}");

    private sealed class EntryCollector(PlacesPaneViewModel owner, int generation) : IProgress<AreaClassificationProgress>
    {
        public void Report(AreaClassificationProgress value) => owner.OnEntriesReported(generation, value);
    }
}
