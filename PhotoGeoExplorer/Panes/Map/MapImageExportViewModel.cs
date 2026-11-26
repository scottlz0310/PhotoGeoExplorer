using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 保存画像の背景の候補
/// </summary>
/// <param name="Label">選択肢の表示名</param>
/// <param name="Request">この背景の生成要求</param>
internal sealed record MapImageBackgroundChoice(string Label, MapBackgroundRequest Request);

/// <summary>
/// 保存ダイアログを開いた時点の地図
/// </summary>
/// <param name="Backgrounds">選べる背景。表示中の背景を含む</param>
/// <param name="DisplayedSource">表示中の背景</param>
/// <param name="View">表示中の視点</param>
/// <param name="PixelDensity">表示倍率（物理ピクセル／論理ピクセル）</param>
/// <param name="Markers">表示中の写真ピン</param>
internal sealed record MapImageExportSource(
    IReadOnlyList<MapImageBackgroundChoice> Backgrounds,
    MapTileSourceType DisplayedSource,
    MapViewState View,
    double PixelDensity,
    IReadOnlyList<MapMarker> Markers);

/// <summary>
/// 保存ダイアログを出し、保存へ進むときに <see langword="true"/> を返す
/// </summary>
internal delegate Task<bool> MapImageExportDialogAsync(
    MapImageExportViewModel viewModel,
    CancellationToken cancellationToken);

/// <summary>
/// 地図画像の保存ダイアログの状態。条件を変えると、少し待ってからプレビューを描き直す。
/// UI スレッドから使う。
/// </summary>
/// <remarks>
/// プレビューは保存する画像そのもの（表示だけ縮小する）。確認した画像と違うものを保存しないよう、
/// いまの条件のプレビューが画面に出るまで保存できず、保存にはその画像を使う。
/// </remarks>
internal sealed class MapImageExportViewModel : BindableBase, IAsyncDisposable
{
    /// <summary>条件の変更からプレビューを描き始めるまでの待ち時間。続けて変えている間は描かない</summary>
    internal static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(500);

    // 画面の入力欄は小数 2 桁に丸めて書き戻すため、丸めの範囲の違いは変更とみなさない
    private const double ZoomRoundingTolerance = 0.005;

    private const int CurrentViewSizeIndex = 0;
    private const int CustomSizeIndex = 3;
    private static readonly (int Width, int Height)[] PresetSizes = [(1280, 720), (1600, 1200)];
    private static readonly double[] StandardTextScales = [1.0, 1.5, 2.0];

    private readonly IMapImageExporter _exporter;
    private readonly MapImageExportSource _source;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly double[] _textScales;
    private CancellationTokenSource? _previewCts;
    private Task _previewTask = Task.CompletedTask;
    // いまの条件で描いた画像と、そのうち画面に出たことを確かめた画像。条件を変えたら、どちらも無効にする
    private byte[]? _renderedPng;
    private byte[]? _shownPng;
    private int _backgroundIndex;
    private int _sizeIndex = CurrentViewSizeIndex;
    private int _textScaleIndex;
    private double _width;
    private double _height;
    private double _zoom;
    private byte[]? _previewPng;
    private bool _isRendering;
    private bool _canSave;
    private Visibility _retryVisibility = Visibility.Collapsed;
    private string _statusText = string.Empty;
    private bool _disposed;

    /// <param name="exporter">画像を作る処理。このインスタンスが破棄する</param>
    /// <param name="source">ダイアログを開いた時点の地図。条件の初期値にする</param>
    /// <param name="delay">プレビューの前の待機。テストで差し替える</param>
    internal MapImageExportViewModel(
        IMapImageExporter exporter,
        MapImageExportSource source,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _delay = delay ?? Task.Delay;
        var sources = source.Backgrounds.Select(choice => choice.Request.SourceType).ToList();
        _backgroundIndex = sources.IndexOf(source.DisplayedSource);
        if (_backgroundIndex < 0)
        {
            throw new ArgumentException("The displayed background is not among the choices.", nameof(source));
        }

        BackgroundChoices = source.Backgrounds.Select(choice => choice.Label).ToArray();

        _textScales = StandardTextScales.Append(source.PixelDensity).Distinct().Order().ToArray();
        _textScaleIndex = Array.IndexOf(_textScales, source.PixelDensity);
        TextScaleChoices = _textScales.Select(CreateTextScaleLabel).ToArray();
        SizeChoices =
        [
            LocalizationService.GetString("MapImageExport.Size.CurrentView"),
            .. PresetSizes.Select(size => $"{size.Width} × {size.Height}"),
            LocalizationService.GetString("MapImageExport.Size.Custom")
        ];
        _zoom = WebMercator.LogicalZoomFromResolution(source.View.Resolution);
        ApplySizeChoice();
    }

