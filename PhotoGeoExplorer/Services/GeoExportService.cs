using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services.Export;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// 書き出し（コピー）の結果。キャンセルされたときも、そこまでの件数を返す
/// </summary>
internal sealed record ExportCopyResult(int SuccessCount, int SkipCount, IReadOnlyList<FileOperationFailure> Failures, bool Canceled)
{
    public int FailureCount => Failures.Count;
}

internal interface IGeoExportService
{
    /// <summary>
    /// 計画のとおりに、元のファイルを出力先へコピーする（元のファイルは変更しない）。
    /// 出力先に同名のファイルがあれば <paramref name="resolveConflictAsync"/> で上書き・スキップ・中止を選ぶ
    /// （指定がなければ、上書きせずにスキップする）。1 件の失敗では止めず、キャンセルされたら、そこまでの件数を返す
    /// </summary>
    /// <param name="progress">1 件ごとに、処理した件数（コピー・スキップ・失敗の合計）を通知する</param>
    Task<ExportCopyResult> CopyAsync(
        IReadOnlyList<ExportPlanItem> plan,
        string destinationRoot,
        Func<string, Task<ConflictResolution>>? resolveConflictAsync,
        IProgress<int>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 台帳用の CSV を書き出す。同名のファイルは上書きする（上書きの確認は、保存先を選ぶダイアログが行う）
    /// </summary>
    /// <exception cref="IOException">書き出せなかった</exception>
    /// <exception cref="UnauthorizedAccessException">書き込む権限がなかった</exception>
    Task WriteCsvAsync(string filePath, IReadOnlyList<PhotoGeoRecord> records, GeoCsvLabels labels, CancellationToken cancellationToken);
}

internal sealed class GeoExportService : IGeoExportService
{
    public async Task<ExportCopyResult> CopyAsync(
        IReadOnlyList<ExportPlanItem> plan,
        string destinationRoot,
        Func<string, Task<ConflictResolution>>? resolveConflictAsync,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(destinationRoot);

        var root = Path.GetFullPath(destinationRoot);

        // 選択した写真の元のファイル。出力先がこれと重なっても、上書きしない（「元の写真は変更しない」）
        var originals = plan.Select(item => Path.GetFullPath(item.SourcePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var success = 0;
        var skipped = 0;
        var failures = new List<FileOperationFailure>();
        var stickyResolution = (ConflictResolution?)null;
        var canceled = false;
        var processed = 0;

        foreach (var item in plan)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var destination = Path.GetFullPath(Path.Combine(root, item.RelativePath));
            var fileName = Path.GetFileName(destination);

            // 計画の名前は整形済みだが、出力先の外へ出る相対パスは、念のため受け付けない
            if (!IsPathUnderRoot(destination, root))
            {
                failures.Add(new FileOperationFailure(item.SourcePath, fileName, FileOperationError.InvalidName));
            }
            else if (string.Equals(Path.GetFullPath(item.SourcePath), destination, StringComparison.OrdinalIgnoreCase))
            {
                // 元のファイルへのコピーは、何もしない
                skipped++;
            }
            else if (originals.Contains(destination))
            {
                // 別の選択写真の元のファイルへの出力は、上書きの確認も出さずに、失敗にする
                failures.Add(new FileOperationFailure(item.SourcePath, fileName, FileOperationError.AlreadyExists));
            }
            else
            {
                var outcome = await CopyOneAsync(item, destination, fileName, resolveConflictAsync, stickyResolution).ConfigureAwait(true);
                stickyResolution = outcome.StickyResolution ?? stickyResolution;
                switch (outcome.Kind)
                {
                    case CopyOutcomeKind.Copied:
                        success++;
                        break;
                    case CopyOutcomeKind.Skipped:
                        skipped++;
                        break;
                    case CopyOutcomeKind.Canceled:
                        canceled = true;
                        break;
                    default:
                        failures.Add(new FileOperationFailure(item.SourcePath, fileName, outcome.Error));
                        break;
                }

                if (canceled)
                {
                    break;
                }
            }

            progress?.Report(++processed);
        }

        return new ExportCopyResult(success, skipped, failures, canceled);
    }

    public Task WriteCsvAsync(string filePath, IReadOnlyList<PhotoGeoRecord> records, GeoCsvLabels labels, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(labels);

        return Task.Run(
            () =>
            {
                using var stream = File.Create(filePath);
                GeoCsvWriter.Write(stream, records, labels);
            },
            cancellationToken);
    }

    /// <summary>
    /// <paramref name="fullPath"/> が、出力先 <paramref name="fullRoot"/> の中（出力先そのものは含まない）か。
    /// ドライブ直下（<c>D:\</c>）は末尾の区切り文字を保つので、区切り文字を二重にしない。名前が前方一致するだけの別のフォルダーは、中とみなさない
    /// </summary>
    internal static bool IsPathUnderRoot(string fullPath, string fullRoot)
    {
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fullPath.Length > prefix.Length;
    }

    private static async Task<CopyOutcome> CopyOneAsync(
        ExportPlanItem item,
        string destination,
        string fileName,
        Func<string, Task<ConflictResolution>>? resolveConflictAsync,
        ConflictResolution? stickyResolution)
    {
        var newSticky = (ConflictResolution?)null;
        if (File.Exists(destination))
        {
            var resolution = stickyResolution
                ?? (resolveConflictAsync is null ? ConflictResolution.Skip : await resolveConflictAsync(fileName).ConfigureAwait(true));
            switch (resolution)
            {
                case ConflictResolution.Cancel:
                    return new CopyOutcome(CopyOutcomeKind.Canceled, FileOperationError.Cancelled, null);
                case ConflictResolution.Skip:
                    return new CopyOutcome(CopyOutcomeKind.Skipped, FileOperationError.None, null);
                case ConflictResolution.SkipAll:
                    return new CopyOutcome(CopyOutcomeKind.Skipped, FileOperationError.None, ConflictResolution.SkipAll);
                case ConflictResolution.OverwriteAll:
                    newSticky = ConflictResolution.OverwriteAll;
                    break;
            }
        }

        try
        {
            // 1 つのファイルのコピーは、途中で止めない（出力先に、書きかけのファイルを残さない）
            await Task.Run(
                () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(item.SourcePath, destination, overwrite: true);
                }).ConfigureAwait(true);
            return new CopyOutcome(CopyOutcomeKind.Copied, FileOperationError.None, newSticky);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error($"Failed to export a photo (access denied): {item.SourcePath}", ex);
            return new CopyOutcome(CopyOutcomeKind.Failed, FileOperationError.Unauthorized, newSticky);
        }
        catch (IOException ex)
        {
            AppLog.Error($"Failed to export a photo: {item.SourcePath}", ex);
            return new CopyOutcome(CopyOutcomeKind.Failed, FileOperationError.IoError, newSticky);
        }
    }

    private enum CopyOutcomeKind
    {
        Copied,
        Skipped,
        Canceled,
        Failed
    }

    private readonly record struct CopyOutcome(CopyOutcomeKind Kind, FileOperationError Error, ConflictResolution? StickyResolution);
}
