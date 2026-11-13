using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;

namespace PhotoGeoExplorer.Tests;

// リソースが読めないテスト環境では、LocalizationService はキーをそのまま返す。文言ではなくキーで確かめる
public class SettingsPaneAreaDataSectionViewModelTests
{
    private static readonly AreaDataPackInfo Tottori = new("31", "鳥取県", 2020, 5_000_000);
    private static readonly AreaDataPackInfo Tokyo = new("13", "東京都", 2015, 800);

    [Fact]
    public async Task RefreshAsyncListsTheImportedPacksInCodeOrder()
    {
        var store = new FakeAreaDataStore();
        store.Add(Tottori);
        store.Add(Tokyo);
        var section = CreateSection(store);

        await section.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("東京都,鳥取県", string.Join(',', section.Packs.Select(pack => pack.PrefectureName)));
        Assert.Equal(Visibility.Collapsed, section.EmptyVisibility);
    }

    [Fact]
    public async Task EmptyVisibilityFollowsTheListAndRaisesPropertyChanged()
    {
        var store = new FakeAreaDataStore();
        var section = CreateSection(store);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)section).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await section.RefreshAsync().ConfigureAwait(true);
        Assert.Equal(Visibility.Visible, section.EmptyVisibility);

        store.Add(Tottori);
        await section.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(Visibility.Collapsed, section.EmptyVisibility);
        Assert.Contains(nameof(SettingsPaneAreaDataSectionViewModel.EmptyVisibility), changed);
    }

    [Fact]
    public async Task RefreshAsyncReportsAnIOFailureWithoutThrowing()
    {
        var store = new FakeAreaDataStore { Failure = FakeAreaDataStore.NewIOException() };
        var section = CreateSection(store);

        await section.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("AreaData.Error.Io", section.StatusMessage);
        Assert.Empty(section.Packs);
    }

    [Fact]
    public async Task AddAsyncImportsTheSelectedArchiveAndRefreshesTheList()
    {
        var store = new FakeAreaDataStore();
        var section = CreateSection(store, pickedPath: @"C:\Downloads\r2ka31.zip");

        await section.AddAsync().ConfigureAwait(true);

        Assert.Equal(@"C:\Downloads\r2ka31.zip", Assert.Single(store.ImportedArchives));
        Assert.Equal("AreaData.Imported", section.StatusMessage);
        Assert.Equal("鳥取県", Assert.Single(section.Packs).PrefectureName);
        Assert.False(section.IsBusy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AddAsyncDoesNothingWhenThePickerIsCanceled(string? pickedPath)
    {
        var store = new FakeAreaDataStore();
        var section = CreateSection(store, pickedPath);

        await section.AddAsync().ConfigureAwait(true);

        Assert.Empty(store.ImportedArchives);
        Assert.Equal(string.Empty, section.StatusMessage);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task AddAsyncReplacesAnImportedPackOfTheSamePrefecture()
    {
        var store = new FakeAreaDataStore();
        store.Add(new AreaDataPackInfo("31", "鳥取県", 2015, 1_000));
        var section = CreateSection(store, pickedPath: @"C:\Downloads\r2ka31.zip");
        await section.RefreshAsync().ConfigureAwait(true);

        await section.AddAsync().ConfigureAwait(true);

        var pack = Assert.Single(section.Packs);
        Assert.Equal(Tottori, pack.Info);
    }

    [Theory]
    [MemberData(nameof(AllFormatErrors))]
    public async Task AddAsyncShowsAReasonForEveryKindOfRejectedArchive(string errorName)
    {
        var error = Enum.Parse<AreaDataFormatError>(errorName);
        var store = new FakeAreaDataStore { Failure = new AreaDataFormatException(error, "rejected") };
        var section = CreateSection(store, pickedPath: @"C:\Downloads\bad.zip");

        await section.AddAsync().ConfigureAwait(true);

        Assert.Equal(SettingsPaneAreaDataSectionViewModel.GetErrorKey(error), section.StatusMessage);
        Assert.StartsWith("AreaData.Error.", section.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(section.Packs);
        Assert.False(section.IsBusy);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AddAsyncReportsFileSystemFailuresWithoutThrowing(Type exceptionType)
    {
        var store = new FakeAreaDataStore { Failure = (Exception)Activator.CreateInstance(exceptionType)! };
        var section = CreateSection(store, pickedPath: @"C:\Downloads\r2ka31.zip");

        await section.AddAsync().ConfigureAwait(true);

        Assert.Equal("AreaData.Error.Io", section.StatusMessage);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task AddAsyncIsBusyAndIgnoresAnotherAddWhileImporting()
    {
        var gate = new TaskCompletionSource();
        var store = new FakeAreaDataStore { ImportGate = gate.Task };
        var section = CreateSection(store, pickedPath: @"C:\Downloads\r2ka31.zip");
        await section.RefreshAsync().ConfigureAwait(true);

        var first = section.AddAsync();
        await WaitForAsync(() => store.ImportedArchives.Count == 1).ConfigureAwait(true);
        Assert.True(section.IsBusy);
        Assert.False(section.AddCommand.CanExecute(null));

        await section.AddAsync().ConfigureAwait(true);
        Assert.Single(store.ImportedArchives);

        gate.SetResult();
        await first.ConfigureAwait(true);

        Assert.False(section.IsBusy);
        Assert.True(section.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteAsyncRemovesThePackAndRefreshesTheList()
    {
        var store = new FakeAreaDataStore();
        store.Add(Tottori);
        store.Add(Tokyo);
        var section = CreateSection(store);
        await section.RefreshAsync().ConfigureAwait(true);

        await section.DeleteAsync(Tottori).ConfigureAwait(true);

        Assert.Equal("31", Assert.Single(store.DeletedCodes));
        Assert.Equal("AreaData.Deleted", section.StatusMessage);
        Assert.Equal("東京都", Assert.Single(section.Packs).PrefectureName);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task DeleteAsyncKeepsTheListAndReportsAFailure()
    {
        var store = new FakeAreaDataStore();
        store.Add(Tottori);
        var section = CreateSection(store);
        await section.RefreshAsync().ConfigureAwait(true);
        store.Failure = FakeAreaDataStore.NewIOException();

        await section.DeleteAsync(Tottori).ConfigureAwait(true);

        Assert.Equal("AreaData.Error.Io", section.StatusMessage);
        Assert.Single(section.Packs);
        Assert.False(section.IsBusy);
    }

    [Fact]
    public async Task ThePackItemExposesNameDetailsAndADeleteCommandThatDeletesThatPack()
    {
        var store = new FakeAreaDataStore();
        store.Add(Tottori);
        var section = CreateSection(store);
        await section.RefreshAsync().ConfigureAwait(true);
        var item = Assert.Single(section.Packs);

        Assert.Equal("鳥取県", item.PrefectureName);
        Assert.Equal("AreaData.PackDetail", item.DetailText);
        Assert.Equal("AreaData.DeleteAutomationName", item.DeleteAutomationName);
        Assert.True(item.DeleteCommand.CanExecute(null));

        item.DeleteCommand.Execute(null);
        await WaitForAsync(() => store.DeletedCodes.Count == 1).ConfigureAwait(true);

        Assert.Equal("31", Assert.Single(store.DeletedCodes));
    }

    [Theory]
    [InlineData(0, "1 KB")]
    [InlineData(1, "1 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1025, "2 KB")]
    [InlineData(800_000, "782 KB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(32_180_266, "30.7 MB")]
    public void FormatSizeUsesKilobytesBelowOneMegabyteAndMegabytesAbove(long bytes, string expected)
    {
        Assert.Equal(expected, AreaDataPackItemViewModel.FormatSize(bytes));
    }

    [Theory]
    [MemberData(nameof(AllFormatErrors))]
    public void EveryErrorKindHasAMessageInBothLanguages(string errorName)
    {
        var error = Enum.Parse<AreaDataFormatError>(errorName);
        var key = SettingsPaneAreaDataSectionViewModel.GetErrorKey(error);

        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            Assert.True(ResourceKeys(language).Contains(key), $"{language} の Resources.resw に {key} がありません。");
        }
    }

    [Fact]
    public void EveryAreaDataResourceUsedByTheViewHasAMessageInBothLanguages()
    {
        var keys = new[]
        {
            "SettingsPaneAreaDataSection.Text",
            "SettingsPaneAreaDataDescription.Text",
            "SettingsPaneAreaDataEmpty.Text",
            "SettingsPaneAreaDataDeleteButton.Content",
            "SettingsPaneAreaDataAddButton.Content",
            "SettingsPaneAreaDataAttribution.Text",
            "SettingsPaneAreaDataDownloadLink.Content",
            "AreaData.PackDetail",
            "AreaData.DeleteAutomationName",
            "AreaData.Importing",
            "AreaData.Imported",
            "AreaData.Deleted",
            "AreaData.Error.Io"
        };

        foreach (var language in new[] { "ja-JP", "en-US" })
        {
            var available = ResourceKeys(language);
            Assert.All(keys, key => Assert.True(available.Contains(key), $"{language} の Resources.resw に {key} がありません。"));
        }
    }

    [Fact]
    public void ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsPaneAreaDataSectionViewModel(null!, _ => Task.FromResult<string?>(null)));
        Assert.Throws<ArgumentNullException>(() => new SettingsPaneAreaDataSectionViewModel(new FakeAreaDataStore(), null!));
    }

    public static IEnumerable<object[]> AllFormatErrors()
        => Enum.GetNames<AreaDataFormatError>().Select(name => new object[] { name });

    private static SettingsPaneAreaDataSectionViewModel CreateSection(FakeAreaDataStore store, string? pickedPath = null)
        => new(store, _ => Task.FromResult(pickedPath));

    private static HashSet<string> ResourceKeys(string language)
    {
        var path = Path.Combine(RepositoryPaths.FindRoot(), "PhotoGeoExplorer", "Strings", language, "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data").Select(element => (string)element.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(true);
        }
    }
}
