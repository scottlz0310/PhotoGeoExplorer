using System;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// <see cref="FileOperationSummary"/> の派生プロパティ（特に #159 で追加した
/// <see cref="FileOperationSummary.HasReportableFailures"/>）の判定を固定する。
/// </summary>
public class FileOperationSummaryTests
{
    [Fact]
    public void HasReportableFailures_NoFailures_IsFalse()
    {
        var summary = new FileOperationSummary(1, 0, Array.Empty<FileOperationFailure>());

        Assert.False(summary.HasFailures);
        Assert.False(summary.HasReportableFailures);
    }

    [Fact]
    public void HasReportableFailures_OnlyCancelled_IsFalse()
    {
        var summary = new FileOperationSummary(0, 0, new[]
        {
            new FileOperationFailure("/a", "a.jpg", FileOperationError.Cancelled),
        });

        Assert.True(summary.HasFailures);
        Assert.False(summary.HasReportableFailures);
    }

    [Theory]
    [InlineData(nameof(FileOperationError.Unauthorized))]
    [InlineData(nameof(FileOperationError.AlreadyExists))]
    [InlineData(nameof(FileOperationError.IoError))]
    [InlineData(nameof(FileOperationError.DescendantPath))]
    public void HasReportableFailures_NonCancelledFailure_IsTrue(string errorName)
    {
        var error = Enum.Parse<FileOperationError>(errorName);
        var summary = new FileOperationSummary(0, 0, new[]
        {
            new FileOperationFailure("/a", "a.jpg", error),
        });

        Assert.True(summary.HasReportableFailures);
    }

    [Fact]
    public void HasReportableFailures_MixedCancelledAndReal_IsTrue()
    {
        var summary = new FileOperationSummary(0, 0, new[]
        {
            new FileOperationFailure("/a", "a.jpg", FileOperationError.Cancelled),
            new FileOperationFailure("/b", "b.jpg", FileOperationError.Unauthorized),
        });

        Assert.True(summary.HasReportableFailures);
    }
}
