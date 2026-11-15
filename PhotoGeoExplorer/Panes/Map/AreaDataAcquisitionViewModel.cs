using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaNames;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>取得の確認ダイアログでの利用者の選択</summary>
internal enum AreaDataConsent
{
    /// <summary>今回は取得しない（この起動中は、同じ都道府県を再び尋ねない）</summary>
    Decline,

    /// <summary>取得する</summary>
    Accept,

    /// <summary>取得し、以降は確認せずに取得する</summary>
    AcceptAndRemember,

    /// <summary>取得せず、以降は案内も取得もしない</summary>
    DeclineAndRemember
}

/// <summary>
/// 字地名の地域データ（e-Stat の小地域境界データ）が無い地域の写真を選んだときに、ダウンロードを案内して取得する。
/// 確認ダイアログで同意したときだけ取得する（「以降は確認せずに取得する」を選ぶと、以降は確認なしで取得する）。
/// 1 回に 1 つの取得だけを行い、失敗・中止・拒否した都道府県は、この起動中は自動では再び取得しない
/// （手動の取得は <see cref="DownloadAsync"/>）。取得したデータは既存の取り込み経路（<see cref="IAreaDataDownloader"/>）で検証して置く
/// </summary>
internal sealed class AreaDataAcquisitionViewModel : BindableBase
{
    private readonly IAreaDataNeedDetector? _detector;
    private readonly IAreaDataDownloader? _downloader;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<AreaDataConsent>>? _askConsentAsync;
    private readonly Action<string, InfoBarSeverity> _notify;
    private readonly Func<Action<long>, IProgress<long>> _createProgress;
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);
    private readonly RelayCommand _cancelCommand;
    private CancellationTokenSource? _downloadCancellation;
    private bool _busy;
    private AreaDataDownloadMode _mode;
    private bool _isDownloading;
    private string _statusText = string.Empty;

    /// <summary>取得しない（設定・画面の対象外）。依存を持たない構成（テスト・未配線）で使う</summary>
    internal AreaDataAcquisitionViewModel()
        : this(null, null, null, (_, _) => { }, null)
    {
    }

    /// <param name="askConsentAsync">取得の確認を利用者に尋ねる（View の責務）。引数は都道府県名</param>
    /// <param name="notify">完了・失敗・中止の通知</param>
    /// <param name="createProgress">受信量の報告を UI スレッドへ渡す。既定は <see cref="Progress{T}"/>（作成時の同期コンテキストへ送る）</param>
    internal AreaDataAcquisitionViewModel(
        IAreaDataNeedDetector? detector,
        IAreaDataDownloader? downloader,
        Func<IReadOnlyList<string>, CancellationToken, Task<AreaDataConsent>>? askConsentAsync,
        Action<string, InfoBarSeverity> notify,
        Func<Action<long>, IProgress<long>>? createProgress)
    {
        _detector = detector;
        _downloader = downloader;
        _askConsentAsync = askConsentAsync;
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _createProgress = createProgress ?? (report => new Progress<long>(report));
        _cancelCommand = new RelayCommand(async () =>
        {
            if (_downloadCancellation is { } cancellation)
            {
                await cancellation.CancelAsync().ConfigureAwait(true);
            }
        });
    }

    /// <summary>
    /// 地域データが無い地域の写真を選んだときの動作。確認ダイアログの「以降は確認せずに取得する」「今後は取得しない」でも変わる。設定ファイルに保存される。
    /// <see cref="AreaDataDownloadMode.Never"/> でも、設定からの手動の取得（<see cref="DownloadAsync"/>）は使える
    /// </summary>
    public AreaDataDownloadMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(StatusVisibility));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Visibility StatusVisibility => IsDownloading ? Visibility.Visible : Visibility.Collapsed;

    public ICommand CancelCommand => _cancelCommand;

    /// <summary>
    /// 単一選択された写真の撮影位置を渡す。地域データが無い都道府県なら、確認のうえ取得する。
    /// 失敗は通知とログで報告し、例外は呼び出し元へ伝えない（選択の処理を止めないため）
    /// </summary>
    public async Task OnLocationAsync(double latitude, double longitude)
    {
        if (_detector is null || _downloader is null || _askConsentAsync is null || _busy || Mode == AreaDataDownloadMode.Never)
        {
            return;
        }

        _busy = true;
        try
        {
            IReadOnlyList<string> missing;
            try
            {
                missing = await _detector.FindMissingAsync(latitude, longitude, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Failed to check whether the area data is installed.", ex);
                return;
            }

            var targets = missing.Where(code => !_blocked.Contains(code)).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            if (Mode == AreaDataDownloadMode.Ask)
            {
                var consent = await _askConsentAsync(GetNames(targets), CancellationToken.None).ConfigureAwait(true);
                if (consent is AreaDataConsent.Decline or AreaDataConsent.DeclineAndRemember)
                {
                    _blocked.UnionWith(targets);
                    if (consent == AreaDataConsent.DeclineAndRemember)
                    {
                        Mode = AreaDataDownloadMode.Never;
                    }

                    return;
                }

                if (consent == AreaDataConsent.AcceptAndRemember)
                {
                    Mode = AreaDataDownloadMode.Auto;
                }
            }

            await RunAsync(targets).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 利用者が都道府県を選んで取得する（設定の「地域データ」）。確認と、拒否・失敗の記録は適用しない（利用者の明示の操作のため）
    /// </summary>
    public async Task DownloadAsync(string prefectureCode)
    {
        if (_downloader is null || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            _blocked.Remove(prefectureCode);
            await RunAsync([prefectureCode]).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }
    }

    private static List<string> GetNames(IEnumerable<string> codes)
        => codes.Select(code => PrefectureCatalog.TryGetName(code, out var name) ? name : code).ToList();

    private async Task RunAsync(IReadOnlyList<string> codes)
    {
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        IsDownloading = true;
        try
        {
            foreach (var code in codes)
            {
                if (!await DownloadOneAsync(code, cancellation.Token).ConfigureAwait(true))
                {
                    // 失敗・中止したら、残りの候補も取得しない（利用者の確認を超えて通信を続けない）
                    _blocked.UnionWith(codes.SkipWhile(candidate => candidate != code));
                    return;
                }
            }
        }
        finally
        {
            IsDownloading = false;
            StatusText = string.Empty;
            _downloadCancellation = null;
        }
    }

    private async Task<bool> DownloadOneAsync(string code, CancellationToken cancellationToken)
    {
        var name = GetNames([code])[0];
        StatusText = LocalizationService.Format("AreaData.Download.Progress", name, FormatBytes(0));
        var progress = _createProgress(received => StatusText = LocalizationService.Format("AreaData.Download.Progress", name, FormatBytes(received)));
        try
        {
            var info = await _downloader!.DownloadAsync(code, progress, cancellationToken).ConfigureAwait(true);
            _notify(
                LocalizationService.Format("AreaData.Download.Done", info.PrefectureName, info.CensusYear.ToString(CultureInfo.InvariantCulture)),
                InfoBarSeverity.Success);
            return true;
        }
        catch (OperationCanceledException)
        {
            _notify(LocalizationService.Format("AreaData.Download.Cancelled", name), InfoBarSeverity.Informational);
        }
        catch (AreaDataDownloadException ex)
        {
            AppLog.Error($"Failed to download the area data of {name}.", ex);
            _notify(LocalizationService.Format(GetDownloadErrorKey(ex.Error), name, ex.StatusCode is { } status ? (int)status : 0), InfoBarSeverity.Error);
        }
        catch (AreaDataFormatException ex)
        {
            AppLog.Error($"The downloaded area data of {name} cannot be imported.", ex);
            _notify(
                LocalizationService.Format("AreaData.Download.Error.Import", name, LocalizationService.GetString(SettingsPaneAreaDataSectionViewModel.GetErrorKey(ex.Error))),
                InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"Failed to save the area data of {name}.", ex);
            _notify(LocalizationService.Format("AreaData.Download.Error.Io", name), InfoBarSeverity.Error);
        }

        return false;
    }

    internal static string GetDownloadErrorKey(AreaDataDownloadError error)
        => error switch
        {
            AreaDataDownloadError.Network => "AreaData.Download.Error.Network",
            AreaDataDownloadError.HttpStatus => "AreaData.Download.Error.HttpStatus",
            AreaDataDownloadError.NotArchive => "AreaData.Download.Error.NotArchive",
            AreaDataDownloadError.TooLarge => "AreaData.Download.Error.TooLarge",
            _ => "AreaData.Download.Error.Network"
        };

    internal static string FormatBytes(long bytes)
        => bytes < 1024 * 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0} KB")
            : string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB");
}
