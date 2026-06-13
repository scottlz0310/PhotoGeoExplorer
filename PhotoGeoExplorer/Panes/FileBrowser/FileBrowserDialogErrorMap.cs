using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// FileBrowser ペインの操作エラー種別を、ダイアログのタイトル/メッセージのリソースキーへ対応付ける純粋関数群。
/// ローカライズ文字列の解決や <c>ContentDialog</c> 表示からは独立しているため、操作種別ごとに異なる
/// 対応関係をユニットテストで固定できる（フォローアップ ISSUE #167）。
/// </summary>
internal static class FileBrowserDialogErrorMap
{
    private const string SeeLogDetailKey = "Dialog.SeeLogDetail";

    /// <summary>
    /// 新規フォルダ作成・リネーム失敗時のエラー → リソースキー対応。
    /// <paramref name="defaultTitleKey"/> は呼び出し元の操作種別ごとに異なる既定タイトル（作成失敗/リネーム失敗）。
    /// </summary>
    public static (string TitleKey, string MessageKey) MapFileOperationError(
        FileOperationError error,
        string defaultTitleKey)
        => error switch
        {
            FileOperationError.InvalidName => ("Dialog.InvalidName.Title", "Dialog.InvalidName.Detail"),
            FileOperationError.AlreadyExists => ("Dialog.AlreadyExists.Title", "Dialog.AlreadyExists.Detail"),
            FileOperationError.NoParent => ("Dialog.RenameNotAvailable.Title", "Dialog.RenameNotAvailable.Detail"),
            FileOperationError.Unauthorized => (defaultTitleKey, SeeLogDetailKey),
            _ => (defaultTitleKey, SeeLogDetailKey),
        };

    /// <summary>Move 操作失敗時の先頭エラー → リソースキー対応。</summary>
    public static (string TitleKey, string MessageKey) MapMoveError(FileOperationError firstError)
        => firstError switch
        {
            FileOperationError.DescendantPath => ("Dialog.MoveFailed.Title", "Dialog.MoveIntoSelf.Detail"),
            FileOperationError.AlreadyExists => ("Dialog.AlreadyExists.Title", "Dialog.AlreadyExistsDestination.Detail"),
            FileOperationError.Unauthorized => ("Dialog.MoveFailed.Title", SeeLogDetailKey),
            _ => ("Dialog.MoveFailed.Title", SeeLogDetailKey),
        };

    /// <summary>Copy 操作失敗時の先頭エラー → リソースキー対応。</summary>
    public static (string TitleKey, string MessageKey) MapCopyError(FileOperationError firstError)
        => firstError switch
        {
            FileOperationError.AlreadyExists => ("Dialog.AlreadyExists.Title", "Dialog.AlreadyExistsDestination.Detail"),
            FileOperationError.Unauthorized => ("Dialog.CopyFailed.Title", SeeLogDetailKey),
            _ => ("Dialog.CopyFailed.Title", SeeLogDetailKey),
        };

    /// <summary>削除操作失敗時の先頭エラー → リソースキー対応。</summary>
    public static (string TitleKey, string MessageKey) MapDeleteError(FileOperationError firstError)
        => firstError switch
        {
            FileOperationError.NoParent => ("Dialog.DeleteNotAvailable.Title", "Dialog.DeleteNotAvailable.Detail"),
            FileOperationError.Unauthorized => ("Dialog.DeleteFailed.Title", SeeLogDetailKey),
            _ => ("Dialog.DeleteFailed.Title", SeeLogDetailKey),
        };
}
