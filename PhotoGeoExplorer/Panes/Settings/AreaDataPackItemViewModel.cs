using System;
using System.Globalization;
using System.Windows.Input;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaNames;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定画面の「地域データ」の一覧の 1 行（取り込み済みの 1 都道府県）
/// </summary>
internal sealed class AreaDataPackItemViewModel : BindableBase
{
    private readonly RelayCommand _deleteCommand;

    internal AreaDataPackItemViewModel(AreaDataPackInfo info, Func<AreaDataPackInfo, System.Threading.Tasks.Task> delete, Func<bool> canDelete)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(delete);
        ArgumentNullException.ThrowIfNull(canDelete);

        Info = info;
        _deleteCommand = new RelayCommand(() => delete(info), canDelete);
    }

    internal AreaDataPackInfo Info { get; }

    public string PrefectureName => Info.PrefectureName;

    /// <summary>調査年とサイズ（例: 2020年 ・ 5.2 MB）</summary>
    public string DetailText => LocalizationService.Format(
        "AreaData.PackDetail",
        Info.CensusYear.ToString(CultureInfo.InvariantCulture),
        FormatSize(Info.SizeBytes));

    /// <summary>削除ボタンの読み上げ名（一覧の行ごとに、どの都道府県の削除かが分かるようにする）</summary>
    public string DeleteAutomationName => LocalizationService.Format("AreaData.DeleteAutomationName", Info.PrefectureName);

    public ICommand DeleteCommand => _deleteCommand;

    internal void RaiseDeleteCanExecuteChanged() => _deleteCommand.RaiseCanExecuteChanged();

    internal static string FormatSize(long bytes)
    {
        const double Megabyte = 1024 * 1024;
        return bytes >= Megabyte
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Megabyte:F1} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (bytes + 1023) / 1024)} KB");
    }
}
