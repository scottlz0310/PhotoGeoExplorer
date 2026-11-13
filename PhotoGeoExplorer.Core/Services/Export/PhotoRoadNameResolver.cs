using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// 書き出しに出す道路名の、確定の程度
/// </summary>
internal enum RoadNameStatus
{
    /// <summary>利用者が写真ごとに確定した値</summary>
    Confirmed,

    /// <summary>未確定。最寄りの道路の第 1 候補</summary>
    Candidate
}

/// <summary>
/// 書き出しの対象にする 1 枚の写真の道路名
/// </summary>
/// <param name="Status">確定した値か、第 1 候補か</param>
/// <param name="Name">名称。名称のない道路は null</param>
/// <param name="Ref">路線番号。なければ null</param>
/// <remarks><paramref name="Name"/> と <paramref name="Ref"/> の少なくとも一方がある</remarks>
internal sealed record RoadNameInfo(RoadNameStatus Status, string? Name, string? Ref)
{
    /// <summary>
    /// 名称があればそれを、なければ路線番号から作った表記（「路線番号 203」。言語ごとに異なる）を返す
    /// </summary>
    public string Format(Func<string, string> formatRouteNumber)
    {
        ArgumentNullException.ThrowIfNull(formatRouteNumber);
        return Name ?? formatRouteNumber(Ref ?? string.Empty);
    }
}

/// <summary>
/// 書き出しの 1 回分の道路名の取得を用意する
/// </summary>
internal sealed class PhotoRoadNameResolver
{
    private readonly IRoadNameConfirmationStore _store;
    private readonly IRoadNameSuggester? _suggester;
    private readonly Func<string, long?> _getFileSize;

    /// <param name="store">利用者が確定した道路名</param>
    /// <param name="suggester">最寄り道路名の候補（通信する）。null なら候補は取らない</param>
    /// <param name="getFileSize">写真のファイルサイズ（確定値の照合に使う）。読めなければ null</param>
    public PhotoRoadNameResolver(
        IRoadNameConfirmationStore store,
        IRoadNameSuggester? suggester,
        Func<string, long?>? getFileSize = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _suggester = suggester;
        _getFileSize = getFileSize ?? GetFileSizeOrNull;
    }

    /// <param name="suggestCandidates">未確定の写真に、最寄り道路の第 1 候補を求める（通信する）。false なら、確定した値だけを出す</param>
    public RoadNameLookup CreateLookup(bool suggestCandidates)
        => new(_store, suggestCandidates ? _suggester : null, _getFileSize);

    private static long? GetFileSizeOrNull(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// 書き出し 1 回分の道路名の取得。確定した値を先に引き、なければ（候補を取るときは）第 1 候補を求める。
/// 通信できなかったときは、その回の残りの写真では候補を取らない（応答を待ち続けて、書き出しが終わらなくなるのを避ける）
/// </summary>
internal sealed class RoadNameLookup
{
    private readonly IRoadNameConfirmationStore _store;
    private readonly IRoadNameSuggester? _suggester;
    private readonly Func<string, long?> _getFileSize;
    private int _suggestionsStopped;
    private int _failedPhotos;

    internal RoadNameLookup(IRoadNameConfirmationStore store, IRoadNameSuggester? suggester, Func<string, long?> getFileSize)
    {
        _store = store;
        _suggester = suggester;
        _getFileSize = getFileSize;
    }

    /// <summary>
    /// 道路名を取得できなかった写真がある（通信できない・保存を読めない）。その写真の道路名は空になる
    /// </summary>
    public bool HasFailures => Volatile.Read(ref _failedPhotos) > 0;

    /// <returns>道路名。確定しておらず候補もないときは null</returns>
    /// <exception cref="OperationCanceledException">キャンセルされた</exception>
    public async Task<RoadNameInfo?> ResolveAsync(
        string filePath,
        DateTimeOffset? takenAt,
        PhotoCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (await FindConfirmedAsync(filePath, takenAt, cancellationToken).ConfigureAwait(false) is { } confirmed)
        {
            return new RoadNameInfo(RoadNameStatus.Confirmed, confirmed.Name, confirmed.Ref);
        }

        return await SuggestFirstAsync(coordinate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConfirmedRoadName?> FindConfirmedAsync(string filePath, DateTimeOffset? takenAt, CancellationToken cancellationToken)
    {
        if (_getFileSize(filePath) is not { } fileSize)
        {
            return null;
        }

        try
        {
            return await _store.FindAsync(new PhotoIdentity(filePath, fileSize, takenAt), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to read the confirmed road name of a photo for the export: {filePath}", ex);
            Interlocked.Increment(ref _failedPhotos);
            return null;
        }
    }

    private async Task<RoadNameInfo?> SuggestFirstAsync(PhotoCoordinate coordinate, CancellationToken cancellationToken)
    {
        if (_suggester is null || Volatile.Read(ref _suggestionsStopped) != 0)
        {
            return null;
        }

        try
        {
            var candidates = await _suggester.SuggestAsync(coordinate.Latitude, coordinate.Longitude, cancellationToken).ConfigureAwait(false);
            return candidates.Count == 0
                ? null
                : new RoadNameInfo(RoadNameStatus.Candidate, candidates[0].Name, candidates[0].Ref);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // 通信の失敗と、通信のタイムアウト（呼び出し元のキャンセルではない）は、以降の写真でも続くことが多い
            AppLog.Error("Failed to suggest a road name for a photo of the export. No more candidates are requested in this export.", ex);
            Interlocked.Increment(ref _failedPhotos);
            Volatile.Write(ref _suggestionsStopped, 1);
            return null;
        }
    }
}
