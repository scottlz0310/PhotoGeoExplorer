using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Panes.FileBrowser;
using PhotoGeoExplorer.Services;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Panes.Export;

/// <summary>
/// 書き出しの保存先の選択・競合の確認・結果の通知（判断を含まない、画面だけの処理）
/// </summary>
internal sealed class GeoExportUi : IGeoExportUi
{
    internal const string FolderPickerSettingsIdentifier = "PhotoGeoExplorer.Export";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CsvFileTypes
        = new Dictionary<string, IReadOnlyList<string>> { ["CSV"] = [".csv"] };

    private readonly IDialogService _dialogService;
    private readonly FileBrowserDialogs _dialogs;
    private readonly Action<string, InfoBarSeverity> _notify;

    public GeoExportUi(IDialogService dialogService, FileBrowserDialogs dialogs, Action<string, InfoBarSeverity> notify)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
    }

    public async Task<string?> PickCsvFileAsync(string suggestedFileName)
    {
        var file = await _dialogService
            .ShowSaveFilePickerAsync(PickerLocationId.DocumentsLibrary, suggestedFileName, CsvFileTypes)
            .ConfigureAwait(true);
        return file?.Path;
    }

    public Task<string?> PickDestinationFolderAsync()
        => _dialogs.PickFolderAsync(FolderPickerSettingsIdentifier, suggestedStartFolder: null);

    public Task<ConflictResolution> ResolveConflictAsync(string fileName)
        => _dialogs.ShowCopyConflictAsync(fileName, isFolder: false);

    public void Notify(string message, InfoBarSeverity severity) => _notify(message, severity);
}
