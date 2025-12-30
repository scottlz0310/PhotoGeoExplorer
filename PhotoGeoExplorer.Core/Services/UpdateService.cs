using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/scottlz0310/PhotoGeoExplorer/releases/latest";
    private static readonly Uri LatestReleaseUri = new(LatestReleaseUrl);
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(Version? currentVersion, CancellationToken cancellationToken)
    {
        var normalizedCurrent = NormalizeVersion(currentVersion);

        try
        {
            using var response = await Client.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement))
            {
                return new UpdateCheckResult(normalizedCurrent, null, false, null, null, "Release tag is missing.");
            }

            var tagName = tagElement.GetString();
            var latestVersion = TryParseVersion(tagName);
            if (latestVersion is null)
            {
                return new UpdateCheckResult(normalizedCurrent, null, false, null, null, $"Invalid release tag: {tagName}");
            }

            var normalizedLatest = NormalizeVersion(latestVersion);
            var isUpdateAvailable = normalizedLatest > normalizedCurrent;
            var downloadUrl = TryGetMsiDownloadUrl(document.RootElement);
            var releasePageUrl = TryGetString(document.RootElement, "html_url");

            return new UpdateCheckResult(
                normalizedCurrent,
                normalizedLatest,
                isUpdateAvailable,
                downloadUrl,
                releasePageUrl,
                null);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("Update check failed.", ex);
            return new UpdateCheckResult(normalizedCurrent, null, false, null, null, "Request failed.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Error("Update check timed out.", ex);
            return new UpdateCheckResult(normalizedCurrent, null, false, null, null, "Request timed out.");
        }
        catch (JsonException ex)
        {
            AppLog.Error("Failed to parse update response.", ex);
            return new UpdateCheckResult(normalizedCurrent, null, false, null, null, "Invalid response.");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PhotoGeoExplorer", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static Version NormalizeVersion(Version? version)
    {
        if (version is null)
        {
            return new Version(0, 0, 0, 0);
        }

        var build = Math.Max(version.Build, 0);
        var revision = Math.Max(version.Revision, 0);
        return new Version(version.Major, version.Minor, build, revision);
    }

    private static Version? TryParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    private static string? TryGetMsiDownloadUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = TryGetString(asset, "name");
            if (string.IsNullOrWhiteSpace(name)
                || !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = TryGetString(asset, "browser_download_url");
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                return downloadUrl;
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
