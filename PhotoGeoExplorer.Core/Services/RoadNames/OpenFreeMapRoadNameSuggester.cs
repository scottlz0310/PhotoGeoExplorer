using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services.RoadNames;

/// <summary>
/// OpenFreeMap から道路のタイルを取得し（端末にキャッシュする）、座標の最寄りの道路名の候補を求める
/// </summary>
/// <remarks>
/// 配信元へ送るのは、タイル（z14。撮影地点を含むもの、縁に近いときは隣のもの）の番号だけで、写真や緯度経度そのものは送らない。
/// 通信するのは、判定を呼んだときだけ
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
    public OpenFreeMapRoadNameSuggester(string cacheDirectory, string userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        _httpClient = new HttpClient { Timeout = RequestTimeout };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _cache = new FileSystemTileCache(
            cacheDirectory,
            maxSizeBytes: MaxCacheBytes,
            errorObserver: exception => AppLog.Error("The road tile cache failed.", exception));
        _tileSource = new OpenFreeMapTileSource(_httpClient, urlTemplateStore: _cache);
        _suggester = new RoadNameSuggester(new VectorRoadTileSource(new CachedVectorTileSource(_tileSource, _cache)));
    }

    public Task<IReadOnlyList<RoadNameCandidate>> SuggestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
        => _suggester.SuggestAsync(latitude, longitude, cancellationToken);

    public void Dispose()
    {
        _tileSource.Dispose();
        _cache.Dispose();
        _httpClient.Dispose();
    }
}
