using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class FolderPickerHistoryTests
{
    private const string OpenKey = "PhotoGeoExplorer.FileBrowser.Open";
    private const string MoveKey = "PhotoGeoExplorer.FileBrowser.Move";

    [Fact]
    public void GetStartFolderReturnsNullWhenNothingRecorded()
    {
        var history = new FolderPickerHistory(_ => true);

        Assert.Null(history.GetStartFolder(OpenKey));
    }

    [Theory]
    [InlineData(true, @"C:\photos")]
    [InlineData(false, null)]
    public void GetStartFolderReturnsRecordedFolderOnlyWhenItExists(bool exists, string? expected)
    {
        var history = new FolderPickerHistory(_ => exists);
        history.Record(OpenKey, @"C:\photos");

        Assert.Equal(expected, history.GetStartFolder(OpenKey));
    }

    [Fact]
    public void FoldersAreRememberedPerKey()
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, @"C:\open");
        history.Record(MoveKey, @"C:\move");

        Assert.Equal(@"C:\open", history.GetStartFolder(OpenKey));
        Assert.Equal(@"C:\move", history.GetStartFolder(MoveKey));
        Assert.Null(history.GetStartFolder("PhotoGeoExplorer.FileBrowser.Copy"));
    }

    [Theory]
    [InlineData(@"C:\photos", @"C:\photos", false)]
    [InlineData(@"C:\photos", @"C:\other", true)]
    [InlineData(@"C:\photos", @"C:\PHOTOS", true)]
    public void RecordReportsWhetherTheFolderChanged(string first, string second, bool expectedChanged)
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, first);

        Assert.Equal(expectedChanged, history.Record(OpenKey, second));
        Assert.Equal(second, history.GetStartFolder(OpenKey));
    }

    [Fact]
    public void RecordReportsChangeForTheFirstFolder()
    {
        var history = new FolderPickerHistory(_ => true);

        Assert.True(history.Record(OpenKey, @"C:\photos"));
    }

    [Theory]
    [InlineData("", @"C:\photos")]
    [InlineData(" ", @"C:\photos")]
    [InlineData(OpenKey, "")]
    [InlineData(OpenKey, " ")]
    public void RecordThrowsForBlankArguments(string key, string path)
    {
        var history = new FolderPickerHistory(_ => true);

        Assert.ThrowsAny<ArgumentException>(() => history.Record(key, path));
    }

    [Fact]
    public void LoadReplacesTheRecordsAndSkipsBlankEntries()
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, @"C:\old");

        history.Load(new Dictionary<string, string>
        {
            [MoveKey] = @"C:\move",
            [" "] = @"C:\blank-key",
            ["PhotoGeoExplorer.FileBrowser.Copy"] = " "
        });

        Assert.Null(history.GetStartFolder(OpenKey));
        Assert.Equal(@"C:\move", history.GetStartFolder(MoveKey));
        Assert.Equal(new Dictionary<string, string> { [MoveKey] = @"C:\move" }, history.ToDictionary());
    }

    [Fact]
    public void LoadWithNullClearsTheRecords()
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, @"C:\old");

        history.Load(null);

        Assert.Empty(history.ToDictionary());
    }

    [Fact]
    public void ToDictionaryReturnsASnapshot()
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, @"C:\first");
        var snapshot = history.ToDictionary();

        history.Record(OpenKey, @"C:\second");

        Assert.Equal(@"C:\first", snapshot[OpenKey]);
    }

    [Fact]
    public void ConstructorThrowsWhenDirectoryExistsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderPickerHistory(null!));
    }
}
