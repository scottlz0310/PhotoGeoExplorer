using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaNames;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定 Pane の「地域データ（字地名）」セクション。
/// 利用者が e-Stat からダウンロードした ZIP を取り込み、取り込み済みの都道府県を一覧・削除する。
/// 変更は設定ファイルではなく、地域データのフォルダーへ直接反映する
/// </summary>
internal sealed class SettingsPaneAreaDataSectionViewModel : BindableBase
{
    private readonly IAreaDataStore _store;
    private readonly Func<CancellationToken, Task<string?>> _pickArchiveAsync;
    private readonly RelayCommand _addCommand;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    /// <param name="pickArchiveAsync">取り込む ZIP のパスを利用者に選んでもらう。キャンセルされたら null</param>
    internal SettingsPaneAreaDataSectionViewModel(
        IAreaDataStore store,
        Func<CancellationToken, Task<string?>> pickArchiveAsync)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pickArchiveAsync = pickArchiveAsync ?? throw new ArgumentNullException(nameof(pickArchiveAsync));
        _addCommand = new RelayCommand(() => AddAsync(), () => !IsBusy);
        Packs.CollectionChanged += OnPacksChanged;
    }

    public ObservableCollection<AreaDataPackItemViewModel> Packs { get; } = [];

    public ICommand AddCommand => _addCommand;

    public Visibility EmptyVisibility => Packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            _addCommand.RaiseCanExecuteChanged();
            foreach (var pack in Packs)
            {
                pack.RaiseDeleteCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    internal async Task RefreshAsync()
    {
        try
        {
            var installed = await _store.ListAsync(CancellationToken.None).ConfigureAwait(true);
            Packs.Clear();
            foreach (var info in installed)
            {
                Packs.Add(CreateItem(info));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to list the imported area data.", ex);
            StatusMessage = LocalizationService.GetString("AreaData.Error.Io");
        }
    }

    internal async Task AddAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var archivePath = await _pickArchiveAsync(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrEmpty(archivePath))
            {
                return;
            }

            StatusMessage = LocalizationService.GetString("AreaData.Importing");
            var info = await _store.ImportAsync(archivePath, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = LocalizationService.Format(
                "AreaData.Imported",
                info.PrefectureName,
                info.CensusYear.ToString(CultureInfo.InvariantCulture));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (AreaDataFormatException ex)
        {
            AppLog.Error("The selected archive cannot be imported as area data.", ex);
            StatusMessage = LocalizationService.GetString(GetErrorKey(ex.Error));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to import the area data.", ex);
            StatusMessage = LocalizationService.GetString("AreaData.Error.Io");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal static string GetErrorKey(AreaDataFormatError error)
        => error switch
        {
            AreaDataFormatError.MissingProjection => "AreaData.Error.MissingProjection",
            AreaDataFormatError.UnsupportedProjection => "AreaData.Error.UnsupportedProjection",
            AreaDataFormatError.MissingAttribute => "AreaData.Error.MissingAttribute",
            AreaDataFormatError.NoRegions => "AreaData.Error.NoRegions",
            AreaDataFormatError.InvalidKeyCode => "AreaData.Error.InvalidKeyCode",
            AreaDataFormatError.MixedPrefectures => "AreaData.Error.MixedPrefectures",
            AreaDataFormatError.InvalidArchive => "AreaData.Error.InvalidArchive",
            AreaDataFormatError.UnsupportedFileName => "AreaData.Error.UnsupportedFileName",
            AreaDataFormatError.TooLarge => "AreaData.Error.TooLarge",
            AreaDataFormatError.CorruptData => "AreaData.Error.CorruptData",
            _ => "AreaData.Error.Unspecified"
        };

    private AreaDataPackItemViewModel CreateItem(AreaDataPackInfo info)
        => new(info, DeleteAsync, () => !IsBusy);

    internal async Task DeleteAsync(AreaDataPackInfo info)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _store.DeleteAsync(info.PrefectureCode, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = LocalizationService.Format("AreaData.Deleted", info.PrefectureName);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Failed to delete the area data.", ex);
            StatusMessage = LocalizationService.GetString("AreaData.Error.Io");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPacksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(EmptyVisibility));
}
