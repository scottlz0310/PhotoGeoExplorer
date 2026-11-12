using System;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.State;

/// <summary>
/// 地図ヘッダーに表示する撮影位置情報の項目。
/// 設定画面と地図ヘッダーの右クリックメニューの両方から切り替えるため、ペイン間で共有する
/// </summary>
internal sealed class MapHeaderDisplaySettings : BindableBase
{
    private bool _showAddress;
    private bool _showRoadName;
    private bool _showCoordinates;
    private bool _includePrefecture;

    public MapHeaderDisplaySettings()
    {
        Apply(new AppSettings());
    }

    public bool ShowAddress
    {
        get => _showAddress;
        set => SetProperty(ref _showAddress, value);
    }

    public bool ShowRoadName
    {
        get => _showRoadName;
        set => SetProperty(ref _showRoadName, value);
    }

    public bool ShowCoordinates
    {
        get => _showCoordinates;
        set => SetProperty(ref _showCoordinates, value);
    }

    public bool IncludePrefecture
    {
        get => _includePrefecture;
        set => SetProperty(ref _includePrefecture, value);
    }

    internal void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ShowAddress = settings.ShowMapHeaderAddress;
        ShowRoadName = settings.ShowMapHeaderRoadName;
        ShowCoordinates = settings.ShowMapHeaderCoordinates;
        IncludePrefecture = settings.MapHeaderIncludePrefecture;
    }

    internal void ResetToDefaults() => Apply(new AppSettings());
}
