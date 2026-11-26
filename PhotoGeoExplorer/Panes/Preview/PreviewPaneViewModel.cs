using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.PanZoom;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// Preview Pane の ViewModel
/// 画像プレビューの状態管理、ズーム/パンの変換（<see cref="PanZoom"/>）の保持、ナビゲーションを実装
/// </summary>
internal sealed class PreviewPaneViewModel : PaneViewModelBase
{
    // 倍率 1 が「ウィンドウに合わせた」状態。それより縮小はしない
    private const double MinScale = 1.0;
    private const double MaxScale = 6.0;
    private const double ZoomButtonFactor = 1.2;

    /// <summary>
    /// ホイール 1 ノッチあたりのズーム係数
    /// </summary>
    internal const double WheelFactorPerNotch = 1.1;

    private const int ClassNotRegisteredHresult = unchecked((int)0x80040154);

    private readonly IPreviewPaneService _service;
    private readonly WorkspaceState _workspaceState;
    private readonly RelayCommand _nextCommand;
    private readonly RelayCommand _previousCommand;
    private Action<Action>? _postToUi;
    private BitmapImage? _currentImage;
    private Visibility _placeholderVisibility = Visibility.Visible;
    private string? _metadataSummary;
    private Visibility _metadataVisibility = Visibility.Collapsed;
    private PanZoomSize _viewportSize;
    private PanZoomSize _imageSize;
    private string? _currentFilePath;
    private int _selectionVersion;

    public PreviewPaneViewModel()
        : this(new PreviewPaneService(), new WorkspaceState())
    {
    }

    internal PreviewPaneViewModel(IPreviewPaneService service, WorkspaceState workspaceState)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        Title = "Preview";

        FitCommand = new RelayCommand(ExecuteFitAsync);
        ZoomInCommand = new RelayCommand(ExecuteZoomInAsync);
        ZoomOutCommand = new RelayCommand(ExecuteZoomOutAsync);
        _nextCommand = new RelayCommand(ExecuteNextAsync, CanExecuteNext);
        _previousCommand = new RelayCommand(ExecutePreviousAsync, CanExecutePrevious);

