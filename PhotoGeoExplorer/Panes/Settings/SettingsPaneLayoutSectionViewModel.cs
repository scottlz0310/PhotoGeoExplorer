using System;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定Paneのペインレイアウトセクション用ViewModel。
/// レイアウトプリセットと各リージョンの表示ビュー選択を管理し、
/// 変更をSettingsCoordinatorへ即時反映します。
/// </summary>
internal sealed class SettingsPaneLayoutSectionViewModel : BindableBase
{
    private readonly ISettingsCoordinator _settingsCoordinator;
    private readonly Action _notifyChanged;

    private PaneLayoutPreset _paneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
    private PaneViewType _paneRegion1View = AppSettings.DefaultPaneRegion1View;
    private PaneViewType _paneRegion2View = AppSettings.DefaultPaneRegion2View;
    private PaneViewType _paneRegion3View = AppSettings.DefaultPaneRegion3View;
    private bool _suppressChangeTracking;

    internal SettingsPaneLayoutSectionViewModel(ISettingsCoordinator settingsCoordinator, Action notifyChanged)
    {
        _settingsCoordinator = settingsCoordinator ?? throw new ArgumentNullException(nameof(settingsCoordinator));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
    }

    public PaneLayoutPreset SelectedPaneLayoutPreset
    {
        get => _paneLayoutPreset;
        set
        {
            if (SetProperty(ref _paneLayoutPreset, value))
            {
                ApplyPaneLayoutPresetChange();
                OnPropertyChanged(nameof(PaneLayoutPresetIndex));
                OnPropertyChanged(nameof(Region1Label));
                OnPropertyChanged(nameof(Region2Label));
                OnPropertyChanged(nameof(Region3Label));
            }
        }
    }

    public int PaneLayoutPresetIndex
    {
        get => (int)_paneLayoutPreset;
        set => SelectedPaneLayoutPreset = FromPresetIndex(value);
    }

