using System.Collections.Generic;
using System.Linq;

namespace PhotoGeoExplorer.Services;

internal sealed record FileOperationFailure(string Path, string FileName, FileOperationError Error);

internal sealed record FileOperationSummary(int SuccessCount, int SkipCount, IReadOnlyList<FileOperationFailure> Failures)
{
    public int FailureCount => Failures.Count;
    public bool HasFailures => Failures.Count > 0;

    /// <summary>
    /// ユーザーへエラーダイアログで通知すべき失敗（キャンセルを除く）が存在するか。
    /// 競合ダイアログでの取り消しや CancellationToken によるキャンセルはエラー通知の対象外とする。
    /// </summary>
    public bool HasReportableFailures => Failures.Any(f => f.Error != FileOperationError.Cancelled);

    public bool IsAllSuccess => Failures.Count == 0;
}
