using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhotoMapStudio.Rendering;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// OpenFreeMap のベクタータイルを、表示範囲全体の 1 枚の画像として描く。通信・タイルのキャッシュ・スタイル・
/// デコード済みタイルのキャッシュをこのクラスが所有する。
/// </summary>
/// <remarks>
/// 通信するのは描画を呼んだときだけ。<see cref="Dispose"/> は進行中の描画へキャンセルを要求するだけで、依存する資源
/// （通信・キャッシュ）の破棄は、進行中の描画がすべて終わってから行う（キャンセルの完了前に破棄すると、
/// 取得処理の終了処理が破棄済みの資源に触れるため）。
/// </remarks>
internal sealed class OpenFreeMapVectorMapRenderer : IVectorMapRenderer, IDisposable
{
    // 応答のない通信で、描画を待たせ続けない
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // 設計合意（#295）。ベクター背景を選んでいる間だけ持つ固定の上限で、設定にしない
    private const long DecodedTileCacheBytes = 128L * 1024L * 1024L;

    private readonly HttpClient _httpClient;
    private readonly FileSystemTileCache _cache;
    private readonly OpenFreeMapTileSource _tileSource;
    private readonly OpenMapTilesStyle _style;
    private readonly VectorMapRenderer _renderer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private int _activeCalls;
    private bool _disposed;

    /// <summary>
    /// 地図のタイルのキャッシュの置き場（地図タイルのキャッシュ <c>Cache\Tiles</c>・道路名の <c>Cache\RoadTiles</c> とは別）。
    /// 道路名の判定は z14 の 1 枚ずつを上限つきで持つ用途で、閲覧で広く触れる地図とは容量・消し方が違うため分ける。
    /// </summary>
    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Cache",
        "VectorTiles");

    /// <param name="cacheDirectory">タイルのキャッシュの置き場</param>
    /// <param name="userAgent">配信元へ表明する User-Agent（アプリの名称・版・連絡先）</param>
    /// <param name="handler">通信の中身。テストで差し替える（<see langword="null"/> なら既定）</param>
    public OpenFreeMapVectorMapRenderer(string cacheDirectory, string userAgent, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        _httpClient = handler is null
            ? new HttpClient { Timeout = RequestTimeout }
            : new HttpClient(handler) { Timeout = RequestTimeout };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _cache = new FileSystemTileCache(
            cacheDirectory,
            errorObserver: exception => AppLog.Error("The vector tile cache failed.", exception));
        _tileSource = new OpenFreeMapTileSource(_httpClient, urlTemplateStore: _cache);
        _style = new OpenMapTilesStyle();
        _renderer = new VectorMapRenderer(
            new CachedVectorTileSource(_tileSource, _cache),
            _style,
            new DecodedTileCache(DecodedTileCacheBytes));
    }

    public async Task<VectorViewResult> RenderViewAsync(VectorViewRequest request, CancellationToken cancellationToken = default)
    {
        EnterCall();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            return await _renderer.RenderViewAsync(request, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            ExitCall();
        }
    }

    public void Dispose()
    {
        bool releaseNow;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetime.Cancel();
            releaseNow = _activeCalls == 0;
        }

        if (releaseNow)
        {
            ReleaseResources();
        }
    }

    private void EnterCall()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeCalls++;
        }
    }

    private void ExitCall()
    {
        bool releaseNow;
        lock (_gate)
        {
            _activeCalls--;
            releaseNow = _disposed && _activeCalls == 0;
        }

        if (releaseNow)
        {
            ReleaseResources();
        }
    }

    private void ReleaseResources()
    {
        _style.Dispose();
        _tileSource.Dispose();
        _cache.Dispose();
        _httpClient.Dispose();
        _lifetime.Dispose();
    }
}
