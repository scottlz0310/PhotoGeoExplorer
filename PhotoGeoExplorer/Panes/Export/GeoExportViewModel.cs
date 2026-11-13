using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.Export;
using PhotoGeoExplorer.State;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Export;

/// <summary>
/// 書き出しで利用者に尋ねること・伝えることのうち、画面（ダイアログ・ピッカー・通知）が担う部分
/// </summary>
internal interface IGeoExportUi
{
    /// <returns>CSV の保存先。キャンセルされたら null</returns>
    Task<string?> PickCsvFileAsync(string suggestedFileName);

    /// <returns>出力先のフォルダー。キャンセルされたら null</returns>
    Task<string?> PickDestinationFolderAsync();

    /// <summary>出力先に同名のファイルがあるとき、上書き・スキップ・中止を尋ねる</summary>
    Task<ConflictResolution> ResolveConflictAsync(string fileName);

    void Notify(string message, InfoBarSeverity severity);
}

/// <summary>
/// ファイル一覧で選択している写真の、地理情報つきの書き出し（台帳用の CSV、字地名別フォルダーへのコピー、リネームしてコピー）。
/// 元のファイルは変更しない。ループ・進捗・キャンセル・件数の集計をここで行い、ピッカーとダイアログは <see cref="IGeoExportUi"/> に任せる
/// </summary>
internal sealed class GeoExportViewModel : BindableBase, IDisposable
{
    private readonly WorkspaceState _workspaceState;
    private readonly PhotoGeoRecordLoader _recordLoader;
    private readonly IGeoExportService _exportService;
    private readonly IGeoExportUi _ui;
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;
    private string _progressText = string.Empty;

    public GeoExportViewModel(
        WorkspaceState workspaceState,
        PhotoGeoRecordLoader recordLoader,
        IGeoExportService exportService,
        IGeoExportUi ui)
    {
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _recordLoader = recordLoader ?? throw new ArgumentNullException(nameof(recordLoader));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        ExportCsvCommand = new RelayCommand(ExportCsvAsync, CanExport);
        CopyByAreaCommand = new RelayCommand(CopyByAreaAsync, CanExport);
        ExportRenamedCommand = new RelayCommand(ExportRenamedAsync, CanExport);
        CancelCommand = new RelayCommand(
            () =>
            {
                Cancel();
                return Task.CompletedTask;
            },
            () => IsBusy);
        _workspaceState.PropertyChanged += OnWorkspaceStatePropertyChanged;
    }

    public RelayCommand ExportCsvCommand { get; }

    public RelayCommand CopyByAreaCommand { get; }

    public RelayCommand ExportRenamedCommand { get; }