    public IReadOnlyList<string> BackgroundChoices { get; }

    public IReadOnlyList<string> SizeChoices { get; }

    public IReadOnlyList<string> TextScaleChoices { get; }

    public int BackgroundIndex
    {
        get => _backgroundIndex;
        set
        {
            if (value >= 0 && value < BackgroundChoices.Count && SetProperty(ref _backgroundIndex, value))
            {
                RefreshPreview(PreviewDelay);
            }
        }
    }

    public int SizeIndex
    {
        get => _sizeIndex;
        set
        {
            // 一覧の再構築中に、選択なし（-1）が書き戻されることがある
            if (value >= 0 && value < SizeChoices.Count && SetProperty(ref _sizeIndex, value))
            {
                OnPropertyChanged(nameof(IsCustomSize));
                ApplySizeChoice();
                RefreshPreview(PreviewDelay);
            }
        }
    }

    /// <summary>幅と高さを自由に入力できる</summary>
    public bool IsCustomSize => _sizeIndex == CustomSizeIndex;

    /// <summary>画像の幅（ピクセル）。入力が空のときは非数</summary>
    public double Width
    {
        get => _width;
        set
        {
            if (IsCustomSize && SetProperty(ref _width, value))
            {
                RefreshPreview(PreviewDelay);
            }
        }
    }

    /// <summary>画像の高さ（ピクセル）。入力が空のときは非数</summary>
    public double Height
    {
        get => _height;
        set
        {
            if (IsCustomSize && SetProperty(ref _height, value))
            {
                RefreshPreview(PreviewDelay);
            }
        }
    }

