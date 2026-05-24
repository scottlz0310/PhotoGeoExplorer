using System.Collections.Generic;

namespace PhotoGeoExplorer.Services;

internal sealed record FileOperationFailure(string Path, string FileName, FileOperationError Error);

internal sealed record FileOperationSummary(int SuccessCount, int SkipCount, IReadOnlyList<FileOperationFailure> Failures)
{
    public int FailureCount => Failures.Count;
    public bool HasFailures => Failures.Count > 0;
    public bool IsAllSuccess => Failures.Count == 0;
}
