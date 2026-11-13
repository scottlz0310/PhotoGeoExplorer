using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Services;
using Windows.Storage.Pickers;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 取り込む ZIP を OS のファイル選択ダイアログで選んでもらう（WinRT Interop のみで、判断を含まない）
/// </summary>
internal sealed class AreaDataArchivePicker
{
    private readonly IDialogService _dialogService;

    public AreaDataArchivePicker(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));
    }

    /// <returns>選ばれた ZIP のパス。キャンセルされたら null</returns>
    public async Task<string?> PickAsync(CancellationToken cancellationToken)
    {
        var file = await _dialogService
            .ShowFilePickerAsync(PickerLocationId.Downloads, [".zip"], cancellationToken)
            .ConfigureAwait(true);
        return file?.Path;
    }
}
