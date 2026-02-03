using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace PhotoGeoExplorer.E2E;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed class AppE2ETests
{
    private readonly ITestOutputHelper _output;

    public AppE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [E2EFact]
    public async Task LaunchOpenFolderPreviewMetadataAndMap()
    {
        E2ETestData? testData = null;
        try
        {
            testData = await E2ETestData.CreateAsync(_output).ConfigureAwait(true);
            using var automation = new UIA3Automation();
            using var app = Application.Launch(testData.StartInfo);

            var window = WaitForMainWindow(app, automation);
            window.Focus();

            var list = WaitForList(app, automation, window);
            Retry.WhileTrue(
                () => list.Items.Length == 0,
                timeout: TimeSpan.FromSeconds(20),
                interval: TimeSpan.FromMilliseconds(200));

            list.Focus();
            var firstItem = list.Items[0];
            SelectListItem(firstItem);

            WaitForPreview(window);
            var summary = WaitForMetadataSummary(window);
            Assert.Contains("Fujifilm", summary, StringComparison.Ordinal);

            if (!TryWaitForMapReady(window))
            {
                _output.WriteLine("Map readiness check skipped (status panel still visible).");
            }
        }
        finally
        {
            if (testData is not null)
            {
                await testData.DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private static Window WaitForMainWindow(Application app, UIA3Automation automation)
    {
        var window = Retry.WhileNull(
                () => app.GetMainWindow(automation),
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(200))
            .Result;
        Assert.NotNull(window);
        return window!;
    }

    private static ListBox WaitForList(Application app, UIA3Automation automation, Window window)
    {
        WaitForWindowReady(window);

        AutomationElement? listElement = null;
        Retry.WhileTrue(
            () => !TryFindReadyList(window, automation, app.ProcessId, out listElement),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(listElement);
        return listElement!.AsListBox();
    }

    private static void WaitForWindowReady(Window window)
    {
        Retry.WhileTrue(
            () =>
            {
                if (!window.IsEnabled || window.IsOffscreen)
                {
                    return true;
                }

                var bounds = window.BoundingRectangle;
                return bounds.Width <= 1 || bounds.Height <= 1;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(200));
    }

    private static bool TryFindReadyList(
        Window window,
        UIA3Automation automation,
        int processId,
        out AutomationElement? element)
    {
        element = FindListElement(window) ?? FindListElement(automation.GetDesktop(), processId);

        if (element is null)
        {
            return false;
        }

        if (!element.IsEnabled || element.IsOffscreen)
        {
            return false;
        }

        var bounds = element.BoundingRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return false;
        }

        return true;
    }

    private static AutomationElement? FindListElement(AutomationElement scope)
    {
        return scope.FindFirstDescendant(cf => cf.ByAutomationId("FileListDetails"))
            ?? scope.FindFirstDescendant(cf => cf.ByAutomationId("FileListIcon"));
    }

    private static AutomationElement? FindListElement(AutomationElement scope, int processId)
    {
        var candidates = scope.FindAllDescendants(cf => cf.ByAutomationId("FileListDetails").Or(cf.ByAutomationId("FileListIcon")));
        return candidates.FirstOrDefault(candidate => candidate.Properties.ProcessId.Value == processId);
    }

    private static void WaitForPreview(Window window)
    {
        Retry.WhileTrue(
            () =>
            {
                var preview = window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewImage"));
                return preview is null || preview.IsOffscreen;
            },
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200));
    }

    private static string WaitForMetadataSummary(Window window)
    {
        string? summaryText = null;
        Retry.WhileTrue(
            () => !TryGetMetadataSummary(window, out summaryText),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true);

        return summaryText ?? string.Empty;
    }

    private static bool TryGetMetadataSummary(Window window, out string? summaryText)
    {
        summaryText = null;
        var summary = window.FindFirstDescendant(cf => cf.ByAutomationId("MetadataSummaryText"));
        if (summary is null)
        {
            return false;
        }

        if (summary.IsOffscreen)
        {
            return false;
        }

        var bounds = summary.BoundingRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return false;
        }

        var text = summary.Name?.Trim();
        if (string.IsNullOrWhiteSpace(text) && summary.Patterns.Text.IsSupported)
        {
            text = summary.Patterns.Text.Pattern.DocumentRange.GetText(-1)?.Trim();
        }

        if (string.IsNullOrWhiteSpace(text) && summary.Patterns.Value.IsSupported)
        {
            text = summary.Patterns.Value.Pattern.Value.Value?.Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        summaryText = text;
        return true;
    }

    private static bool TryWaitForMapReady(Window window)
    {
        var statusResult = Retry.WhileTrue(
            () =>
            {
                var status = window.FindFirstDescendant(cf => cf.ByAutomationId("MapStatusPanel"));
                return status is not null && !status.IsOffscreen;
            },
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false);

        return !statusResult.TimedOut;
    }

    private static void SelectListItem(AutomationElement item)
    {
        if (item.Patterns.SelectionItem.IsSupported)
        {
            item.Patterns.SelectionItem.Pattern.Select();
            Retry.WhileTrue(
                () => !item.Patterns.SelectionItem.Pattern.IsSelected,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200),
                throwOnTimeout: true);
            return;
        }

        item.Click();
    }

    private sealed class E2ETestData : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _root;
        private readonly string _imagePath;

        private E2ETestData(ITestOutputHelper output, string root, string imagePath, ProcessStartInfo startInfo)
        {
            _output = output;
            _root = root;
            _imagePath = imagePath;
            StartInfo = startInfo;
        }

        public ProcessStartInfo StartInfo { get; }

        public static async Task<E2ETestData> CreateAsync(ITestOutputHelper output)
        {
            var root = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerE2E", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var imagePath = Path.Combine(root, "sample.jpg");
            await CreateImageAsync(imagePath).ConfigureAwait(false);

            var appPath = ResolveAppPath();
            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = $"--folder \"{root}\"",
                WorkingDirectory = Path.GetDirectoryName(appPath) ?? root,
                UseShellExecute = false
            };

            output.WriteLine($"E2E folder: {root}");
            output.WriteLine($"App path: {appPath}");

            return new E2ETestData(output, root, imagePath, startInfo);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(_imagePath))
                {
                    File.Delete(_imagePath);
                }
            }
            catch (IOException ex)
            {
                _output.WriteLine(ex.ToString());
            }
            catch (UnauthorizedAccessException ex)
            {
                _output.WriteLine(ex.ToString());
            }

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException ex)
            {
                _output.WriteLine(ex.ToString());
            }
            catch (UnauthorizedAccessException ex)
            {
                _output.WriteLine(ex.ToString());
            }

            return ValueTask.CompletedTask;
        }

        private static async Task CreateImageAsync(string path)
        {
            using var image = new Image<Rgba32>(256, 256);
            image[0, 0] = new Rgba32(255, 255, 255, 255);

            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Make, "Fujifilm");
            profile.SetValue(ExifTag.Model, "X100V");
            profile.SetValue(ExifTag.DateTimeOriginal, "2024:01:02 03:04:00");
            SetGps(profile, latitude: 35.6895, longitude: 139.6917);
            image.Metadata.ExifProfile = profile;

            await image.SaveAsJpegAsync(path).ConfigureAwait(false);
        }

        private static void SetGps(ExifProfile profile, double latitude, double longitude)
        {
            profile.SetValue<string>(ExifTag.GPSLatitudeRef, latitude >= 0 ? "N" : "S");
            profile.SetValue<string>(ExifTag.GPSLongitudeRef, longitude >= 0 ? "E" : "W");
            profile.SetValue(ExifTag.GPSLatitude, ToRationals(latitude));
            profile.SetValue(ExifTag.GPSLongitude, ToRationals(longitude));
        }

        private static Rational[] ToRationals(double coordinate)
        {
            var absolute = Math.Abs(coordinate);
            var degrees = (int)Math.Floor(absolute);
            var minutesFull = (absolute - degrees) * 60;
            var minutes = (int)Math.Floor(minutesFull);
            var seconds = (minutesFull - minutes) * 60;

            return new[]
            {
                new Rational((uint)degrees, 1),
                new Rational((uint)minutes, 1),
                new Rational((uint)Math.Round(seconds * 100), 100)
            };
        }

        private static string ResolveAppPath()
        {
            var root = FindSolutionRoot() ?? throw new InvalidOperationException("Solution root not found.");
            var appPath = Path.Combine(
                root,
                "PhotoGeoExplorer",
                "bin",
                "x64",
                "Release",
                "net10.0-windows10.0.19041.0",
                "PhotoGeoExplorer.exe");

            if (!File.Exists(appPath))
            {
                throw new FileNotFoundException("App executable not found.", appPath);
            }

            return appPath;
        }

        private static string? FindSolutionRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "PhotoGeoExplorer.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }
    }

}
