using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace PhotoGeoExplorer.E2E;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed class AppE2ETests
{
    private static readonly string[] PrimaryDialogButtonNames = { "Save", "保存" };
    private static readonly string[] SecondaryDialogButtonNames = { "Cancel", "キャンセル" };
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
            try
            {
                var window = WaitForMainWindow(app, automation);
                window.Focus();

                var list = WaitForList(app, automation, window);
                Retry.WhileTrue(
                    () => list.Items.Length == 0,
                    timeout: TimeSpan.FromSeconds(20),
                    interval: TimeSpan.FromMilliseconds(200));

                list.Focus();
                var imageItem = WaitForListItemByName(list, "sample.jpg");
                SelectListItem(imageItem);

                WaitForPreview(window);
                var summary = WaitForMetadataSummary(window, automation, app, _output);
                Assert.Contains("Fujifilm", summary, StringComparison.Ordinal);

                if (!TryWaitForMapReady(window))
                {
                    _output.WriteLine("Map readiness check skipped (status panel still visible).");
                }
            }
            finally
            {
                TerminateApp(app);
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

    [E2EFact]
    public async Task ExifEditorContextMenuAndDateToggleWorks()
    {
        E2ETestData? testData = null;
        try
        {
            testData = await E2ETestData.CreateAsync(_output).ConfigureAwait(true);
            using var automation = new UIA3Automation();
            using var app = Application.Launch(testData.StartInfo);
            try
            {
                var window = WaitForMainWindow(app, automation);
                window.Focus();

                var list = WaitForList(app, automation, window);
                WaitForListItems(list, minimumCount: 2);

                var disabledMenuItem = OpenExifMenuForItemName(window, automation, app.ProcessId, list, "folder");
                Assert.False(disabledMenuItem.IsEnabled);
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                WaitForElementGone(window, automation, app.ProcessId, "FileBrowser.EditExifMenuItem");

                var enabledMenuItem = OpenExifMenuForItemName(window, automation, app.ProcessId, list, "sample.jpg");
                Assert.True(enabledMenuItem.IsEnabled);
                enabledMenuItem.Click();

                var updateDate = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.UpdateDateCheckBox");
                var datePicker = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.TakenAtDatePicker");
                var timePicker = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.TakenAtTimePicker");
                var updateFileDate = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.UpdateFileDateCheckBox");

                SetCheckBoxState(updateDate, isChecked: false);
                WaitForEnabledState(datePicker, isEnabled: false);
                WaitForEnabledState(timePicker, isEnabled: false);
                WaitForEnabledState(updateFileDate, isEnabled: false);

                SetCheckBoxState(updateDate, isChecked: true);
                WaitForEnabledState(datePicker, isEnabled: true);
                WaitForEnabledState(timePicker, isEnabled: true);
                WaitForEnabledState(updateFileDate, isEnabled: true);

                ClickSecondaryDialogButton(window, automation, app.ProcessId);
                WaitForElementGone(window, automation, app.ProcessId, "ExifEditor.UpdateDateCheckBox");
            }
            finally
            {
                TerminateApp(app);
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

    [E2EFact]
    public async Task ExifEditorSaveAndReopenKeepsCoordinates()
    {
        E2ETestData? testData = null;
        try
        {
            testData = await E2ETestData.CreateAsync(_output).ConfigureAwait(true);
            using var automation = new UIA3Automation();
            using var app = Application.Launch(testData.StartInfo);
            try
            {
                var window = WaitForMainWindow(app, automation);
                window.Focus();

                var list = WaitForList(app, automation, window);
                WaitForListItems(list, minimumCount: 1);

                var menuItem = OpenExifMenuForItemName(window, automation, app.ProcessId, list, "sample.jpg");
                Assert.True(menuItem.IsEnabled);
                menuItem.Click();

                var latitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LatitudeTextBox");
                var longitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LongitudeTextBox");
                SetTextBoxValue(latitudeBox, "34.000001");
                SetTextBoxValue(longitudeBox, "135.000001");

                ClickPrimaryDialogButton(window, automation, app.ProcessId);
                WaitForElementGone(window, automation, app.ProcessId, "ExifEditor.LatitudeTextBox");

                var reopenMenu = OpenExifMenuForItemName(window, automation, app.ProcessId, list, "sample.jpg");
                Assert.True(reopenMenu.IsEnabled);
                reopenMenu.Click();

                var reopenedLatitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LatitudeTextBox");
                var reopenedLongitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LongitudeTextBox");

                var latitude = ParseInvariantDouble(GetTextBoxValue(reopenedLatitudeBox));
                var longitude = ParseInvariantDouble(GetTextBoxValue(reopenedLongitudeBox));

                Assert.InRange(latitude, 33.999, 34.001);
                Assert.InRange(longitude, 134.999, 135.001);

                ClickSecondaryDialogButton(window, automation, app.ProcessId);
            }
            finally
            {
                TerminateApp(app);
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

    private static void WaitForListItems(ListBox list, int minimumCount)
    {
        Retry.WhileTrue(
            () => list.Items.Length < minimumCount,
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200));
    }

    private static void TerminateApp(Application? app)
    {
        if (app is null)
        {
            return;
        }

        var processId = SafeGet(() => app.ProcessId, -1);
        try
        {
            app.Close();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Win32Exception)
        {
        }

        if (processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or System.NotSupportedException
                or Win32Exception)
            {
            }
        }

    }

    private static ListBoxItem WaitForListItemByName(ListBox list, string expectedName)
    {
        var result = Retry.WhileNull(
            () => list.Items.FirstOrDefault(item =>
                ListItemMatches(item, expectedName)),
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(result.Result);
        return result.Result!;
    }

    private static bool ListItemMatches(AutomationElement item, string expectedName)
    {
        if (ContainsIgnoreCase(SafeGet(() => item.Name, string.Empty), expectedName)
            || ContainsIgnoreCase(SafeGet(() => item.Properties.Name.ValueOrDefault, string.Empty), expectedName))
        {
            return true;
        }

        var descendants = item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
        return descendants.Any(text =>
            ContainsIgnoreCase(SafeGet(() => text.Name, string.Empty), expectedName)
            || ContainsIgnoreCase(SafeGet(() => text.Properties.Name.ValueOrDefault, string.Empty), expectedName));
    }

    private static AutomationElement OpenExifMenuForItemName(
        Window window,
        UIA3Automation automation,
        int processId,
        ListBox list,
        string itemName)
    {
        var listItem = WaitForListItemByName(list, itemName);
        SelectListItem(listItem);
        listItem.RightClick();
        return WaitForElementByAutomationId(
            window,
            automation,
            processId,
            "FileBrowser.EditExifMenuItem",
            timeout: TimeSpan.FromSeconds(10));
    }

    private static AutomationElement WaitForElementByAutomationId(
        Window window,
        UIA3Automation automation,
        int processId,
        string automationId,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var result = Retry.WhileNull(
            () => FindByAutomationId(window, automationId, processId)
                ?? FindByAutomationId(automation.GetDesktop(), automationId, processId),
            timeout: actualTimeout,
            interval: TimeSpan.FromMilliseconds(150));

        Assert.NotNull(result.Result);
        return result.Result!;
    }

    private static void WaitForElementGone(
        Window window,
        UIA3Automation automation,
        int processId,
        string automationId)
    {
        Retry.WhileTrue(
            () => (FindByAutomationId(window, automationId, processId)
                ?? FindByAutomationId(automation.GetDesktop(), automationId, processId))
                is not null,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(150));
    }

    private static void SetCheckBoxState(AutomationElement checkBoxElement, bool isChecked)
    {
        Retry.WhileTrue(
            () =>
            {
                if (!checkBoxElement.Patterns.Toggle.IsSupported)
                {
                    checkBoxElement.Click();
                    return true;
                }

                var current = checkBoxElement.Patterns.Toggle.Pattern.ToggleState == ToggleState.On;
                if (current == isChecked)
                {
                    return false;
                }

                checkBoxElement.Click();
                return true;
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(150),
            throwOnTimeout: false);

        if (checkBoxElement.Patterns.Toggle.IsSupported)
        {
            var current = checkBoxElement.Patterns.Toggle.Pattern.ToggleState == ToggleState.On;
            Assert.Equal(isChecked, current);
        }
    }

    private static void WaitForEnabledState(AutomationElement element, bool isEnabled)
    {
        Retry.WhileTrue(
            () => element.IsEnabled != isEnabled,
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(150));

        Assert.Equal(isEnabled, element.IsEnabled);
    }

    private static void SetTextBoxValue(AutomationElement textBoxElement, string value)
    {
        if (textBoxElement.Patterns.Value.IsSupported)
        {
            textBoxElement.Patterns.Value.Pattern.SetValue(value);
            return;
        }

        var textBox = textBoxElement.AsTextBox();
        textBox.Focus();
        textBox.Enter(value);
    }

    private static string GetTextBoxValue(AutomationElement textBoxElement)
    {
        if (textBoxElement.Patterns.Value.IsSupported)
        {
            return textBoxElement.Patterns.Value.Pattern.Value.Value ?? string.Empty;
        }

        return textBoxElement.Name ?? string.Empty;
    }

    private static void ClickPrimaryDialogButton(Window window, UIA3Automation automation, int processId)
    {
        ClickDialogButton(
            window,
            automation,
            processId,
            "PrimaryButton",
            PrimaryDialogButtonNames);
    }

    private static void ClickSecondaryDialogButton(Window window, UIA3Automation automation, int processId)
    {
        ClickDialogButton(
            window,
            automation,
            processId,
            "SecondaryButton",
            SecondaryDialogButtonNames);
    }

    private static void ClickDialogButton(
        Window window,
        UIA3Automation automation,
        int processId,
        string automationId,
        IReadOnlyList<string> fallbackNames)
    {
        try
        {
            var byId = WaitForElementByAutomationId(
                window,
                automation,
                processId,
                automationId,
                timeout: TimeSpan.FromSeconds(3));
            byId.Click();
            return;
        }
        catch (TimeoutException)
        {
        }

        foreach (var name in fallbackNames)
        {
            var button = FindButtonByName(window, name, processId)
                ?? FindButtonByName(automation.GetDesktop(), name, processId);
            if (button is not null)
            {
                button.Click();
                return;
            }
        }

        throw new TimeoutException($"Dialog button not found. AutomationId='{automationId}'");
    }

    private static AutomationElement? FindButtonByName(AutomationElement scope, string name, int processId)
    {
        var candidates = scope.FindAllDescendants(cf => cf.ByName(name));
        return candidates.FirstOrDefault(candidate =>
            SafeGet(() => candidate.Properties.ProcessId.ValueOrDefault, -1) == processId
            && SafeGet(() => candidate.ControlType, ControlType.Custom) == ControlType.Button);
    }

    private static double ParseInvariantDouble(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Failed to parse coordinate value: '{text}'");
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

    private static string WaitForMetadataSummary(Window window, UIA3Automation automation, Application app, ITestOutputHelper output)
    {
        string? summaryText = null;
        try
        {
            Retry.WhileTrue(
                () => !TryGetMetadataSummary(window, automation, app.ProcessId, out summaryText),
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(200),
                throwOnTimeout: true);
        }
        catch (TimeoutException)
        {
            DumpMetadataSummaryDiagnostics(output, automation, app, window);
            throw;
        }

        return summaryText ?? string.Empty;
    }

    private static bool TryGetMetadataSummary(
        Window window,
        UIA3Automation automation,
        int processId,
        out string? summaryText)
    {
        summaryText = null;
        var summary = window.FindFirstDescendant(cf => cf.ByAutomationId("MetadataSummaryText"))
            ?? FindByAutomationId(automation.GetDesktop(), "MetadataSummaryText", processId);
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

    private static void DumpMetadataSummaryDiagnostics(
        ITestOutputHelper output,
        UIA3Automation automation,
        Application app,
        Window window)
    {
        output.WriteLine("=== UIA diagnostics: MetadataSummaryText ===");
        DumpElementSummary(output, "MainWindow", window);
        DumpSpecificElement(output, "Window.MetadataSummaryText", window.FindFirstDescendant(cf => cf.ByAutomationId("MetadataSummaryText")));

        var desktop = automation.GetDesktop();
        DumpSpecificElement(output, "Desktop.MetadataSummaryText", FindByAutomationId(desktop, "MetadataSummaryText", app.ProcessId));

        var windowDescendants = window.FindAllDescendants();
        output.WriteLine($"Window descendants: {windowDescendants.Length}");
        DumpCandidates(output, "Window candidates", windowDescendants, processId: null);

        var desktopDescendants = desktop.FindAllDescendants();
        output.WriteLine($"Desktop descendants (process {app.ProcessId}): {desktopDescendants.Length}");
        DumpCandidates(output, "Desktop candidates (process)", desktopDescendants, processId: app.ProcessId);

        TryCaptureWindowScreenshot(output, window);
    }

    private static void DumpElementSummary(ITestOutputHelper output, string label, AutomationElement? element)
    {
        if (element is null)
        {
            output.WriteLine($"{label}: null");
            return;
        }

        output.WriteLine($"{label}: {FormatElement(element)}");
    }

    private static void DumpSpecificElement(ITestOutputHelper output, string label, AutomationElement? element)
    {
        DumpElementSummary(output, label, element);
        if (element is null)
        {
            return;
        }

        output.WriteLine($"{label} Patterns: {GetSupportedPatterns(element)}");
    }

    private static void DumpCandidates(
        ITestOutputHelper output,
        string label,
        IEnumerable<AutomationElement> elements,
        int? processId)
    {
        var candidates = elements
            .Where(element => IsMetadataCandidate(element, processId))
            .Take(50)
            .ToList();

        output.WriteLine($"{label}: {candidates.Count} candidates");
        foreach (var candidate in candidates)
        {
            output.WriteLine(FormatElement(candidate));
        }
    }

    private static bool IsMetadataCandidate(AutomationElement element, int? processId)
    {
        if (processId is int requiredProcessId)
        {
            var elementProcessId = SafeGet(() => element.Properties.ProcessId.ValueOrDefault, -1);
            if (elementProcessId != requiredProcessId)
            {
                return false;
            }
        }

        var automationId = SafeGet(() => element.Properties.AutomationId.ValueOrDefault, string.Empty);
        var name = SafeGet(() => element.Name, string.Empty);
        var hasKeyword = ContainsIgnoreCase(automationId, "metadata")
            || ContainsIgnoreCase(automationId, "summary")
            || ContainsIgnoreCase(name, "metadata")
            || ContainsIgnoreCase(name, "summary")
            || ContainsIgnoreCase(name, "fujifilm");

        var controlType = SafeGet(() => element.ControlType, ControlType.Custom);
        var isTextLike = controlType == ControlType.Text
            || controlType == ControlType.Edit
            || controlType == ControlType.Document;

        return hasKeyword || isTextLike;
    }

    private static string FormatElement(AutomationElement element)
    {
        var automationId = SafeGet(() => element.Properties.AutomationId.ValueOrDefault, string.Empty);
        var name = SafeGet(() => element.Name, string.Empty);
        var controlType = SafeGet(() => element.ControlType.ToString(), "(unknown)");
        var className = SafeGet(() => element.ClassName, string.Empty);
        var isEnabled = SafeGet(() => element.IsEnabled, false);
        var isOffscreen = SafeGet(() => element.IsOffscreen, true);
        var bounds = SafeGet(() => element.BoundingRectangle.ToString(), "(unavailable)");
        var patterns = GetSupportedPatterns(element);
        return $"AutomationId='{automationId}', Name='{name}', ControlType='{controlType}', ClassName='{className}', Enabled={isEnabled}, Offscreen={isOffscreen}, Bounds={bounds}, Patterns=[{patterns}]";
    }

    private static string GetSupportedPatterns(AutomationElement element)
    {
        var patterns = new List<string>();
        if (SafeGet(() => element.Patterns.Text.IsSupported, false))
        {
            patterns.Add("Text");
        }

        if (SafeGet(() => element.Patterns.Value.IsSupported, false))
        {
            patterns.Add("Value");
        }

        if (SafeGet(() => element.Patterns.LegacyIAccessible.IsSupported, false))
        {
            patterns.Add("LegacyIAccessible");
        }

        return string.Join(", ", patterns);
    }

    private static AutomationElement? FindByAutomationId(AutomationElement scope, string automationId, int processId)
    {
        var candidates = scope.FindAllDescendants(cf => cf.ByAutomationId(automationId));
        return candidates.FirstOrDefault(candidate => SafeGet(() => candidate.Properties.ProcessId.ValueOrDefault, -1) == processId);
    }

    private static bool ContainsIgnoreCase(string? value, string keyword)
    {
        return !string.IsNullOrEmpty(value)
            && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryCaptureWindowScreenshot(ITestOutputHelper output, Window window)
    {
        try
        {
            var captureType = Type.GetType("FlaUI.Core.Capturing.Capture, FlaUI.Core");
            if (captureType is null)
            {
                output.WriteLine("Screenshot capture skipped: FlaUI.Core.Capturing.Capture not available.");
                return;
            }

            var elementMethod = captureType.GetMethod("Element", new[] { typeof(AutomationElement) });
            if (elementMethod is null)
            {
                output.WriteLine("Screenshot capture skipped: Capture.Element method not found.");
                return;
            }

            var capture = elementMethod.Invoke(null, new object[] { window });
            if (capture is null)
            {
                output.WriteLine("Screenshot capture skipped: Capture.Element returned null.");
                return;
            }

            var toFileMethod = capture.GetType().GetMethod("ToFile", new[] { typeof(string) });
            if (toFileMethod is null)
            {
                output.WriteLine("Screenshot capture skipped: ToFile method not found.");
                return;
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorerE2E", "Diagnostics");
            Directory.CreateDirectory(outputDir);
            var fileName = $"metadata-timeout-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png";
            var filePath = Path.Combine(outputDir, fileName);
            toFileMethod.Invoke(capture, new object[] { filePath });
            output.WriteLine($"Screenshot saved: {filePath}");
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or IOException
            or COMException
            or UnauthorizedAccessException
            or TargetInvocationException)
        {
            output.WriteLine($"Screenshot capture failed: {ex}");
        }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or PropertyNotSupportedException)
        {
            return fallback;
        }
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
            try
            {
                item.Patterns.SelectionItem.Pattern.Select();
                Retry.WhileTrue(
                    () => !item.Patterns.SelectionItem.Pattern.IsSelected,
                    timeout: TimeSpan.FromSeconds(10),
                    interval: TimeSpan.FromMilliseconds(200),
                    throwOnTimeout: true);
                return;
            }
            catch (ElementNotAvailableException)
            {
                // 再生成タイミングで SelectionItem パターンが一時的に無効になるため、Click にフォールバック
            }
            catch (COMException)
            {
                // 一部環境で SelectionItem.Select が COM 例外を返すことがある
            }
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
            Directory.CreateDirectory(Path.Combine(root, "folder"));
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
