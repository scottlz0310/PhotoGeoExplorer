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
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// Preview Pane の ViewModel
/// 画像プレビューの状態管理、ズーム/パン制御、ナビゲーションを実装
/// </summary>
internal sealed class PreviewPaneViewModel : PaneViewModelBase
{
    private const float MinZoomFactor = 0.1f;
    private const float MaxZoomFactor = 6.0f;
    private const float ZoomInMultiplier = 1.2f;
    private const float ZoomOutMultiplier = 1f / 1.2f;
    private const int ClassNotRegisteredHresult = unchecked((int)0x80040154);

    private readonly IPreviewPaneService _service;
    private readonly WorkspaceState _workspaceState;
    private BitmapImage? _currentImage;
    private Visibility _placeholderVisibility = Visibility.Visible;
    private string? _metadataSummary;
    private Visibility _metadataVisibility = Visibility.Collapsed;
    private bool _fitToWindow = true;
    private float _zoomFactor = 1.0f;
    private double _rasterizationScale = 1.0;
    private string? _currentFilePath;

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
        NextCommand = new RelayCommand(ExecuteNextAsync, CanExecuteNext);
        PreviousCommand = new RelayCommand(ExecutePreviousAsync, CanExecutePrevious);

        // WorkspaceState の変更を監視
        _workspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
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
    /// ウィンドウにフィットさせるモードかどうか
    /// </summary>
    public bool FitToWindow
    {
        get => _fitToWindow;
        set => SetProperty(ref _fitToWindow, value);
    }

    /// <summary>
    /// 現在のズームファクター
    /// </summary>
    public float ZoomFactor
    {
        get => _zoomFactor;
        set => SetProperty(ref _zoomFactor, value);
    }

    /// <summary>
    /// 現在の DPI スケール
    /// </summary>
    public double RasterizationScale
    {
        get => _rasterizationScale;
        set => SetProperty(ref _rasterizationScale, value);
    }

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
    public ICommand NextCommand { get; }

    /// <summary>
    /// 前の画像コマンド
    /// </summary>
    public ICommand PreviousCommand { get; }

    /// <summary>
    /// ScrollViewer のビューポートサイズが変更されたときに呼ばれる
    /// </summary>
    public void OnViewportSizeChanged(double viewportWidth, double viewportHeight)
    {
        if (FitToWindow && CurrentImage is not null)
        {
            var newZoom = _service.CalculateFitZoomFactor(
                CurrentImage.PixelWidth,
                CurrentImage.PixelHeight,
                viewportWidth,
                viewportHeight,
                MinZoomFactor,
                MaxZoomFactor);
            ZoomFactor = newZoom;
        }
    }

    /// <summary>
    /// 画像が読み込まれたときに呼ばれる
    /// </summary>
    public void OnImageOpened(double viewportWidth, double viewportHeight)
    {
        FitToWindow = true;
        OnViewportSizeChanged(viewportWidth, viewportHeight);
    }

    /// <summary>
    /// ズーム操作（乗算）
    /// </summary>
    public void AdjustZoom(float multiplier)
    {
        FitToWindow = false;
        var newZoom = ZoomFactor * multiplier;
        ZoomFactor = Math.Clamp(newZoom, MinZoomFactor, MaxZoomFactor);
    }

    /// <summary>
    /// ホイールズーム操作（カーソル位置を基準にズーム）
    /// </summary>
    public void ZoomAtPoint(int wheelDelta, double viewportWidth, double viewportHeight)
    {
        FitToWindow = false;
        var multiplier = wheelDelta > 0 ? 1.1f : 1f / 1.1f;
        var newZoom = ZoomFactor * multiplier;
        ZoomFactor = Math.Clamp(newZoom, MinZoomFactor, MaxZoomFactor);
    }

