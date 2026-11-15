using System.Globalization;
using System.Net;

namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// e-Stat の「国勢調査 小地域（町丁・字等）境界データ」（令和 2 年・JGD2011・緯度経度・Shape）を、都道府県単位で取得して取り込む。
/// 1 回の操作で 1 つの ZIP を順に取得し、一覧ページは読まない。認証が要る応答や ZIP ではない応答は、取得を止めて報告する
/// （認証情報は扱わない。手動の取り込みへ戻れる）。
/// </summary>
internal sealed class AreaDataDownloadService : IAreaDataDownloader
{
    /// <summary>令和 2 年国勢調査 町丁・字等境界データの識別子。新しい調査は、アプリの更新で扱う</summary>
    internal const string SurveyId = "A002005212020";

    /// <summary>
    /// ダウンロード中の受信量の上限。展開後の上限（<see cref="AreaDataStore.MaxExtractedBytes"/>）は ZIP の保存後に評価されるため、別に要る。
    /// 実際の ZIP のサイズに合わせて調整する（北海道の展開後は約 32MB）
    /// </summary>
    internal const long DefaultMaxDownloadBytes = 256L * 1024 * 1024;

    internal static readonly Uri DefaultBaseUri = new("https://www.e-stat.go.jp/", UriKind.Absolute);

    private static readonly string[] AcceptedMediaTypes = ["application/zip", "application/octet-stream", "application/x-zip-compressed"];

    private readonly HttpClient _httpClient;
    private readonly IAreaDataStore _store;
    private readonly string _temporaryDirectory;
    private readonly long _maxDownloadBytes;
    private readonly Uri _baseUri;

    public AreaDataDownloadService(
        HttpClient httpClient,
        IAreaDataStore store,
        string? temporaryDirectory = null,
        long maxDownloadBytes = DefaultMaxDownloadBytes,
        Uri? baseUri = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDownloadBytes);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        _maxDownloadBytes = maxDownloadBytes;
        _baseUri = baseUri ?? DefaultBaseUri;
    }

    /// <summary>都道府県の ZIP のダウンロード URL</summary>
    internal static Uri BuildUri(Uri baseUri, string prefectureCode)
    {
        if (!PrefectureCatalog.IsValidCode(prefectureCode))
        {
            throw new ArgumentException($"Not a prefecture code: {prefectureCode}", nameof(prefectureCode));
        }

        return new Uri(
            baseUri,
            string.Create(
                CultureInfo.InvariantCulture,
                $"gis/statmap-search/data?dlserveyId={SurveyId}&code={prefectureCode}&coordSys=1&format=shape&downloadType=5&datum=2011"));
    }

    public async Task<AreaDataPackInfo> DownloadAsync(string prefectureCode, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var uri = BuildUri(_baseUri, prefectureCode);
        Directory.CreateDirectory(_temporaryDirectory);
        var temporaryPath = Path.Combine(_temporaryDirectory, $"photogeoexplorer-areadata-{Guid.NewGuid():N}.zip");
        try
        {
            // 取り込みは、呼び出し元の同期コンテキスト（UI スレッド）で始める。取り込みの完了通知（DataChanged）は、
            // 取り込みを呼んだ側のコンテキストで発火し、表示中の写真の住所の更新が UI スレッドで走るため
            await DownloadToFileAsync(uri, temporaryPath, progress, cancellationToken).ConfigureAwait(true);
            return await _store.ImportAsync(temporaryPath, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private async Task DownloadToFileAsync(Uri uri, string path, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            ValidateResponse(response);

            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            await using (file.ConfigureAwait(false))
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    await CopyWithLimitAsync(stream, file, progress, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            throw new AreaDataDownloadException(AreaDataDownloadError.Network, "Failed to download the area data.", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient のタイムアウト（呼び出し元のキャンセルではない）
            throw new AreaDataDownloadException(AreaDataDownloadError.Network, "The area data download timed out.", innerException: ex);
        }
    }

    private void ValidateResponse(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new AreaDataDownloadException(
                AreaDataDownloadError.HttpStatus,
                $"The server returned {(int)response.StatusCode}.",
                response.StatusCode);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !AcceptedMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
        {
            // ログイン画面への遷移（HTML）など。自動ログインはしない
            throw new AreaDataDownloadException(
                AreaDataDownloadError.NotArchive,
                $"The response is not an archive (content type: {mediaType ?? "none"}).");
        }

        if (response.Content.Headers.ContentLength is { } length && length > _maxDownloadBytes)
        {
            throw new AreaDataDownloadException(AreaDataDownloadError.TooLarge, "The download exceeds the size limit.");
        }
    }

    private async Task CopyWithLimitAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > _maxDownloadBytes)
            {
                throw new AreaDataDownloadException(AreaDataDownloadError.TooLarge, "The download exceeds the size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(total);
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 次回以降の一時フォルダーの掃除に任せる。取り込みの結果は変えない
            AppLog.Error("Failed to delete the temporary area data archive.", ex);
        }
    }
}
