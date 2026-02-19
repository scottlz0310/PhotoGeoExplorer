using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class HelpServiceTests : IDisposable
{
    private static readonly string[] EnglishFirstPreferredLanguages = ["en-GB", "ja-JP"];
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public void GetHelpHtmlFileNameReturnsEnglishFileWhenLanguageOverrideIsEnglish()
    {
        var result = HelpService.GetHelpHtmlFileName(
            languageOverride: "en-US",
            preferredLanguages: null,
            currentUiCultureName: "ja-JP");

        Assert.Equal("index.en.html", result);
    }

    [Fact]
    public void GetHelpHtmlFileNameReturnsDefaultFileWhenLanguageOverrideIsJapanese()
    {
        var result = HelpService.GetHelpHtmlFileName(
            languageOverride: "ja-JP",
            preferredLanguages: null,
            currentUiCultureName: "en-US");

        Assert.Equal("index.html", result);
    }

    [Fact]
    public void GetHelpHtmlFileNameUsesPreferredLanguagesWhenLanguageOverrideIsMissing()
    {
        var result = HelpService.GetHelpHtmlFileName(
            languageOverride: null,
            preferredLanguages: EnglishFirstPreferredLanguages,
            currentUiCultureName: "ja-JP");

        Assert.Equal("index.en.html", result);
    }

    [Fact]
    public void GetHelpHtmlFileNameUsesCurrentCultureWhenLanguageOverrideAndPreferredLanguagesAreMissing()
    {
        var result = HelpService.GetHelpHtmlFileName(
            languageOverride: null,
            preferredLanguages: Array.Empty<string>(),
            currentUiCultureName: "en-US");

        Assert.Equal("index.en.html", result);
    }

    [Fact]
    public void TryGetHelpHtmlUriReturnsPreferredFileWhenItExists()
    {
        var root = CreateTempDirectory();
        var helpDirectory = Path.Combine(root, "wwwroot", "help");
        Directory.CreateDirectory(helpDirectory);

        var preferredPath = Path.Combine(helpDirectory, "index.en.html");
        File.WriteAllText(preferredPath, "<html>english</html>");
        File.WriteAllText(Path.Combine(helpDirectory, "index.html"), "<html>default</html>");

        var uri = HelpService.TryGetHelpHtmlUri(
            baseDirectory: root,
            languageOverride: "en-US",
            preferredLanguages: null,
            currentUiCultureName: "ja-JP");

        Assert.NotNull(uri);
        Assert.Equal(NormalizePath(preferredPath), NormalizePath(uri!.LocalPath));
    }

    [Fact]
    public void TryGetHelpHtmlUriFallsBackToDefaultFileWhenPreferredFileDoesNotExist()
    {
        var root = CreateTempDirectory();
        var helpDirectory = Path.Combine(root, "wwwroot", "help");
        Directory.CreateDirectory(helpDirectory);

        var fallbackPath = Path.Combine(helpDirectory, "index.html");
        File.WriteAllText(fallbackPath, "<html>default</html>");

        var uri = HelpService.TryGetHelpHtmlUri(
            baseDirectory: root,
            languageOverride: "en-US",
            preferredLanguages: null,
            currentUiCultureName: "ja-JP");

        Assert.NotNull(uri);
        Assert.Equal(NormalizePath(fallbackPath), NormalizePath(uri!.LocalPath));
    }

    [Fact]
    public void TryGetHelpHtmlUriReturnsNullWhenHelpFilesAreMissing()
    {
        var root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "wwwroot", "help"));

        var uri = HelpService.TryGetHelpHtmlUri(
            baseDirectory: root,
            languageOverride: "ja-JP",
            preferredLanguages: null,
            currentUiCultureName: "ja-JP");

        Assert.Null(uri);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