    /// <summary>
    /// DPI スケーリングが変更されたときに呼ばれる
    /// </summary>
    public void OnRasterizationScaleChanged(double newScale)
    {
        if (Math.Abs(newScale - RasterizationScale) < 0.0001)
        {
            return;
        }

        AppLog.Info($"RasterizationScale changed: {RasterizationScale} -> {newScale}");

        if (FitToWindow && CurrentImage is not null)
        {
            // FitToWindow モードの場合は、後で再計算されるのでスケールだけ更新
            RasterizationScale = newScale;
            return;
        }

        // ユーザーが手動でズームしている場合は、ZoomFactor を補正して視覚的サイズを維持
        if (RasterizationScale > 0)
        {
            var correctedZoom = _service.CalculateDpiCorrectedZoom(
                ZoomFactor,
                RasterizationScale,
                newScale,
                MinZoomFactor,
                MaxZoomFactor);
            ZoomFactor = correctedZoom;
            AppLog.Info($"Preview zoom corrected: {ZoomFactor} -> {correctedZoom}");
        }

        RasterizationScale = newScale;
    }

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
    }

    private async Task LoadSelectedPhotoAsync()
    {
        var selectedPhotos = _workspaceState.SelectedPhotos;
        if (selectedPhotos is null || selectedPhotos.Count == 0)
        {
            // UI スレッドでクリア
            var clearQueue = TryGetDispatcherQueue();
            if (clearQueue is null)
            {
                CurrentImage = null;
                MetadataSummary = null;
                _currentFilePath = null;
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                clearQueue.TryEnqueue(() =>
                {
                    try
                    {
                        CurrentImage = null;
                        MetadataSummary = null;
                        _currentFilePath = null;
                        tcs.TrySetResult(true);
                    }
#pragma warning disable CA1031 // UIコールバック内ではTCSを確実に完了させるため、例外を捕捉する。
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
#pragma warning restore CA1031 // UIコールバック内ではTCSを確実に完了させるため、例外を捕捉する。
                });
                await tcs.Task.ConfigureAwait(false);
            }
            return;
        }

        var selectedPhoto = selectedPhotos[0];
        if (selectedPhoto.IsFolder)
        {
            return;
        }

        var filePath = selectedPhoto.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || filePath == _currentFilePath)
        {
            return;
        }

        _currentFilePath = filePath;

        var image = await _service.LoadImageAsync(filePath).ConfigureAwait(false);
        var metadata = await ExifService.GetMetadataAsync(filePath, CancellationToken.None).ConfigureAwait(false);

        // UI スレッドで更新
        var dispatcherQueue = TryGetDispatcherQueue();
        if (dispatcherQueue is null)
        {
            CurrentImage = image;
            MetadataSummary = BuildMetadataSummary(metadata, selectedPhoto);
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    CurrentImage = image;
                    MetadataSummary = BuildMetadataSummary(metadata, selectedPhoto);
                    tcs.TrySetResult(true);
                }
#pragma warning disable CA1031 // UIコールバック内ではTCSを確実に完了させるため、例外を捕捉する。
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
#pragma warning restore CA1031 // UIコールバック内ではTCSを確実に完了させるため、例外を捕捉する。
            });
            await tcs.Task.ConfigureAwait(false);
        }
    }

    private static DispatcherQueue? TryGetDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
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

        if (selectedPhoto?.PixelWidth is int width && selectedPhoto.PixelHeight is int height)
        {
            if (width > 0 && height > 0)
            {
                parts.Add($"{width}x{height}");
            }
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : null;
    }

    private Task ExecuteFitAsync()
    {
        FitToWindow = true;
        // ビューポートサイズは View 側から OnViewportSizeChanged で通知される
        return Task.CompletedTask;
    }

    private Task ExecuteZoomInAsync()
    {
        AdjustZoom(ZoomInMultiplier);
        return Task.CompletedTask;
    }

    private Task ExecuteZoomOutAsync()
    {
        AdjustZoom(ZoomOutMultiplier);
        return Task.CompletedTask;
    }

    private Task ExecuteNextAsync()
    {
        // MainViewModel の SelectNext を呼ぶ必要がある
        // TODO: WorkspaceState に CurrentPhotoIndex を追加して連携
        return Task.CompletedTask;
    }

    private Task ExecutePreviousAsync()
    {
        // MainViewModel の SelectPrevious を呼ぶ必要がある
        // TODO: WorkspaceState に CurrentPhotoIndex を追加して連携
        return Task.CompletedTask;
    }

    private bool CanExecuteNext()
    {
        // TODO: WorkspaceState から判断
        return false;
    }

    private bool CanExecutePrevious()
    {
        // TODO: WorkspaceState から判断
        return false;
    }
}
