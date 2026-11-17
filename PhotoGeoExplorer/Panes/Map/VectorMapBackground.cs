using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoMapStudio.Rendering;
using SkiaSharp;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// ベクタータイルを表示範囲全体の 1 枚の画像として描く背景（OpenFreeMap）。視点が変わるたびに描画を要求し、
/// 描けた画像を <see cref="ViewExtentImageLayer"/> へ載せる。操作中は直前の画像が視点に追従する。
/// </summary>
/// <remarks>
/// <see cref="Attach"/> を呼ぶまでは描かない。Mapsui はアニメーション中の視点変更を UI スレッド以外で通知するため、
/// 通知の処理では XAML に触らず、画素密度は呼び出し側が渡す取得関数（任意のスレッドから呼べること）で読む。
/// </remarks>
internal sealed class VectorMapBackground : IMapBackground
{
    /// <summary>
    /// 描画元とスタイルの識別子。値が違う画像どうしは互いの代わりに表示できない。スタイルの見た目を変えたら版を上げる。
    /// </summary>
    internal const string SourceKey = "openfreemap-vector:1";

    // 並列の帯の数。最良値はコア数に近く、多すぎると遅くなる（設計合意 #295）
    private static readonly int DefaultBandCount = Math.Clamp(Environment.ProcessorCount, 1, 8);

    private readonly IVectorMapRenderer _renderer;
    private readonly ViewExtentImageLayer _layer;
    private readonly ViewExtentRenderCoordinator<SKImage> _coordinator;
    private Mapsui.Map? _map;
    private Func<double>? _getPixelDensity;
    private bool _wasIncomplete;
    private bool _disposed;

    /// <param name="renderer">描画エンジン。この背景が所有し、破棄のときに（<see cref="IDisposable"/> なら）破棄する。</param>
    /// <param name="tryPostToUi">処理を UI スレッドへ渡す。渡せなかったら false。</param>
    internal VectorMapBackground(IVectorMapRenderer renderer, Func<Action, bool> tryPostToUi)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(tryPostToUi);

