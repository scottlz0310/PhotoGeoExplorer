using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.FileBrowser;

/// <summary>
/// ファイルブラウザのステータス表示を担う子 ViewModel。
/// ステータスオーバーレイ（空フォルダ・エラー時の案内）、ステータスバー（フォルダ/件数/選択/GPS 表示）、
/// 選択アイテムの EXIF メタデータ非同期ロード（CTS 管理を含む）を担当する。
/// 親 ViewModel から選択変更・フォルダ読み込み・フィルタ変更のタイミングでメソッド呼び出しで状態を受け取る。
/// </summary>
internal sealed class FileBrowserStatusViewModel : BindableBase, IDisposable
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<string, CancellationToken, Task<PhotoMetadata?>> _getMetadataAsync;

    private string? _statusMessage;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private string? _statusTitle;
    private string? _statusDetail;
    private Symbol _statusSymbol = Symbol.Help;
    private StatusAction _statusPrimaryAction;
    private StatusAction _statusSecondaryAction;
    private string? _statusPrimaryActionLabel;
    private string? _statusSecondaryActionLabel;
    private Visibility _statusPrimaryActionVisibility = Visibility.Collapsed;
    private Visibility _statusSecondaryActionVisibility = Visibility.Collapsed;
    private string? _statusBarText;
    private Symbol _statusBarLocationSymbol = Symbol.Map;
    private Visibility _statusBarLocationVisibility = Visibility.Collapsed;
    private string? _statusBarLocationTooltip;
    private PhotoMetadata? _selectedMetadata;
    private CancellationTokenSource? _metadataCts;
    private bool _hasActiveFilters;
    private int _selectedCount;
    private PhotoListItem? _selectedItem;

    /// <param name="uiDispatcher">メタデータロード完了時の UI 更新に使うディスパッチャ。</param>
    /// <param name="getMetadataAsync">EXIF メタデータ取得処理。null の場合は <see cref="ExifReader.GetMetadataAsync"/>（テスト用シーム）。</param>
    public FileBrowserStatusViewModel(
        IUiDispatcher uiDispatcher,
        Func<string, CancellationToken, Task<PhotoMetadata?>>? getMetadataAsync = null)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _uiDispatcher = uiDispatcher;
        _getMetadataAsync = getMetadataAsync ?? ExifReader.GetMetadataAsync;
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set => SetProperty(ref _statusVisibility, value);
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public string? StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string? StatusDetail
    {
        get => _statusDetail;
        private set => SetProperty(ref _statusDetail, value);
    }

    public Symbol StatusSymbol
    {
        get => _statusSymbol;
        private set => SetProperty(ref _statusSymbol, value);
    }

    public StatusAction StatusPrimaryAction
    {
        get => _statusPrimaryAction;
        private set => SetProperty(ref _statusPrimaryAction, value);
    }

    public StatusAction StatusSecondaryAction
    {
        get => _statusSecondaryAction;
        private set => SetProperty(ref _statusSecondaryAction, value);
    }

    public string? StatusPrimaryActionLabel
    {
        get => _statusPrimaryActionLabel;
        private set => SetProperty(ref _statusPrimaryActionLabel, value);
    }

    public string? StatusSecondaryActionLabel
    {
        get => _statusSecondaryActionLabel;
        private set => SetProperty(ref _statusSecondaryActionLabel, value);
    }

    public Visibility StatusPrimaryActionVisibility
    {
        get => _statusPrimaryActionVisibility;
        private set => SetProperty(ref _statusPrimaryActionVisibility, value);
    }

    public Visibility StatusSecondaryActionVisibility
    {
        get => _statusSecondaryActionVisibility;
        private set => SetProperty(ref _statusSecondaryActionVisibility, value);
    }

    public string? StatusBarText
    {
        get => _statusBarText;
        private set => SetProperty(ref _statusBarText, value);
    }

    public Symbol StatusBarLocationSymbol
    {
        get => _statusBarLocationSymbol;
        private set => SetProperty(ref _statusBarLocationSymbol, value);
    }

    public Visibility StatusBarLocationVisibility
    {
        get => _statusBarLocationVisibility;
        private set => SetProperty(ref _statusBarLocationVisibility, value);
    }

    public string? StatusBarLocationTooltip
    {
        get => _statusBarLocationTooltip;
        private set => SetProperty(ref _statusBarLocationTooltip, value);
    }

    /// <summary>選択中アイテムの EXIF メタデータ。View のコンテキストメニュー（Google Maps で開く等）から参照される。</summary>
    internal PhotoMetadata? SelectedMetadata => _selectedMetadata;

    /// <summary>
    /// ステータスメッセージを設定し、オーバーレイ表示を更新する。UI スレッド上から呼び出すこと。
    /// </summary>
    public void SetStatus(string? message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        StatusVisibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatusOverlay(message, severity);
    }

    /// <summary>
    /// フィルタ状態の変化を受け取り、オーバーレイの文言・アクションを再評価する。
    /// </summary>
    public void NotifyFiltersChanged(bool hasActiveFilters)
    {
        _hasActiveFilters = hasActiveFilters;
        UpdateStatusOverlay(StatusMessage, _statusSeverity);
    }

    /// <summary>
    /// 現在のフォルダ・件数・選択状態からステータスバーのテキストと GPS アイコンを更新する。
    /// 選択状態はメタデータロード完了時の GPS アイコン再評価にも使うため内部に保持する。
    /// </summary>
    public void UpdateStatusBar(string? currentFolderPath, int itemCount, int selectedCount, PhotoListItem? selectedItem)
    {
        _selectedCount = selectedCount;
        _selectedItem = selectedItem;

        var folderLabel = string.IsNullOrWhiteSpace(currentFolderPath)
            ? LocalizationService.GetString("StatusBar.NoFolderSelected")
            : currentFolderPath;
        string? selectedLabel;
        if (selectedCount == 0)
        {
            selectedLabel = null;
        }
        else if (selectedCount == 1 && selectedItem is not null)
        {
            selectedLabel = LocalizationService.Format("StatusBar.Selected", selectedItem.FileName);
        }
        else
        {
            selectedLabel = LocalizationService.Format("StatusBar.SelectedMultiple", selectedCount);
        }

        var resolutionLabel = selectedCount == 1 && selectedItem is not null && !selectedItem.IsFolder
            ? selectedItem.ResolutionText
            : null;

        var itemsLabel = LocalizationService.Format("StatusBar.Items", itemCount);
        var statusText = selectedLabel is null
            ? $"{folderLabel} | {itemsLabel}"
            : $"{folderLabel} | {itemsLabel} | {selectedLabel}";
        if (!string.IsNullOrWhiteSpace(resolutionLabel))
        {
            statusText = $"{statusText} | {resolutionLabel}";
        }

        StatusBarText = statusText;
        UpdateStatusBarLocation();
    }

    /// <summary>
    /// ステータスバーのテキストを直接設定する（Move/Copy の進捗・完了メッセージ用）。
    /// </summary>
    public void SetStatusBarText(string? text)
    {
        StatusBarText = text;
    }

    /// <summary>
    /// 選択アイテムの EXIF メタデータを非同期ロードし、完了時に GPS アイコンを更新する。
    /// 先行するロードはキャンセルする。
    /// </summary>
    public async Task LoadMetadataAsync(PhotoListItem? item)
    {
        // _metadataCts の差し替え（先行 CTS の取得と新 CTS の公開）は最初の await より前に
        // 同期的に行う。await 後に公開すると、連続呼び出し時に後続が先行の CTS を観測できず
        // キャンセル漏れが起き、古い選択のメタデータで表示が上書きされる（#164）
        var previousCts = _metadataCts;
        CancellationToken token = default;
        if (item is null || item.IsFolder)
        {
            _metadataCts = null;
        }
        else
        {
            var cts = new CancellationTokenSource();
            // CancelMetadataLoad が CTS を Dispose した後に Token getter へ触れると
            // ObjectDisposedException になるため、await 前に CancellationToken を保持する
            token = cts.Token;
            _metadataCts = cts;
        }

        if (previousCts is not null)
        {
            await previousCts.CancelAsync().ConfigureAwait(false);
            previousCts.Dispose();
        }

        _selectedMetadata = null;
        await _uiDispatcher.RunAsync(UpdateStatusBarLocation).ConfigureAwait(false);

        if (item is null || item.IsFolder)
        {
            return;
        }

        try
        {
            var metadata = await _getMetadataAsync(item.FilePath, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _selectedMetadata = metadata;
            await _uiDispatcher.RunAsync(UpdateStatusBarLocation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // メタデータ読み込み処理がキャンセルされた場合は想定された動作のため、何もしない
        }
    }

    public void CancelMetadataLoad()
    {
        var previousCts = _metadataCts;
        _metadataCts = null;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済み
            }
        }
    }

    public void Dispose()
    {
        CancelMetadataLoad();
    }

    private void UpdateStatusOverlay(string? message, InfoBarSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusTitle = null;
            StatusDetail = null;
            StatusSymbol = Symbol.Help;
            SetStatusActions(StatusAction.None, StatusAction.None);
            return;
        }

        if (message == LocalizationService.GetString("Message.NoFilesFound"))
        {
            StatusTitle = LocalizationService.GetString("Overlay.NoFilesFoundTitle");
            StatusDetail = _hasActiveFilters
                ? LocalizationService.GetString("Overlay.NoFilesFoundDetailWithFilters")
                : LocalizationService.GetString("Overlay.NoFilesFoundDetail");
            StatusSymbol = Symbol.Pictures;
            SetStatusActions(StatusAction.OpenFolder, _hasActiveFilters ? StatusAction.ResetFilters : StatusAction.None);
            return;
        }

        if (severity == InfoBarSeverity.Error)
        {
            StatusTitle = LocalizationService.GetString("Overlay.LoadFolderErrorTitle");
            StatusDetail = message;
            StatusSymbol = Symbol.Folder;
            SetStatusActions(StatusAction.OpenFolder, StatusAction.GoHome);
            return;
        }

        StatusTitle = message;
        StatusDetail = null;
        StatusSymbol = Symbol.Help;
        SetStatusActions(StatusAction.None, StatusAction.None);
    }

    private void SetStatusActions(StatusAction primary, StatusAction secondary)
    {
        StatusPrimaryAction = primary;
        StatusSecondaryAction = secondary;
        StatusPrimaryActionLabel = GetActionLabel(primary);
        StatusSecondaryActionLabel = GetActionLabel(secondary);
        StatusPrimaryActionVisibility = primary == StatusAction.None ? Visibility.Collapsed : Visibility.Visible;
        StatusSecondaryActionVisibility = secondary == StatusAction.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? GetActionLabel(StatusAction action)
    {
        return action switch
        {
            StatusAction.OpenFolder => LocalizationService.GetString("Action.OpenFolder"),
            StatusAction.GoHome => LocalizationService.GetString("Action.GoHome"),
            StatusAction.ResetFilters => LocalizationService.GetString("Action.ResetFilters"),
            _ => null
        };
    }

    private void UpdateStatusBarLocation()
    {
        if (_selectedCount != 1 || _selectedItem is null || _selectedItem.IsFolder)
        {
            StatusBarLocationVisibility = Visibility.Collapsed;
            StatusBarLocationSymbol = Symbol.Map;
            StatusBarLocationTooltip = null;
            return;
        }

        if (_selectedMetadata?.HasValidLocation == true)
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationSymbol = Symbol.Map;
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsAvailable");
        }
        else if (_selectedMetadata is null)
        {
            StatusBarLocationVisibility = Visibility.Collapsed;
            StatusBarLocationSymbol = Symbol.Map;
            StatusBarLocationTooltip = null;
        }
        else if (_selectedMetadata.IsLikelyLocationFixFailed)
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationSymbol = Symbol.Important;
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsFixFailed");
        }
        else
        {
            StatusBarLocationVisibility = Visibility.Visible;
            StatusBarLocationSymbol = Symbol.Cancel;
            StatusBarLocationTooltip = LocalizationService.GetString("StatusBar.GpsMissing");
        }
    }
}
