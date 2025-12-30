namespace PhotoGeoExplorer.Models;

internal sealed class UpdateCheckResult
{
    public UpdateCheckResult(
        Version currentVersion,
        Version? latestVersion,
        bool isUpdateAvailable,
        string? downloadUrl,
        string? releasePageUrl,
        string? errorMessage)
    {
        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        IsUpdateAvailable = isUpdateAvailable;
        DownloadUrl = downloadUrl;
        ReleasePageUrl = releasePageUrl;
        ErrorMessage = errorMessage;
    }

    public Version CurrentVersion { get; }
    public Version? LatestVersion { get; }
    public bool IsUpdateAvailable { get; }
    public string? DownloadUrl { get; }
    public string? ReleasePageUrl { get; }
    public string? ErrorMessage { get; }
    public bool IsSuccess => string.IsNullOrWhiteSpace(ErrorMessage);
}