        _renderer = renderer;
        _layer = new ViewExtentImageLayer("VectorMapBackground");
        Layers = [_layer.Layer];
        AttributionText = string.Join(' ', MapAttribution.VectorLinks.Select(link => link.Text));
        _coordinator = new ViewExtentRenderCoordinator<SKImage>(
            RenderAsync,
            tryPostToUi,
            _layer.Show,
            OnRenderFailed);
        _coordinator.BusyChanged += OnCoordinatorBusyChanged;
    }

    public IReadOnlyList<ILayer> Layers { get; }

    public string AttributionText { get; }

    public bool IsBusy => _coordinator.IsBusy;

    public event EventHandler? BusyChanged;

    /// <summary>
    /// 地図の視点の監視を始め、今の視点で最初の描画を要求する。1 回だけ呼べる。
    /// </summary>
    /// <param name="map">視点を監視する地図。</param>
    /// <param name="getPixelDensity">画素密度（物理ピクセル／論理ピクセル）の取得。任意のスレッドから呼ばれる。</param>
    internal void Attach(Mapsui.Map map, Func<double> getPixelDensity)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(getPixelDensity);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_map is not null)
        {
            throw new InvalidOperationException("The vector map background is already attached.");
        }

        _map = map;
        _getPixelDensity = getPixelDensity;
        map.Navigator.ViewportChanged += OnViewportChanged;
        SubmitCurrentViewport();
    }

    public async Task<IMapExportLayers> CreateExportLayersAsync(
        Viewport viewport,
        float pixelDensity,
        CancellationToken cancellationToken)
    {
        if (!ViewExtentRequest.TryCreate(
                viewport.CenterX,
                viewport.CenterY,
                viewport.Resolution,
                viewport.Width,
                viewport.Height,
                pixelDensity,
                SourceKey,
                out var request))
        {
            throw new ArgumentException("The viewport cannot be rendered.", nameof(viewport));
        }

        // 保存では欠けた地図を黙って書き出さない。欠落タイルがあれば描画側が例外にする
        using var result = await _renderer
            .RenderViewAsync(CreateViewRequest(request, allowIncompleteMaps: false), cancellationToken)
            .ConfigureAwait(false);

        var layer = new ViewExtentImageLayer("VectorMapBackgroundExport");
        layer.Show(new ViewExtentFrame<SKImage>(request, 0, new SharedResource<SKImage>(result.TakeImage())));
        return new ExportLayers(layer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_map is { } map)
        {
            map.Navigator.ViewportChanged -= OnViewportChanged;
        }

        _coordinator.BusyChanged -= OnCoordinatorBusyChanged;
        _coordinator.Dispose();
        _layer.Dispose();
        (_renderer as IDisposable)?.Dispose();
    }

    /// <summary>
    /// 描画要求を、描画エンジンの入力（緯度経度・連続ズーム・物理ピクセル寸法）へ変換する。
    /// </summary>
    /// <param name="request">表示範囲の描画要求。</param>
    /// <param name="allowIncompleteMaps">
    /// 通信の失敗などで欠けたタイルを許して、取得できたタイルだけで描くか。表示では許し（結果で判別する）、保存では許さない。
    /// </param>
    internal static VectorViewRequest CreateViewRequest(ViewExtentRequest request, bool allowIncompleteMaps)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new VectorViewRequest(
            WebMercator.ToLatitude(request.CenterY),
            WebMercator.ToLongitude(request.CenterX),
            WebMercator.ZoomFromResolution(request.Resolution, request.PixelDensity),
            request.ImageWidth,
            request.ImageHeight)
        {
            AllowIncompleteMaps = allowIncompleteMaps,
            BandCount = DefaultBandCount,
            ParallelLabels = true,
        };
    }

    private async Task<SKImage> RenderAsync(ViewExtentRequest request, CancellationToken cancellationToken)
    {
        using var result = await _renderer
            .RenderViewAsync(CreateViewRequest(request, allowIncompleteMaps: true), cancellationToken)
            .ConfigureAwait(false);
        ReportCompleteness(result);
        return result.TakeImage();
    }

    // 欠けたタイルは白地のまま表示される。気づけるよう、欠けた状態になったときと戻ったときだけ記録する
    // （描画は 1 つずつ逐次に走るので、状態の更新に排他は要らない）
    private void ReportCompleteness(VectorViewResult result)
    {
        if (result.IsComplete == !_wasIncomplete)
        {
            return;
        }

        _wasIncomplete = !result.IsComplete;
        AppLog.Info(result.IsComplete
            ? "The vector map is complete again."
            : $"The vector map has missing tiles (unavailable: {result.UnavailableTileCount}, absent: {result.AbsentTileCount}).");
    }

    private static void OnRenderFailed(ViewExtentRequest request, Exception exception)
        => AppLog.Error("Failed to render the vector map.", exception);

    private void OnViewportChanged(object? sender, EventArgs e) => SubmitCurrentViewport();

    private void SubmitCurrentViewport()
    {
        if (_map is not { } map || _getPixelDensity is not { } getPixelDensity)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        if (ViewExtentRequest.TryCreate(
                viewport.CenterX,
                viewport.CenterY,
                viewport.Resolution,
                viewport.Width,
                viewport.Height,
                getPixelDensity(),
                SourceKey,
                out var request))
        {
            _coordinator.Submit(request);
        }
    }

    private void OnCoordinatorBusyChanged(object? sender, EventArgs e)
        => BusyChanged?.Invoke(this, EventArgs.Empty);

    private sealed class ExportLayers(ViewExtentImageLayer layer) : IMapExportLayers
    {
        public IReadOnlyList<ILayer> Layers { get; } = [layer.Layer];

        public void Dispose() => layer.Dispose();
    }
}