        // WorkspaceState の変更を監視
        _workspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
    }

    internal void InitializeDispatcherQueue(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        InitializeUiPost(action => dispatcherQueue.TryEnqueue(() => action()));
    }

    /// <summary>
    /// UI スレッドで実行する処理の投入先を設定する。テストは、実行の順序を制御できる投入先を渡す
    /// </summary>
    internal void InitializeUiPost(Action<Action> postToUi)
    {
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
    }

    /// <summary>
    /// 現在表示中の画像
    /// </summary>
    public BitmapImage? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (SetProperty(ref _currentImage, value))
            {
                PlaceholderVisibility = value is null ? Visibility.Visible : Visibility.Collapsed;
                OnPropertyChanged(nameof(HasImage));
                OnImageReplaced(value?.PixelWidth ?? 0, value?.PixelHeight ?? 0);
            }
        }
    }

    /// <summary>
    /// プレースホルダーの表示/非表示
    /// </summary>
    public Visibility PlaceholderVisibility
    {
        get => _placeholderVisibility;
        private set => SetProperty(ref _placeholderVisibility, value);
    }

    /// <summary>
    /// メタデータサマリー
    /// </summary>
    public string? MetadataSummary
    {
        get => _metadataSummary;
        private set
        {
            if (SetProperty(ref _metadataSummary, value))
            {
                MetadataVisibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// メタデータの表示/非表示
    /// </summary>
    public Visibility MetadataVisibility
    {
        get => _metadataVisibility;
        private set => SetProperty(ref _metadataVisibility, value);
    }

    /// <summary>
    /// 画像が読み込まれているかどうか
    /// </summary>
    public bool HasImage => CurrentImage is not null;

    /// <summary>
    /// ズーム・パンの変換。入力（ホイール・ジェスチャー・ボタン）は View のアダプタがここへ渡し、View は変更通知を受けて描画に反映する
    /// </summary>
    internal PanZoomController PanZoom { get; } = new(MinScale, MaxScale);

    /// <summary>
    /// フィットコマンド
    /// </summary>
    public ICommand FitCommand { get; }

    /// <summary>
    /// ズームインコマンド
    /// </summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>
    /// ズームアウトコマンド
    /// </summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>
    /// 次の画像コマンド
    /// </summary>
    public ICommand NextCommand => _nextCommand;

    /// <summary>
    /// 前の画像コマンド
    /// </summary>
    public ICommand PreviousCommand => _previousCommand;

    /// <summary>
    /// 表示領域（DIP）の大きさが変わったときに呼ばれる。拡大状態と見ている位置は保たれる
    /// </summary>
    public void OnViewportSizeChanged(double viewportWidth, double viewportHeight)
    {
        _viewportSize = new PanZoomSize(viewportWidth, viewportHeight);
        PanZoom.SetLayout(_viewportSize, _imageSize);
    }

    /// <summary>
    /// 表示する画像が別のもの（または画像なし）に替わったときに呼ばれる。前の画像の拡大状態は引き継がない。
    /// 寸法が分かっていなければ（読み込み前）空のままにして、<see cref="OnImageOpened"/> を待つ
    /// </summary>
    /// <remarks>
    /// 読み込み済みの BitmapImage を Image.Source に渡すと ImageOpened は発火しないため、替わった時点で寸法が分かっていればここで使う
    /// </remarks>
    internal void OnImageReplaced(double pixelWidth, double pixelHeight)
    {
        _imageSize = default;
        PanZoom.SetLayout(_viewportSize, _imageSize);
        PanZoom.Fit();

        if (pixelWidth > 0 && pixelHeight > 0)
        {
            OnImageOpened(pixelWidth, pixelHeight);
        }
    }

    /// <summary>
    /// 画像が読み込まれて、ピクセル寸法が分かったときに呼ばれる
    /// </summary>
    public void OnImageOpened(double pixelWidth, double pixelHeight)
    {
        _imageSize = new PanZoomSize(pixelWidth, pixelHeight);
        PanZoom.SetLayout(_viewportSize, _imageSize);
        PanZoom.Fit();
    }

    /// <summary>
    /// ウィンドウに合わせた状態へ戻す
    /// </summary>
    public void Fit() => PanZoom.Fit();

    protected override void OnCleanup()
    {
        _workspaceState.PropertyChanged -= OnWorkspaceStatePropertyChanged;
        CurrentImage = null;
        _currentFilePath = null;
    }

    protected override void OnActiveChanged()
    {
        // Paneがアクティブになったときの処理
    }

    private async void OnWorkspaceStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceState.SelectedPhotos))
        {
            await LoadSelectedPhotoAsync().ConfigureAwait(false);
        }

        // ナビゲーションボタンの状態を更新
        if (e.PropertyName == nameof(WorkspaceState.CurrentPhotoIndex) ||
            e.PropertyName == nameof(WorkspaceState.PhotoListCount))
        {
            UpdateNavigationCommands();
        }
    }

    private void UpdateNavigationCommands()
    {
        _nextCommand.RaiseCanExecuteChanged();
        _previousCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadSelectedPhotoAsync()
    {
        var selectedPhotos = _workspaceState.SelectedPhotos;
        if (selectedPhotos is null || selectedPhotos.Count == 0)
        {
            // 反映が後回しになる間に次の選択が届くことがある（一覧の選択の入れ直しは「空 → 選択」の順に届く）
            var clearVersion = ++_selectionVersion;
            await RunOnUiAsync(() =>
            {
                if (clearVersion != _selectionVersion)
                {
                    return;
                }

                CurrentImage = null;
                MetadataSummary = null;
                _currentFilePath = null;
            }).ConfigureAwait(false);
            return;
        }

        var selectedPhoto = selectedPhotos[0];
        if (selectedPhoto.IsFolder)
        {
            return;
        }

        var filePath = selectedPhoto.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        // 反映を待っている消去は、この選択より古いので取り消す
        _selectionVersion++;
        if (filePath == _currentFilePath)
        {
            return;
        }

        _currentFilePath = filePath;

        var image = await _service.LoadImageAsync(filePath).ConfigureAwait(false);
        var metadata = await _service.GetMetadataAsync(filePath, CancellationToken.None).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            // 読み込みの間に別の写真が選ばれたか、選択が解除されていれば反映しない
            if (filePath != _currentFilePath)
            {
                return;
            }

            CurrentImage = image;
            MetadataSummary = BuildMetadataSummary(metadata, selectedPhoto);
        }).ConfigureAwait(false);
    }

    // UI スレッドで実行して完了を待つ。UI のキューがない（単体テストなど）場合は、その場で実行する
    private Task RunOnUiAsync(Action action)
    {
        var postToUi = _postToUi ?? TryGetCurrentThreadPost();
        if (postToUi is null)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        postToUi(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
#pragma warning disable CA1031 // This callback must complete the TaskCompletionSource on any failure.
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
#pragma warning restore CA1031 // This callback must complete the TaskCompletionSource on any failure.
        });
        return completion.Task;
    }

    private static Action<Action>? TryGetCurrentThreadPost()
    {
        try
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            return dispatcherQueue is null ? null : action => dispatcherQueue.TryEnqueue(() => action());
        }
        catch (COMException ex) when (ex.HResult == ClassNotRegisteredHresult)
        {
            return null;
        }
    }

    private static string? BuildMetadataSummary(Models.PhotoMetadata? metadata, PhotoListItem? selectedPhoto)
    {
        if (metadata is null && selectedPhoto is null)
        {
            return null;
        }

        var parts = new System.Collections.Generic.List<string>();

        if (metadata?.TakenAt.HasValue == true)
        {
            parts.Add(metadata.TakenAt.Value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture));
        }

        if (!string.IsNullOrWhiteSpace(metadata?.CameraMake) || !string.IsNullOrWhiteSpace(metadata?.CameraModel))
        {
            var camera = string.IsNullOrWhiteSpace(metadata.CameraMake)
                ? metadata.CameraModel
                : string.IsNullOrWhiteSpace(metadata.CameraModel)
                    ? metadata.CameraMake
                    : $"{metadata.CameraMake} {metadata.CameraModel}";
            parts.Add(camera ?? string.Empty);
        }

        if (selectedPhoto?.PixelWidth is int width &&
            selectedPhoto.PixelHeight is int height &&
            width > 0 &&
            height > 0)
        {
            parts.Add($"{width}x{height}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    private Task ExecuteFitAsync()
    {
        Fit();
        return Task.CompletedTask;
    }

    private Task ExecuteZoomInAsync()
    {
        PanZoom.ZoomAtCenter(ZoomButtonFactor);
        return Task.CompletedTask;
    }

    private Task ExecuteZoomOutAsync()
    {
        PanZoom.ZoomAtCenter(1.0 / ZoomButtonFactor);
        return Task.CompletedTask;
    }

    private Task ExecuteNextAsync()
    {
        _workspaceState.SelectNext();
        return Task.CompletedTask;
    }

    private Task ExecutePreviousAsync()
    {
        _workspaceState.SelectPrevious();
        return Task.CompletedTask;
    }

    private bool CanExecuteNext()
    {
        return _workspaceState.CanSelectNext;
    }

    private bool CanExecutePrevious()
    {
        return _workspaceState.CanSelectPrevious;
    }
}
