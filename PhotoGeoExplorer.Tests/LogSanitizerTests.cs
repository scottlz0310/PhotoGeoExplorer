using System;
using System.IO;
using PhotoGeoExplorer.Security;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class LogSanitizerTests
{
    private const string Token = "pk.eyJ1IjoiZXhhbXBsZSJ9.abc-DEF_123";

    [Theory]
    [InlineData(
        "GET https://api.mapbox.com/v4/mapbox.satellite/1/2/3.jpg90?access_token=" + Token,
        "GET https://api.mapbox.com/v4/mapbox.satellite/1/2/3.jpg90?access_token=***")]
    [InlineData(
        "https://example.test/tiles?access_token=" + Token + "&v=2 failed",
        "https://example.test/tiles?access_token=***&v=2 failed")]
    [InlineData(
        "Request to 'https://example.test/?ACCESS_TOKEN=" + Token + "' failed.",
        "Request to 'https://example.test/?ACCESS_TOKEN=***' failed.")]
    [InlineData(
        "first access_token=" + Token + " second access_token=" + Token,
        "first access_token=*** second access_token=***")]
    [InlineData("No secret here", "No secret here")]
    [InlineData("access_token=", "access_token=")]
    [InlineData("", "")]
    public void MaskSecretsReplacesAccessTokenValue(string input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.MaskSecrets(input));
    }

    [Fact]
    public void CrashReportMaskRemovesAccessToken()
    {
        var input = "System.Net.Http.HttpRequestException: 401 (https://api.mapbox.com/v4/mapbox.satellite/0/0/0.jpg90?access_token=" + Token + ")";

        var result = CrashReportService.MaskSensitiveData(input);

        Assert.DoesNotContain(Token, result, StringComparison.Ordinal);
        Assert.Contains("access_token=***", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashLogFileDoesNotContainAccessToken()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new CrashReportService(directory);
            var exception = new InvalidOperationException(
                "Tile fetch failed: https://api.mapbox.com/v4/mapbox.satellite/0/0/0.jpg90?access_token=" + Token);

            service.WriteCrashLog(exception);

            var content = service.GetLatestCrashLogContent();
            Assert.NotNull(content);
            Assert.DoesNotContain(Token, content, StringComparison.Ordinal);
            Assert.Contains("access_token=***", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
