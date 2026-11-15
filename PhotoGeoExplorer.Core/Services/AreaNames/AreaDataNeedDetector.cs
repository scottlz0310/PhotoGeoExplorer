namespace PhotoGeoExplorer.Services.AreaNames;

/// <summary>
/// 座標の字地名を求めるのに、取得が要る都道府県を調べる
/// </summary>
internal interface IAreaDataNeedDetector
{
    /// <returns>地域データが無い都道府県のコード。座標が国外、または候補のどれかが導入済みなら空</returns>
    Task<IReadOnlyList<string>> FindMissingAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

/// <summary>
/// 同梱の都道府県ポリゴン（<see cref="IPrefectureLocator"/>）で座標の候補を引き、導入済みの地域データと照らす。
/// 県境付近で候補が複数あっても、どれかが導入済みなら取得は要らない（導入済みのデータで判定できる可能性があるため）
/// </summary>
internal sealed class AreaDataNeedDetector : IAreaDataNeedDetector
{
    private readonly IPrefectureLocator _locator;
    private readonly IAreaDataStore _store;

    public AreaDataNeedDetector(IPrefectureLocator locator, IAreaDataStore store)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<string>> FindMissingAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var candidates = _locator.FindCandidates(latitude, longitude);
        if (candidates.Count == 0)
        {
            return [];
        }

        var installed = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var installedCodes = installed.Select(pack => pack.PrefectureCode).ToHashSet(StringComparer.Ordinal);
        return candidates.Any(installedCodes.Contains) ? [] : candidates;
    }
}
