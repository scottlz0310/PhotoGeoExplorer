using System;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// FileBrowserDialogErrorMap の純粋マッピング（FileOperationError → リソースキー）の網羅テスト。
/// ContentDialog 表示やローカライズ解決から独立して、操作種別ごとに異なる対応関係を固定する。
/// 各操作で既知の FileOperationError 全 8 値を検証し、default フォールバックも併せて固定する。
/// </summary>
/// <remarks>
/// FileOperationError は internal のため、public テストメソッドのパラメータ型には直接使えない（CS0051）。
/// InlineData では nameof で enum 名を渡し、メソッド内で Enum.Parse して使う。
/// </remarks>
public sealed class FileBrowserDialogErrorMapTests
{
    private const string DefaultTitleKey = "Dialog.CreateFolderFailed.Title";
    private const string SeeLogDetailKey = "Dialog.SeeLogDetail";

    [Theory]
    [InlineData(nameof(FileOperationError.InvalidName), "Dialog.InvalidName.Title", "Dialog.InvalidName.Detail")]
    [InlineData(nameof(FileOperationError.AlreadyExists), "Dialog.AlreadyExists.Title", "Dialog.AlreadyExists.Detail")]
    [InlineData(nameof(FileOperationError.NoParent), "Dialog.RenameNotAvailable.Title", "Dialog.RenameNotAvailable.Detail")]
    [InlineData(nameof(FileOperationError.Unauthorized), DefaultTitleKey, SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.None), DefaultTitleKey, SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.DescendantPath), DefaultTitleKey, SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.IoError), DefaultTitleKey, SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.Cancelled), DefaultTitleKey, SeeLogDetailKey)]
    public void MapFileOperationError_ReturnsExpectedKeys(
        string errorName, string expectedTitleKey, string expectedMessageKey)
    {
        var error = Enum.Parse<FileOperationError>(errorName);

        var (titleKey, messageKey) = FileBrowserDialogErrorMap.MapFileOperationError(error, DefaultTitleKey);

        Assert.Equal(expectedTitleKey, titleKey);
        Assert.Equal(expectedMessageKey, messageKey);
    }

    [Theory]
    [InlineData(nameof(FileOperationError.DescendantPath), "Dialog.MoveFailed.Title", "Dialog.MoveIntoSelf.Detail")]
    [InlineData(nameof(FileOperationError.AlreadyExists), "Dialog.AlreadyExists.Title", "Dialog.AlreadyExistsDestination.Detail")]
    [InlineData(nameof(FileOperationError.Unauthorized), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.None), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.InvalidName), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.NoParent), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.IoError), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.Cancelled), "Dialog.MoveFailed.Title", SeeLogDetailKey)]
    public void MapMoveError_ReturnsExpectedKeys(
        string errorName, string expectedTitleKey, string expectedMessageKey)
    {
        var firstError = Enum.Parse<FileOperationError>(errorName);

        var (titleKey, messageKey) = FileBrowserDialogErrorMap.MapMoveError(firstError);

        Assert.Equal(expectedTitleKey, titleKey);
        Assert.Equal(expectedMessageKey, messageKey);
    }

    [Theory]
    [InlineData(nameof(FileOperationError.AlreadyExists), "Dialog.AlreadyExists.Title", "Dialog.AlreadyExistsDestination.Detail")]
    [InlineData(nameof(FileOperationError.Unauthorized), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.None), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.InvalidName), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.DescendantPath), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.NoParent), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.IoError), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.Cancelled), "Dialog.CopyFailed.Title", SeeLogDetailKey)]
    public void MapCopyError_ReturnsExpectedKeys(
        string errorName, string expectedTitleKey, string expectedMessageKey)
    {
        var firstError = Enum.Parse<FileOperationError>(errorName);

        var (titleKey, messageKey) = FileBrowserDialogErrorMap.MapCopyError(firstError);

        Assert.Equal(expectedTitleKey, titleKey);
        Assert.Equal(expectedMessageKey, messageKey);
    }

    [Theory]
    [InlineData(nameof(FileOperationError.NoParent), "Dialog.DeleteNotAvailable.Title", "Dialog.DeleteNotAvailable.Detail")]
    [InlineData(nameof(FileOperationError.Unauthorized), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.None), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.InvalidName), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.AlreadyExists), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.DescendantPath), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.IoError), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    [InlineData(nameof(FileOperationError.Cancelled), "Dialog.DeleteFailed.Title", SeeLogDetailKey)]
    public void MapDeleteError_ReturnsExpectedKeys(
        string errorName, string expectedTitleKey, string expectedMessageKey)
    {
        var firstError = Enum.Parse<FileOperationError>(errorName);

        var (titleKey, messageKey) = FileBrowserDialogErrorMap.MapDeleteError(firstError);

        Assert.Equal(expectedTitleKey, titleKey);
        Assert.Equal(expectedMessageKey, messageKey);
    }
}
