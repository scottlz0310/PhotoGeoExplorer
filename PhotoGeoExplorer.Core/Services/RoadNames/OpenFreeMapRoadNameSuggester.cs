using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// OpenFreeMap から道路のタイルを取得し（端末にキャッシュする）、座標の最寄りの道路名の候補を求める
/// </summary>
/// <remarks>
/// 配信元へ送るのは、タイル（z14。撮影地点を含むもの、縁に近いときは隣のもの）の番号だけで、写真や緯度経度そのものは送らない。
/// 通信するのは、判定を呼んだときだけ。
/// <see cref="Dispose"/> は進行中の判定へキャンセルを要求するだけで、依存する資源（通信・キャッシュ）の破棄は、
/// 進行中の判定がすべて終わってから行う（キャンセルの完了前に破棄すると、取得処理の終了処理が破棄済みの資源に触れるため）
/// </remarks>
internal sealed class OpenFreeMapRoadNameSuggester : IRoadNameSuggester, IDisposable
{
    // 選択を切り替えるたびに呼ばれるので、応答のない通信で待ち続けない
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // 道路のタイルは 1 枚で数百 KB になることがあるため、パッケージの既定（1 GB）より小さく抑える
    private const long MaxCacheBytes = 200L * 1024L * 1024L;

    private readonly HttpClient _httpClient;
    private readonly FileSystemTileCache _cache;
    private readonly OpenFreeMapTileSource _tileSource;
    private readonly RoadNameSuggester _suggester;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private int _activeCalls;
    private bool _disposed;

    /// <summary>
    /// 道路のタイルのキャッシュの既定の置き場（地図タイルのキャッシュ <c>Cache\Tiles</c> とは別）
    /// </summary>
    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Cache",
        "RoadTiles");

    /// <param name="cacheDirectory">タイルのキャッシュの置き場</param>
    /// <param name="userAgent">配信元へ表明する User-Agent（アプリの名称・版・連絡先）</param>
    /// <param name="handler">通信の中身。キャンセルと破棄の順序をテストで再現するために差し替える（<see langword="null"/> なら既定）</param>
    public OpenFreeMapRoadNameSuggester(string cacheDirectory, string userAgent, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        _httpClient = handler is null
            ? new HttpClient { Timeout = RequestTimeout }
            : new HttpClient(handler) { Timeout = RequestTimeout };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _cache = new FileSystemTileCache(
            cacheDirectory,
            maxSizeBytes: MaxCacheBytes,
            errorObserver: exception => AppLog.Error("The road tile cache failed.", exception));
        _tileSource = new OpenFreeMapTileSource(_httpClient, urlTemplateStore: _cache);
        _suggester = new RoadNameSuggester(new VectorRoadTileSource(new CachedVectorTileSource(_tileSource, _cache)));
    }

    public async Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        EnterCall();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            return await _suggester.SuggestAsync(latitude, longitude, linked.Token).ConfigureAwait(false);
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
        _tileSource.Dispose();
        _cache.Dispose();
        _httpClient.Dispose();
        _lifetime.Dispose();
    }
}
