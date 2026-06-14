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
    private const string EditExifMenuAutomationId = "FileBrowser.EditExifMenuItem";
    private const string ConfirmationMessageAutomationId = "FileBrowser.ConfirmationMessage";
    private static readonly string[] SingleFileSelection = { "sample.jpg" };
    private static readonly string[] SingleFolderSelection = { "folder" };
    private static readonly string[] MultipleSelection = { "sample.jpg", "folder" };
    private static readonly string[] FileListAutomationIds = { "FileListDetails", "FileListIcon", "FileListList" };
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

                var list = WaitForList(app, automation, window, _output);
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

                Assert.True(
                    TryWaitForMapReady(window),
                    "Map readiness check failed: MapStatusPanel is still visible after selecting sample.jpg.");
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

                var list = WaitForList(app, automation, window, _output);
                WaitForListItems(list, minimumCount: 2);

                var disabledMenuItem = OpenContextMenuItemForItemName(window, automation, app.ProcessId, list, "folder", EditExifMenuAutomationId);
                Assert.False(disabledMenuItem.IsEnabled);
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                WaitForElementGone(window, automation, app.ProcessId, EditExifMenuAutomationId);

                var enabledMenuItem = OpenContextMenuItemForItemName(window, automation, app.ProcessId, list, "sample.jpg", EditExifMenuAutomationId);
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

                var list = WaitForList(app, automation, window, _output);
                WaitForListItems(list, minimumCount: 2);

                try
                {
                    var warmupMenu = OpenContextMenuItemForItemName(window, automation, app.ProcessId, list, "folder", EditExifMenuAutomationId);
                    _output.WriteLine($"Warmup menu state for 'folder': IsEnabled={warmupMenu.IsEnabled}");
                    Keyboard.Type(VirtualKeyShort.ESCAPE);
                    WaitForElementGone(window, automation, app.ProcessId, EditExifMenuAutomationId);
                }
                catch (TimeoutException)
                {
                    _output.WriteLine("Warmup for 'folder' was skipped because the menu item was not found.");
                }

                var menuItem = OpenContextMenuItemForItemName(window, automation, app.ProcessId, list, "sample.jpg", EditExifMenuAutomationId);
                Assert.True(menuItem.IsEnabled);
                menuItem.Click();

                var latitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LatitudeTextBox");
                var longitudeBox = WaitForElementByAutomationId(window, automation, app.ProcessId, "ExifEditor.LongitudeTextBox");
                SetTextBoxValue(latitudeBox, "34.000001");
                SetTextBoxValue(longitudeBox, "135.000001");

                ClickPrimaryDialogButton(window, automation, app.ProcessId);
                WaitForElementGone(window, automation, app.ProcessId, "ExifEditor.LatitudeTextBox");

                var reopenMenu = OpenContextMenuItemForItemName(window, automation, app.ProcessId, list, "sample.jpg", EditExifMenuAutomationId);
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

    [E2EFact]
    public async Task DeleteConfirmationMessageVariesBySelection()
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

                var list = WaitForList(app, automation, window, _output);
                WaitForListItems(list, minimumCount: 2);

                // 単一ファイル / 単一フォルダ / 複数選択で確認文面を取得する。
                // 文面の正確なローカライズは単体テスト（BuildDeleteConfirmationMessage）が担保するため、
                // E2E は「選択に応じて確認文面が実機で分岐する」ことを検証する。テストランナーの
                // ロケール／リソース解決状況（未パッケージ起動・en ランナーでは未解決でリソースキーが
                // 返ることがある）に依存しないよう、3 文面が互いに異なり非空であることを確認する。
                var fileMessage = OpenDeleteConfirmationForSelection(window, automation, app.ProcessId, list, SingleFileSelection);
                _output.WriteLine($"[delete-confirm] single-file message='{fileMessage}'");
                CancelDeleteConfirmation(window, automation, app.ProcessId);

                var folderMessage = OpenDeleteConfirmationForSelection(window, automation, app.ProcessId, list, SingleFolderSelection);
                _output.WriteLine($"[delete-confirm] single-folder message='{folderMessage}'");
                CancelDeleteConfirmation(window, automation, app.ProcessId);

                var multipleMessage = OpenDeleteConfirmationForSelection(window, automation, app.ProcessId, list, MultipleSelection);
                _output.WriteLine($"[delete-confirm] multiple message='{multipleMessage}'");
                CancelDeleteConfirmation(window, automation, app.ProcessId);

                Assert.NotEmpty(fileMessage);
                Assert.NotEmpty(folderMessage);
                Assert.NotEmpty(multipleMessage);
                Assert.NotEqual(fileMessage, folderMessage);
                Assert.NotEqual(fileMessage, multipleMessage);
                Assert.NotEqual(folderMessage, multipleMessage);
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

    // 指定した項目を選択し、Delete キーで削除確認ダイアログを開いて文面を返す。
    // Delete キーは選択を変えないため複数選択シナリオでも選択を維持できる。
    private static string OpenDeleteConfirmationForSelection(
        Window window,
        UIA3Automation automation,
        int processId,
        ListBox list,
        string[] itemNames)
    {
        SelectListItemsByName(list, itemNames);

        // ダイアログ連続開閉の直後はリストのフォーカスが不安定で Delete が届かないことがあるため、
        // 確認ダイアログが現れるまで list へのフォーカスと Delete 送出をリトライする。
        AutomationElement? message = null;
        Retry.WhileNull(
            () =>
            {
                list.Focus();
                Keyboard.Type(VirtualKeyShort.DELETE);
                message = TryWaitForElementByAutomationId(
                    window, automation, processId, ConfirmationMessageAutomationId, TimeSpan.FromSeconds(3));
                return message;
            },
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200),
            ignoreException: true,
            throwOnTimeout: false);

        if (message is null)
        {
            throw new TimeoutException(
                $"Delete confirmation dialog did not appear for [{string.Join(",", itemNames)}].");
        }

        // 要素出現直後は TextBlock のテキストが未反映のことがあるため、文面が非空になるまで待つ。
        var text = string.Empty;
        Retry.WhileTrue(
            () =>
            {
                text = GetElementText(message);
                return string.IsNullOrWhiteSpace(text);
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(150),
            throwOnTimeout: false);
        return text;
    }

    private static void CancelDeleteConfirmation(Window window, UIA3Automation automation, int processId)
    {
        ClickSecondaryDialogButton(window, automation, processId);
        WaitForElementGone(window, automation, processId, ConfirmationMessageAutomationId);
    }

    private static void SelectListItemsByName(ListBox list, string[] itemNames)
    {
        for (var i = 0; i < itemNames.Length; i++)
        {
            var item = WaitForListItemByName(list, itemNames[i]);
            TryScrollIntoView(item);
            if (i == 0)
            {
                SelectListItem(item);
            }
            else
            {
                AddToSelection(item);
            }
        }
    }

    private static void AddToSelection(AutomationElement item)
    {
        if (!item.Patterns.SelectionItem.IsSupported)
        {
            return;
        }

        try
        {
            item.Patterns.SelectionItem.Pattern.AddToSelection();
            Retry.WhileTrue(
                () => !item.Patterns.SelectionItem.Pattern.IsSelected,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(200),
                throwOnTimeout: false);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }
    }

    private static string GetElementText(AutomationElement element)
    {
        var text = SafeGet(() => element.Name, string.Empty);
        if (string.IsNullOrWhiteSpace(text) && element.Patterns.Text.IsSupported)
        {
            text = SafeGet(() => element.Patterns.Text.Pattern.DocumentRange.GetText(-1), string.Empty);
        }

        if (string.IsNullOrWhiteSpace(text) && element.Patterns.Value.IsSupported)
        {
            text = SafeGet(() => element.Patterns.Value.Pattern.Value.Value, string.Empty);
        }

        return text?.Trim() ?? string.Empty;
    }

    private static void WaitForListItems(ListBox list, int minimumCount)
    {
        Retry.WhileTrue(
            () => list.Items.Length < minimumCount,
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200));

        // 先頭アイテムだけでなく minimumCount 件すべての描画完了を待つ（CI ランナーの描画遅延対策）
        Retry.WhileTrue(
            () =>
            {
                var items = list.Items;
                if (items.Length < minimumCount)
                {
                    return true;
                }

                return items.Take(minimumCount).Any(item =>
                {
                    var width = SafeGet(() => item.BoundingRectangle.Width, 0d);
                    var height = SafeGet(() => item.BoundingRectangle.Height, 0d);
                    return width <= 1 || height <= 1;
                });
            },
            timeout: TimeSpan.FromSeconds(8),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: false);
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

    private static AutomationElement OpenContextMenuItemForItemName(
        Window window,
        UIA3Automation automation,
        int processId,
        ListBox list,
        string itemName,
        string menuItemAutomationId)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var listItem = WaitForListItemByName(list, itemName);
                TryFocusElement(window);
                SelectListItem(listItem);
                TryFocusElement(listItem);

                // スクロールして可視領域に入れ、描画完了を待つ
                TryScrollIntoView(listItem);
                WaitForElementClickable(listItem);
                ClickElementCenter(listItem);

                Keyboard.Type(VirtualKeyShort.APPS);
                var byAppsKey = TryWaitForMenuItemById(window, automation, processId, menuItemAutomationId, TimeSpan.FromSeconds(5));
                if (byAppsKey is not null)
                {
                    return byAppsKey;
                }

                // listItem.RightClick() は NoClickablePointException を投げる可能性があるため
                // 安全な RightClickElementCenter を使用する
                RightClickElementCenter(listItem);
                var byRightClick = TryWaitForMenuItemById(window, automation, processId, menuItemAutomationId, TimeSpan.FromSeconds(5));
                if (byRightClick is not null)
                {
                    return byRightClick;
                }

                RightClickElementCenter(listItem);
                var byMouseRightClick = TryWaitForMenuItemById(window, automation, processId, menuItemAutomationId, TimeSpan.FromSeconds(5));
                if (byMouseRightClick is not null)
                {
                    return byMouseRightClick;
                }

                list.RightClick();
                var byListRightClick = TryWaitForMenuItemById(window, automation, processId, menuItemAutomationId, TimeSpan.FromSeconds(4));
                if (byListRightClick is not null)
                {
                    return byListRightClick;
                }

                Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F10);
                var byShiftF10 = TryWaitForMenuItemById(window, automation, processId, menuItemAutomationId, TimeSpan.FromSeconds(4));
                if (byShiftF10 is not null)
                {
                    return byShiftF10;
                }
            }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException or NoClickablePointException)
            {
            }

            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }

        throw new TimeoutException(
            $"Context menu item '{menuItemAutomationId}' not found for '{itemName}'. List snapshot: {BuildListSnapshot(list)}");
    }

    private static AutomationElement? TryWaitForMenuItemById(
        Window window,
        UIA3Automation automation,
        int processId,
        string menuItemAutomationId,
        TimeSpan timeout)
    {
        return Retry.WhileNull(
            () => FindByAutomationId(window, menuItemAutomationId, processId)
                ?? FindByAutomationId(automation.GetDesktop(), menuItemAutomationId, processId)
                ?? window.FindFirstDescendant(cf => cf.ByAutomationId(menuItemAutomationId))
                ?? automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId(menuItemAutomationId)),
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(150),
            ignoreException: true,
            throwOnTimeout: false).Result;
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
            {
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException or NoClickablePointException)
        {
        }
    }

    private static void WaitForElementClickable(AutomationElement element)
    {
        Retry.WhileTrue(
            () =>
            {
                if (SafeGet(() => element.IsOffscreen, false))
                {
                    return true;
                }

                var width = SafeGet(() => element.BoundingRectangle.Width, 0d);
                var height = SafeGet(() => element.BoundingRectangle.Height, 0d);
                return width <= 1 || height <= 1;
            },
            timeout: TimeSpan.FromSeconds(8),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: false);
    }

    private static void RightClickElementCenter(AutomationElement element)
    {
        var left = SafeGet(() => element.BoundingRectangle.Left, 0d);
        var top = SafeGet(() => element.BoundingRectangle.Top, 0d);
        var width = SafeGet(() => element.BoundingRectangle.Width, 0d);
        var height = SafeGet(() => element.BoundingRectangle.Height, 0d);
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var centerX = (int)Math.Round(left + (width / 2d), MidpointRounding.AwayFromZero);
        var centerY = (int)Math.Round(top + (height / 2d), MidpointRounding.AwayFromZero);
        Mouse.RightClick(new System.Drawing.Point(centerX, centerY));
    }

    private static void ClickElementCenter(AutomationElement element)
    {
        var left = SafeGet(() => element.BoundingRectangle.Left, 0d);
        var top = SafeGet(() => element.BoundingRectangle.Top, 0d);
        var width = SafeGet(() => element.BoundingRectangle.Width, 0d);
        var height = SafeGet(() => element.BoundingRectangle.Height, 0d);
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var centerX = (int)Math.Round(left + (width / 2d), MidpointRounding.AwayFromZero);
        var centerY = (int)Math.Round(top + (height / 2d), MidpointRounding.AwayFromZero);
        Mouse.LeftClick(new System.Drawing.Point(centerX, centerY));
    }

    private static void TryFocusElement(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ElementNotAvailableException)
        {
        }
    }

    private static string BuildListSnapshot(ListBox list)
    {
        try
        {
            var names = list.Items
                .Select(DescribeListItem)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(8)
                .ToArray();
            return names.Length == 0 ? "<empty>" : string.Join(", ", names);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Win32Exception or TimeoutException)
        {
            return $"<snapshot-error:{ex.GetType().Name}>";
        }
    }

    private static string DescribeListItem(AutomationElement item)
    {
        var primaryName = SafeGet(() => item.Name, string.Empty);
        var textParts = SafeGet(
            () => item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Select(text => SafeGet(() => text.Name, string.Empty))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray(),
            Array.Empty<string>());

        if (textParts.Length > 0)
        {
            return string.Join("|", textParts);
        }

        return primaryName;
    }

    private static AutomationElement WaitForElementByAutomationId(
        Window window,
        UIA3Automation automation,
        int processId,
        string automationId,
        TimeSpan? timeout = null)
    {
        var result = TryWaitForElementByAutomationId(window, automation, processId, automationId, timeout);
        if (result is null)
        {
            throw new TimeoutException($"Element with AutomationId '{automationId}' was not found within the timeout.");
        }

        return result;
    }

    private static AutomationElement? TryWaitForElementByAutomationId(
        Window window,
        UIA3Automation automation,
        int processId,
        string automationId,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(20);
        return Retry.WhileNull(
            () => FindByAutomationId(window, automationId, processId)
                ?? FindByAutomationId(automation.GetDesktop(), automationId, processId),
            timeout: actualTimeout,
            interval: TimeSpan.FromMilliseconds(150),
            ignoreException: true,
            throwOnTimeout: false).Result;
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
        var result = Retry.WhileNull(
            () => TryGetMainWindow(app, automation),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true);

        if (result.TimedOut || result.Result is null)
        {
            throw new TimeoutException("Main window was not found within the timeout.");
        }

        return result.Result;
    }

    private static Window? TryGetMainWindow(Application app, UIA3Automation automation)
    {
        try
        {
            var byMainHandle = app.GetMainWindow(automation);
            if (byMainHandle is not null)
            {
                return byMainHandle;
            }
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or TimeoutException)
        {
        }

        var processId = SafeGet(() => app.ProcessId, -1);
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            var desktopWindow = automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(window =>
                    SafeGet(() => window.Properties.ProcessId.ValueOrDefault, -1) == processId);
            return desktopWindow?.AsWindow();
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or TimeoutException)
        {
            return null;
        }
    }

    private static ListBox WaitForList(Application app, UIA3Automation automation, Window window, ITestOutputHelper output)
    {
        WaitForWindowReady(window);

        AutomationElement? listElement = null;
        var waitResult = Retry.WhileTrue(
            () => !TryFindReadyList(window, automation, app.ProcessId, out listElement),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false);
        if (waitResult.TimedOut || listElement is null)
        {
            try
            {
                DumpListDiagnostics(output, automation, app, window);
            }
            catch (Exception ex) when (ex is COMException
                or InvalidOperationException
                or TimeoutException
                or Win32Exception
                or ElementNotAvailableException
                or PropertyNotSupportedException
                or TargetInvocationException)
            {
                output.WriteLine($"DumpListDiagnostics failed: {ex}");
            }
            throw new TimeoutException("File list element was not found within the timeout.");
        }

        return listElement.AsListBox();
    }

    private static void WaitForWindowReady(Window window)
    {
        Retry.WhileTrue(
            () =>
            {
                if (!SafeGet(() => window.IsEnabled, false) || SafeGet(() => window.IsOffscreen, false))
                {
                    return true;
                }

                var width = SafeGet(() => window.BoundingRectangle.Width, 0d);
                var height = SafeGet(() => window.BoundingRectangle.Height, 0d);
                return width <= 1 || height <= 1;
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

        var targetElement = element;
        if (!SafeGet(() => targetElement.IsEnabled, false) || SafeGet(() => targetElement.IsOffscreen, false))
        {
            return false;
        }

        var width = SafeGet(() => targetElement.BoundingRectangle.Width, 0d);
        var height = SafeGet(() => targetElement.BoundingRectangle.Height, 0d);
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        return true;
    }

    private static AutomationElement? FindListElement(AutomationElement scope)
    {
        foreach (var automationId in FileListAutomationIds)
        {
            var element = scope.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null)
            {
                return element;
            }
        }

        var candidates = scope.FindAllDescendants(cf => cf.ByControlType(ControlType.List).Or(cf.ByControlType(ControlType.DataGrid)));
        return SelectBestFileListCandidate(candidates);
    }

    private static AutomationElement? FindListElement(AutomationElement scope, int processId)
    {
        var candidates = scope.FindAllDescendants(cf => cf.ByAutomationId("FileListDetails")
            .Or(cf.ByAutomationId("FileListIcon"))
            .Or(cf.ByAutomationId("FileListList")));
        var explicitMatch = candidates.FirstOrDefault(candidate => candidate.Properties.ProcessId.ValueOrDefault == processId);
        if (explicitMatch is not null)
        {
            return explicitMatch;
        }

        var fallbackCandidates = scope.FindAllDescendants(cf => cf.ByControlType(ControlType.List).Or(cf.ByControlType(ControlType.DataGrid)))
            .Where(candidate => SafeGet(() => candidate.Properties.ProcessId.ValueOrDefault, -1) == processId);
        return SelectBestFileListCandidate(fallbackCandidates);
    }

    private static void WaitForPreview(Window window)
    {
        Retry.WhileTrue(
            () =>
            {
                var preview = window.FindFirstDescendant(cf => cf.ByAutomationId("PreviewImage"));
                return preview is null || SafeGet(() => preview.IsOffscreen, false);
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

        if (SafeGet(() => summary.IsOffscreen, false))
        {
            return false;
        }

        var width = SafeGet(() => summary.BoundingRectangle.Width, 0d);
        var height = SafeGet(() => summary.BoundingRectangle.Height, 0d);
        if (width <= 1 || height <= 1)
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

    private static void DumpListDiagnostics(
        ITestOutputHelper output,
        UIA3Automation automation,
        Application app,
        Window window)
    {
        output.WriteLine("=== UIA diagnostics: FileList ===");
        DumpElementSummary(output, "MainWindow", window);
        foreach (var automationId in FileListAutomationIds)
        {
            DumpSpecificElement(output, $"Window.{automationId}", window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)));
        }

        var desktop = automation.GetDesktop();
        foreach (var automationId in FileListAutomationIds)
        {
            DumpSpecificElement(output, $"Desktop.{automationId}", FindByAutomationId(desktop, automationId, app.ProcessId));
        }

        var windowDescendants = window.FindAllDescendants();
        output.WriteLine($"Window descendants: {windowDescendants.Length}");
        DumpListCandidates(output, "Window list candidates", windowDescendants, processId: null);

        var desktopDescendants = desktop.FindAllDescendants();
        output.WriteLine($"Desktop descendants (process {app.ProcessId}): {desktopDescendants.Length}");
        DumpListCandidates(output, "Desktop list candidates (process)", desktopDescendants, processId: app.ProcessId);

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

    private static void DumpListCandidates(
        ITestOutputHelper output,
        string label,
        IEnumerable<AutomationElement> elements,
        int? processId)
    {
        var candidates = elements
            .Where(element => IsListCandidate(element, processId))
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

    private static bool IsListCandidate(AutomationElement element, int? processId)
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
        var controlType = SafeGet(() => element.ControlType, ControlType.Custom);
        return controlType == ControlType.List
            || controlType == ControlType.DataGrid
            || FileListAutomationIds.Any(id => string.Equals(id, automationId, StringComparison.Ordinal));
    }

    private static AutomationElement? SelectBestFileListCandidate(IEnumerable<AutomationElement> candidates)
    {
        return candidates
            .OrderByDescending(IsListCandidateLikelyFileList)
            .ThenByDescending(candidate => SafeGet(() => candidate.BoundingRectangle.Width * candidate.BoundingRectangle.Height, 0d))
            .FirstOrDefault();
    }

    private static bool IsListCandidateLikelyFileList(AutomationElement element)
    {
        var automationId = SafeGet(() => element.Properties.AutomationId.ValueOrDefault, string.Empty);
        if (FileListAutomationIds.Any(id => string.Equals(id, automationId, StringComparison.Ordinal)))
        {
            return true;
        }

        var name = SafeGet(() => element.Name, string.Empty);
        var className = SafeGet(() => element.ClassName, string.Empty);
        return ContainsIgnoreCase(automationId, "file")
            || ContainsIgnoreCase(automationId, "list")
            || ContainsIgnoreCase(name, "file")
            || ContainsIgnoreCase(className, "list");
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
        try
        {
            var candidates = scope.FindAllDescendants(cf => cf.ByAutomationId(automationId));
            return candidates.FirstOrDefault(candidate => SafeGet(() => candidate.Properties.ProcessId.ValueOrDefault, -1) == processId);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Win32Exception or TimeoutException)
        {
            return null;
        }
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
                return status is not null && !SafeGet(() => status.IsOffscreen, true);
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
