using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.AreaNames;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 取り込み・一覧・削除をメモリ上で行う <see cref="IAreaDataStore"/>。失敗と待機を差し込める
/// </summary>
internal sealed class FakeAreaDataStore : IAreaDataStore
{
    private readonly List<AreaDataPackInfo> _packs = [];

    public List<string> ImportedArchives { get; } = [];

    public List<string> DeletedCodes { get; } = [];

    public int ListCallCount { get; private set; }

    /// <summary>設定すると、一覧・取り込み・削除がこの例外を投げる</summary>
    public Exception? Failure { get; set; }

    /// <summary>取り込みが返す情報（取り込み済みとして一覧にも加わる）</summary>
    public AreaDataPackInfo ImportResult { get; set; } = new("31", "鳥取県", 2020, 5_000_000);

    /// <summary>設定すると、取り込みがこのタスクの完了まで待つ</summary>
    public Task? ImportGate { get; set; }

    public void Add(AreaDataPackInfo info) => _packs.Add(info);

    public Task<IReadOnlyList<AreaDataPackInfo>> ListAsync(CancellationToken cancellationToken)
    {
        ListCallCount++;
        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<AreaDataPackInfo>>(Failure);
        }

        return Task.FromResult<IReadOnlyList<AreaDataPackInfo>>(_packs.OrderBy(pack => pack.PrefectureCode, StringComparer.Ordinal).ToList());
    }

    public async Task<AreaDataPackInfo> ImportAsync(string archivePath, CancellationToken cancellationToken)
    {
        ImportedArchives.Add(archivePath);
        if (ImportGate is not null)
        {
            await ImportGate.ConfigureAwait(false);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        _packs.RemoveAll(pack => pack.PrefectureCode == ImportResult.PrefectureCode);
        _packs.Add(ImportResult);
        return ImportResult;
    }

    public Task<bool> DeleteAsync(string prefectureCode, CancellationToken cancellationToken)
    {
        DeletedCodes.Add(prefectureCode);
        if (Failure is not null)
        {
            return Task.FromException<bool>(Failure);
        }

        return Task.FromResult(_packs.RemoveAll(pack => pack.PrefectureCode == prefectureCode) > 0);
    }

    public static IOException NewIOException() => new("simulated failure");
}
