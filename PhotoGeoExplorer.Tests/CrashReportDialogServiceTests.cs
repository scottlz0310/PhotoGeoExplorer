using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class CrashReportDialogServiceTests
{
    [Theory]
    [InlineData("App Version: 1.8.5\nTimestamp: 2024-01-01\nException Type: System.NullReferenceException", "Exception Type:", "System.NullReferenceException")]
    [InlineData("App Version: 1.8.5\nTimestamp: 2024-01-01", "Exception Type:", null)]
    [InlineData("", "Exception Type:", null)]
    [InlineData(null, "Exception Type:", null)]
    public void ParseCrashLogFieldExtractsFieldValue(string? logContent, string fieldName, string? expected)
    {
        var result = CrashReportDialogService.ParseCrashLogField(logContent, fieldName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildCrashReportGitHubIssueUrlIncludesExceptionTypeInTitle()
    {
        var logContent = "App Version: 1.8.5\nException Type: System.NullReferenceException";

        var url = CrashReportDialogService.BuildCrashReportGitHubIssueUrl(logContent);

        Assert.StartsWith("https://github.com/scottlz0310/PhotoGeoExplorer/issues/new", url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("System.NullReferenceException"), url, StringComparison.Ordinal);
        Assert.Contains("labels=bug", url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCrashReportGitHubIssueUrlUsesUnknownWhenExceptionTypeMissing()
    {
        var url = CrashReportDialogService.BuildCrashReportGitHubIssueUrl(null);

        Assert.Contains(Uri.EscapeDataString("[Problem] Unknown"), url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCrashReportGitHubIssueUrlTruncatesLongLogContent()
    {
        var logContent = new string('a', 3000);

        var url = CrashReportDialogService.BuildCrashReportGitHubIssueUrl(logContent);

        Assert.Contains(Uri.EscapeDataString("...(truncated)"), url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCrashReportMailtoUriUsesMailtoScheme()
    {
        var logContent = "App Version: 1.8.5\nException Type: System.NullReferenceException";

        var uri = CrashReportDialogService.BuildCrashReportMailtoUri(logContent);

        Assert.Equal("mailto", uri.Scheme);
        Assert.StartsWith("mailto:photogeoexplorer@outlook.com", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("1.8.5"), uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("System.NullReferenceException"), uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCrashReportMailtoUriUsesUnknownWhenLogContentMissing()
    {
        var uri = CrashReportDialogService.BuildCrashReportMailtoUri(null);

        Assert.Contains(Uri.EscapeDataString("Unknown"), uri.OriginalString, StringComparison.Ordinal);
    }
}
