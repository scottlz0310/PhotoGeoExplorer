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

    [Fact]
    public void RecordReplacesThePreviousFolderForTheSameKey()
    {
        var history = new FolderPickerHistory(_ => true);
        history.Record(OpenKey, @"C:\first");

        history.Record(OpenKey, @"C:\second");

        Assert.Equal(@"C:\second", history.GetStartFolder(OpenKey));
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
    public void RecordFileLocationRemembersTheParentFolderOfTheFile()
    {
        var history = new FolderPickerHistory(_ => true);

        history.RecordFileLocation(OpenKey, @"C:\exports\map.png");

        Assert.Equal(@"C:\exports", history.GetStartFolder(OpenKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("map.png")]
    [InlineData(@"C:\")]
    public void RecordFileLocationThrowsWhenThereIsNoParentFolder(string filePath)
    {
        var history = new FolderPickerHistory(_ => true);

        Assert.ThrowsAny<ArgumentException>(() => history.RecordFileLocation(OpenKey, filePath));
    }

    [Fact]
    public void ConstructorThrowsWhenDirectoryExistsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderPickerHistory(null!));
    }
}
