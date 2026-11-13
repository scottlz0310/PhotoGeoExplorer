using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;

namespace PhotoGeoExplorer.Services.Export;

/// <summary>
/// 書き出しの対象の写真から、撮影日時・撮影位置・字地名を読む。背景で並列に処理し、進捗を通知する。
/// 結果は、入力と同じ順序で返す
/// </summary>
internal sealed class PhotoGeoRecordLoader
{
    internal const int DefaultMaxDegreeOfParallelism = 4;

    private readonly Func<string, CancellationToken, Task<PhotoMetadata?>> _readMetadata;
    private readonly IAreaNameResolver _areaNameResolver;
    private readonly int _maxDegreeOfParallelism;

    /// <param name="readMetadata">写真のメタデータを読む。読めなければ null</param>
    /// <param name="areaNameResolver">座標から字地名を判定する</param>
    public PhotoGeoRecordLoader(
        Func<string, CancellationToken, Task<PhotoMetadata?>> readMetadata,
        IAreaNameResolver areaNameResolver,
        int maxDegreeOfParallelism = DefaultMaxDegreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        _readMetadata = readMetadata ?? throw new ArgumentNullException(nameof(readMetadata));
        _areaNameResolver = areaNameResolver ?? throw new ArgumentNullException(nameof(areaNameResolver));
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    /// <param name="progress">写真 1 枚ごとに、読み終えた枚数を通知する</param>
    /// <exception cref="OperationCanceledException">キャンセルされた</exception>
    /// <exception cref="IOException">地域データを読めなかった</exception>
    public async Task<IReadOnlyList<PhotoGeoRecord>> LoadAsync(
        IReadOnlyList<string> filePaths,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var records = new PhotoGeoRecord[filePaths.Count];
        var completed = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, filePaths.Count),
            options,
            async (index, token) =>
            {
                records[index] = await LoadOneAsync(filePaths[index], token).ConfigureAwait(false);
                progress?.Report(Interlocked.Increment(ref completed));
            }).ConfigureAwait(false);

        return records;
    }

    private async Task<PhotoGeoRecord> LoadOneAsync(string filePath, CancellationToken cancellationToken)
    {
        // 読めない写真は、位置情報なしとして扱う（理由は、メタデータを読む側がログに残す）
        var metadata = await _readMetadata(filePath, cancellationToken).ConfigureAwait(false);
        if (metadata is not { HasValidLocation: true, Latitude: { } latitude, Longitude: { } longitude })
        {
            return new PhotoGeoRecord(filePath, metadata?.TakenAt, null, PhotoAreaKind.NoLocation, null);
        }

        var coordinate = new PhotoCoordinate(latitude, longitude);
        var area = await _areaNameResolver.ResolveAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
        return area is null
            ? new PhotoGeoRecord(filePath, metadata.TakenAt, coordinate, PhotoAreaKind.NoAreaData, null)
            : new PhotoGeoRecord(filePath, metadata.TakenAt, coordinate, PhotoAreaKind.Resolved, area);
    }
}