    public RelayCommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ProgressVisibility));
                RaiseCanExecuteChanged();
            }
        }
    }

    public Visibility ProgressVisibility => _isBusy ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>ステータスバーに出す、進捗の文言</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public void Cancel() => _cancellation?.Cancel();

    public void Dispose()
    {
        _workspaceState.PropertyChanged -= OnWorkspaceStatePropertyChanged;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    /// <summary>
    /// 台帳用の CSV を書き出す（ファイル名・字地名・緯度経度・撮影日時）
    /// </summary>
    public async Task ExportCsvAsync()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0 || IsBusy)
        {
            return;
        }

        var file = await _ui.PickCsvFileAsync(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PhotoGeoExplorer_{DateTime.Now:yyyyMMdd_HHmmss}.csv")).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        await RunAsync(
            paths,
            async (records, cancellationToken) =>
            {
                try
                {
                    await _exportService.WriteCsvAsync(file, records, BuildCsvLabels(), cancellationToken).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Error($"Failed to write the CSV: {file}", ex);
                    _ui.Notify(LocalizationService.Format("GeoExport.Message.CsvFailed", Path.GetFileName(file)), InfoBarSeverity.Error);
                    return;
                }

                _ui.Notify(LocalizationService.Format("GeoExport.Message.CsvDone", records.Count, Path.GetFileName(file)), InfoBarSeverity.Success);
            }).ConfigureAwait(true);
    }

    /// <summary>
    /// 選択した写真を、字地名別のフォルダー（市区町村 / 町丁・字）へコピーする
    /// </summary>
    public Task CopyByAreaAsync()
        => CopyAsync(records => ExportPlanBuilder.BuildFolderCopyPlan(records, BuildFolderNames()));

    /// <summary>
    /// 選択した写真を、「{日付}_{字名}_{連番}」の名前でコピーする
    /// </summary>
    public Task ExportRenamedAsync()
        => CopyAsync(records => ExportPlanBuilder.BuildRenamePlan(records, BuildRenameNames()));

    private async Task CopyAsync(Func<IReadOnlyList<PhotoGeoRecord>, IReadOnlyList<ExportPlanItem>> buildPlan)
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0 || IsBusy)
        {
            return;
        }

        var destination = await _ui.PickDestinationFolderAsync().ConfigureAwait(true);
        if (destination is null)
        {
            return;
        }

        await RunAsync(
            paths,
            async (records, cancellationToken) =>
            {
                var plan = buildPlan(records);
                var progress = new Progress<int>(done => ProgressText = LocalizationService.Format("GeoExport.Progress.Copying", done, plan.Count));
                ProgressText = LocalizationService.Format("GeoExport.Progress.Copying", 0, plan.Count);
                var result = await _exportService
                    .CopyAsync(plan, destination, _ui.ResolveConflictAsync, progress, cancellationToken)
                    .ConfigureAwait(true);
                Report(result);
            }).ConfigureAwait(true);
    }

    private void Report(ExportCopyResult result)
    {
        if (result.Canceled)
        {
            _ui.Notify(
                LocalizationService.Format("GeoExport.Message.CopyCanceled", result.SuccessCount, result.SkipCount, result.FailureCount),
                InfoBarSeverity.Warning);
        }
        else if (result.FailureCount > 0)
        {
            _ui.Notify(
                LocalizationService.Format("GeoExport.Message.CopyFailed", result.SuccessCount, result.SkipCount, result.FailureCount),
                InfoBarSeverity.Warning);
        }
        else
        {
            _ui.Notify(
                LocalizationService.Format("GeoExport.Message.CopyDone", result.SuccessCount, result.SkipCount),
                InfoBarSeverity.Success);
        }
    }

    // 位置情報と字地名を読み、続きの処理（CSV の書き出し・コピー）へ渡す。読み込みのキャンセルと失敗は、ここで利用者へ伝える
    private async Task RunAsync(
        List<string> paths,
        Func<IReadOnlyList<PhotoGeoRecord>, CancellationToken, Task> operation)
    {
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        ProgressText = LocalizationService.Format("GeoExport.Progress.Loading", 0, paths.Count);
        try
        {
            var loadProgress = new Progress<int>(done => ProgressText = LocalizationService.Format("GeoExport.Progress.Loading", done, paths.Count));
            var records = await _recordLoader.LoadAsync(paths, loadProgress, cancellation.Token).ConfigureAwait(true);
            await operation(records, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _ui.Notify(LocalizationService.GetString("GeoExport.Message.Canceled"), InfoBarSeverity.Warning);
        }
        catch (IOException ex)
        {
            AppLog.Error("Failed to read the area data for the export.", ex);
            _ui.Notify(LocalizationService.GetString("GeoExport.Message.AreaDataFailed"), InfoBarSeverity.Error);
        }
        finally
        {
            ProgressText = string.Empty;
            IsBusy = false;
            _cancellation = null;
            cancellation.Dispose();
        }
    }

    private List<string> GetSelectedPaths()
        => _workspaceState.SelectedPhotos?.Select(photo => photo.FilePath).ToList() ?? [];

    private bool CanExport() => !IsBusy && _workspaceState.SelectedPhotoCount > 0;

    private void OnWorkspaceStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceState.SelectedPhotoCount) or nameof(WorkspaceState.SelectedPhotos))
        {
            RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged()
    {
        ExportCsvCommand.RaiseCanExecuteChanged();
        CopyByAreaCommand.RaiseCanExecuteChanged();
        ExportRenamedCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private static ExportFolderNames BuildFolderNames() => new(
        LocalizationService.GetString("Places.Node.Water"),
        LocalizationService.GetString("Places.Node.NoAreaData"),
        LocalizationService.GetString("Places.Node.NoLocation"),
        LocalizationService.GetString("Places.Node.Unnamed"));

    private static ExportRenameNames BuildRenameNames() => new(
        LocalizationService.GetString("Places.Node.Water"),
        LocalizationService.GetString("Places.Node.NoAreaData"),
        LocalizationService.GetString("Places.Node.NoLocation"),
        LocalizationService.GetString("Places.Node.Unnamed"),
        LocalizationService.GetString("GeoExport.NoDate"));

    private static GeoCsvLabels BuildCsvLabels() => new(
        LocalizationService.GetString("GeoExport.Csv.FileName"),
        LocalizationService.GetString("GeoExport.Csv.Prefecture"),
        LocalizationService.GetString("GeoExport.Csv.City"),
        LocalizationService.GetString("GeoExport.Csv.Town"),
        LocalizationService.GetString("GeoExport.Csv.KeyCode"),
        LocalizationService.GetString("GeoExport.Csv.Latitude"),
        LocalizationService.GetString("GeoExport.Csv.Longitude"),
        LocalizationService.GetString("GeoExport.Csv.TakenAt"),
        LocalizationService.GetString("GeoExport.Csv.Status"),
        LocalizationService.GetString("GeoExport.Csv.StatusResolved"),
        LocalizationService.GetString("GeoExport.Csv.StatusWater"),
        LocalizationService.GetString("GeoExport.Csv.StatusNoAreaData"),
        LocalizationService.GetString("GeoExport.Csv.StatusNoLocation"));
}