    public PaneViewType PaneRegion1View
    {
        get => _paneRegion1View;
        set
        {
            var previous = _paneRegion1View;
            if (SetProperty(ref _paneRegion1View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 0, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion1ViewIndex));
            }
        }
    }

    public int PaneRegion1ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion1View);
        set => PaneRegion1View = FromPaneViewIndex(value);
    }

    public PaneViewType PaneRegion2View
    {
        get => _paneRegion2View;
        set
        {
            var previous = _paneRegion2View;
            if (SetProperty(ref _paneRegion2View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 1, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion2ViewIndex));
            }
        }
    }

    public int PaneRegion2ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion2View);
        set => PaneRegion2View = FromPaneViewIndex(value);
    }

    public PaneViewType PaneRegion3View
    {
        get => _paneRegion3View;
        set
        {
            var previous = _paneRegion3View;
            if (SetProperty(ref _paneRegion3View, value))
            {
                ApplyPaneRegionViewChange(regionIndex: 2, previousValue: previous);
                OnPropertyChanged(nameof(PaneRegion3ViewIndex));
            }
        }
    }

    public int PaneRegion3ViewIndex
    {
        get => ToPaneViewIndex(_paneRegion3View);
        set => PaneRegion3View = FromPaneViewIndex(value);
    }

    public string Region1Label => LocalizationService.GetString(GetRegionLabelKey(_paneLayoutPreset, regionIndex: 0));

    public string Region2Label => LocalizationService.GetString(GetRegionLabelKey(_paneLayoutPreset, regionIndex: 1));

    public string Region3Label => LocalizationService.GetString(GetRegionLabelKey(_paneLayoutPreset, regionIndex: 2));

    /// <summary>
    /// SettingsCoordinatorの現在値でプロパティを更新します。変更追跡は発火しません。
    /// </summary>
    internal void RefreshFromCoordinator()
    {
        _suppressChangeTracking = true;
        try
        {
            SelectedPaneLayoutPreset = _settingsCoordinator.PaneLayoutPreset;
            PaneRegion1View = _settingsCoordinator.PaneRegion1View;
            PaneRegion2View = _settingsCoordinator.PaneRegion2View;
            PaneRegion3View = _settingsCoordinator.PaneRegion3View;
        }
        finally
        {
            _suppressChangeTracking = false;
        }
    }

    /// <summary>
    /// 既定レイアウトへ戻します。変更追跡は発火しません（呼び出し側で保存します）。
    /// </summary>
    internal void ResetToDefaults()
    {
        _suppressChangeTracking = true;
        try
        {
            SelectedPaneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
            PaneRegion1View = AppSettings.DefaultPaneRegion1View;
            PaneRegion2View = AppSettings.DefaultPaneRegion2View;
            PaneRegion3View = AppSettings.DefaultPaneRegion3View;
        }
        finally
        {
            _suppressChangeTracking = false;
        }
    }

    /// <summary>
    /// 現在のレイアウト選択をSettingsCoordinatorへ反映します。
    /// </summary>
    internal void ApplyToCoordinator()
    {
        _settingsCoordinator.ChangePaneLayout(
            SelectedPaneLayoutPreset,
            PaneRegion1View,
            PaneRegion2View,
            PaneRegion3View);
    }

    private void ApplyPaneLayoutPresetChange()
    {
        if (_suppressChangeTracking)
        {
            return;
        }

        ApplyPaneLayoutChange();
    }

    private void ApplyPaneRegionViewChange(int regionIndex, PaneViewType previousValue)
    {
        if (_suppressChangeTracking)
        {
            return;
        }

        var selected = regionIndex switch
        {
            0 => PaneRegion1View,
            1 => PaneRegion2View,
            _ => PaneRegion3View
        };

        var duplicateRegionIndex = regionIndex switch
        {
            0 when PaneRegion2View == selected => 1,
            0 when PaneRegion3View == selected => 2,
            1 when PaneRegion1View == selected => 0,
            1 when PaneRegion3View == selected => 2,
            2 when PaneRegion1View == selected => 0,
            2 when PaneRegion2View == selected => 1,
            _ => -1
        };

        if (duplicateRegionIndex >= 0)
        {
            ReplacePaneRegionView(duplicateRegionIndex, previousValue);
        }

        ApplyPaneLayoutChange();
    }

    private void ApplyPaneLayoutChange()
    {
        ApplyToCoordinator();
        _notifyChanged();
    }

    private void ReplacePaneRegionView(int regionIndex, PaneViewType value)
    {
        _suppressChangeTracking = true;
        try
        {
            switch (regionIndex)
            {
                case 0:
                    PaneRegion1View = value;
                    break;
                case 1:
                    PaneRegion2View = value;
                    break;
                default:
                    PaneRegion3View = value;
                    break;
            }
        }
        finally
        {
            _suppressChangeTracking = false;
        }
    }

    internal static string GetRegionLabelKey(PaneLayoutPreset preset, int regionIndex)
    {
        return preset switch
        {
            PaneLayoutPreset.LeftCenterRight => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionLeft",
                1 => "SettingsPaneLayoutRegionCenter",
                _ => "SettingsPaneLayoutRegionRight"
            },
            PaneLayoutPreset.LeftSplitAndRight => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionTopLeft",
                1 => "SettingsPaneLayoutRegionBottomLeft",
                _ => "SettingsPaneLayoutRegionRight"
            },
            _ => regionIndex switch
            {
                0 => "SettingsPaneLayoutRegionLeft",
                1 => "SettingsPaneLayoutRegionTopRight",
                _ => "SettingsPaneLayoutRegionBottomRight"
            }
        };
    }

    internal static PaneLayoutPreset FromPresetIndex(int value)
    {
        return value switch
        {
            0 => PaneLayoutPreset.LeftCenterRight,
            2 => PaneLayoutPreset.LeftSplitAndRight,
            _ => PaneLayoutPreset.LeftAndRightSplit
        };
    }

    internal static int ToPaneViewIndex(PaneViewType value)
    {
        return value switch
        {
            PaneViewType.Preview => 1,
            PaneViewType.Map => 2,
            _ => 0
        };
    }

    internal static PaneViewType FromPaneViewIndex(int value)
    {
        return value switch
        {
            1 => PaneViewType.Preview,
            2 => PaneViewType.Map,
            _ => PaneViewType.File
        };
    }
}