    /// <summary>論理ズーム。入力が空のときは非数</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!(Math.Abs(value - _zoom) <= ZoomRoundingTolerance) && SetProperty(ref _zoom, value))
            {
                RefreshPreview(PreviewDelay);
            }
        }
    }

    public int TextScaleIndex
    {
        get => _textScaleIndex;
        set
        {
            if (value >= 0 && value < _textScales.Length && SetProperty(ref _textScaleIndex, value))
            {
                // 現在の表示範囲は、文字を大きくしても同じ範囲が写るように画素数を変える
                ApplySizeChoice();
                RefreshPreview(PreviewDelay);
            }
        }
    }

    /// <summary>プレビュー（保存する画像と同じ PNG）。ないときは <see langword="null"/></summary>
    public byte[]? PreviewPng
    {
        get => _previewPng;
        private set => SetProperty(ref _previewPng, value);
    }

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    /// <summary>いまの条件のプレビューが画面に出ていて、保存できる</summary>
    public bool CanSave
    {
        get => _canSave;
        private set => SetProperty(ref _canSave, value);
    }

    /// <summary>プレビューの作成か表示に失敗したときだけ、やり直しの操作を出す</summary>
    public Visibility RetryVisibility
    {
        get => _retryVisibility;
        private set => SetProperty(ref _retryVisibility, value);
    }

    /// <summary>取得するタイルの枚数、作れない理由、または描画の失敗</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>進行中のプレビュー。失敗や取り消しでは例外にならない</summary>
    internal Task PreviewTask => _previewTask;

    /// <summary>
    /// 開いた時点の条件で、最初のプレビューを描き始める
    /// </summary>
    internal void Start() => RefreshPreview(TimeSpan.Zero);

    /// <summary>
    /// 同じ条件で、プレビューを作り直す
    /// </summary>
    internal void Retry() => RefreshPreview(TimeSpan.Zero);

    /// <summary>
    /// 画面がプレビューを表示し終えた。いまの条件の画像なら、保存できるようにする
    /// </summary>
    /// <param name="png">表示した画像（<see cref="PreviewPng"/> の値）</param>
    internal void NotifyPreviewShown(byte[] png)
    {
        // 条件を変えた後で（元の条件へ戻した後でも）、前の画像の表示が終わることがある
        if (IsCurrentPreview(png))
        {
            _shownPng = png;
            CanSave = true;
        }
    }

    /// <summary>
    /// 画面がプレビューを表示できなかった
    /// </summary>
    internal void NotifyPreviewDisplayFailed(byte[] png)
    {
        if (IsCurrentPreview(png))
        {
            ShowPreviewFailure();
        }
    }

    /// <summary>
    /// 画面で確認した画像を返す
    /// </summary>
    /// <returns>PNG のストリーム</returns>
    /// <exception cref="InvalidOperationException">いまの条件のプレビューが表示されていない場合</exception>
    internal Stream OpenImageToSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _shownPng is { } shown
            ? new MemoryStream(shown, writable: false)
            : throw new InvalidOperationException("The preview of the current conditions has not been shown.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_previewCts is { } cts)
        {
            await cts.CancelAsync().ConfigureAwait(true);
        }

        // 描画中の通信とキャッシュを使い終えてから破棄する
        await _previewTask.ConfigureAwait(true);
        _previewCts?.Dispose();
        _previewCts = null;
        _exporter.Dispose();
    }

    private string CreateTextScaleLabel(double scale)
    {
        var percent = (int)Math.Round(scale * 100);
        return scale == _source.PixelDensity
            ? LocalizationService.Format("MapImageExport.TextScale.Current", percent)
            : $"{percent}%";
    }

    private void ApplySizeChoice()
    {
        double width;
        double height;
        if (_sizeIndex == CurrentViewSizeIndex)
        {
            var scale = _textScales[_textScaleIndex];
            width = Math.Round(_source.View.WidthDip * scale);
            height = Math.Round(_source.View.HeightDip * scale);
        }
        else if (_sizeIndex == CustomSizeIndex)
        {
            return;
        }
        else
        {
            (width, height) = PresetSizes[_sizeIndex - 1];
        }

        SetProperty(ref _width, width, nameof(Width));
        SetProperty(ref _height, height, nameof(Height));
    }

    private MapImageExportRequest CreateRequest()
        => new(
            _source.Backgrounds[_backgroundIndex].Request,
            _source.View.CenterX,
            _source.View.CenterY,
            _zoom,
            ToPixels(_width),
            ToPixels(_height),
            _textScales[_textScaleIndex],
            _source.Markers);

    private static int ToPixels(double value)
        => double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value), 0, int.MaxValue) : 0;

    private void RefreshPreview(TimeSpan delay)
    {
        if (_disposed)
        {
            return;
        }

        _previewCts?.Cancel();
        _previewCts = null;

        var request = CreateRequest();
        var plan = _exporter.Plan(request);
        InvalidatePreview();
        RetryVisibility = Visibility.Collapsed;
        StatusText = Describe(plan);
        if (plan.Problem != MapImageExportProblem.None)
        {
            IsRendering = false;
            PreviewPng = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        IsRendering = true;
        _previewTask = RenderPreviewAsync(_previewTask, request, delay, cts);
    }

    private async Task RenderPreviewAsync(
        Task previous,
        MapImageExportRequest request,
        TimeSpan delay,
        CancellationTokenSource cts)
    {
        try
        {
            // 前の描画が取り消しを終えてから始める（同じタイルキャッシュを同時に使わない）
            await previous.ConfigureAwait(true);
            await _delay(delay, cts.Token).ConfigureAwait(true);

            using var stream = await _exporter.RenderPngAsync(request, cts.Token).ConfigureAwait(true);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cts.Token).ConfigureAwait(true);
            cts.Token.ThrowIfCancellationRequested();

            var png = buffer.ToArray();
            _renderedPng = png;
            PreviewPng = png;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 条件が変わったか、ダイアログを閉じた
        }
        // プレビューの失敗はダイアログに表示する。待つ側がいないため、ここで終端する
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.Error("Failed to render the map image preview.", ex);
            if (!cts.IsCancellationRequested)
            {
                ShowPreviewFailure();
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts))
            {
                _previewCts = null;
                IsRendering = false;
            }

            cts.Dispose();
        }
    }

    private bool IsCurrentPreview(byte[] png)
        => !_disposed && ReferenceEquals(_renderedPng, png);

    private void InvalidatePreview()
    {
        _renderedPng = null;
        _shownPng = null;
        CanSave = false;
    }

    private void ShowPreviewFailure()
    {
        InvalidatePreview();
        PreviewPng = null;
        RetryVisibility = Visibility.Visible;
        StatusText = LocalizationService.GetString("MapImageExport.Status.PreviewFailed");
    }

    private static string Describe(MapImageExportPlan plan)
        => plan.Problem switch
        {
            MapImageExportProblem.None => LocalizationService.Format("MapImageExport.Status.TileCount", plan.TileCount),
            MapImageExportProblem.TooManyTiles => LocalizationService.Format(
                "MapImageExport.Problem.TooManyTiles", plan.TileCount, MapImageExportLimits.MaxTileCount),
            MapImageExportProblem.SizeOutOfRange => LocalizationService.Format(
                "MapImageExport.Problem.Size", MapImageExportLimits.MinPixelSize, MapImageExportLimits.MaxPixelSize),
            MapImageExportProblem.ZoomOutOfRange => LocalizationService.Format(
                "MapImageExport.Problem.Zoom", MapImageExportLimits.MinLogicalZoom, MapImageExportLimits.MaxLogicalZoom),
            MapImageExportProblem.CenterOutOfRange => LocalizationService.GetString("MapImageExport.Problem.Center"),
            MapImageExportProblem.PixelDensityOutOfRange => LocalizationService.GetString("MapImageExport.Problem.TextScale"),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Problem, "Unknown problem.")
        };
}
