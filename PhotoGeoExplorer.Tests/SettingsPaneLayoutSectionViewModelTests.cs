using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Settings;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class SettingsPaneLayoutSectionViewModelTests
{
    // PaneViewType / PaneLayoutPreset は internal のため public テストメソッドの引数に使えず（CS0051）、int で受けてキャストする
    [Theory]
    [InlineData((int)PaneViewType.File, 0)]
    [InlineData((int)PaneViewType.Preview, 1)]
    [InlineData((int)PaneViewType.Map, 2)]
    [InlineData(99, 0)]
    public void ToPaneViewIndexMapsViewTypes(int value, int expected)
    {
        Assert.Equal(expected, SettingsPaneLayoutSectionViewModel.ToPaneViewIndex((PaneViewType)value));
    }

    [Theory]
    [InlineData(0, (int)PaneViewType.File)]
    [InlineData(1, (int)PaneViewType.Preview)]
    [InlineData(2, (int)PaneViewType.Map)]
    [InlineData(-1, (int)PaneViewType.File)]
    [InlineData(99, (int)PaneViewType.File)]
    public void FromPaneViewIndexMapsIndexes(int value, int expected)
    {
        Assert.Equal((PaneViewType)expected, SettingsPaneLayoutSectionViewModel.FromPaneViewIndex(value));
    }

    [Theory]
    [InlineData(0, (int)PaneLayoutPreset.LeftCenterRight)]
    [InlineData(1, (int)PaneLayoutPreset.LeftAndRightSplit)]
    [InlineData(2, (int)PaneLayoutPreset.LeftSplitAndRight)]
    [InlineData(-1, (int)PaneLayoutPreset.LeftAndRightSplit)]
    [InlineData(99, (int)PaneLayoutPreset.LeftAndRightSplit)]
    public void FromPresetIndexMapsIndexes(int value, int expected)
    {
        Assert.Equal((PaneLayoutPreset)expected, SettingsPaneLayoutSectionViewModel.FromPresetIndex(value));
    }

    [Theory]
    [InlineData((int)PaneLayoutPreset.LeftCenterRight, 0, "SettingsPaneLayoutRegionLeft")]
    [InlineData((int)PaneLayoutPreset.LeftCenterRight, 1, "SettingsPaneLayoutRegionCenter")]
    [InlineData((int)PaneLayoutPreset.LeftCenterRight, 2, "SettingsPaneLayoutRegionRight")]
    [InlineData((int)PaneLayoutPreset.LeftSplitAndRight, 0, "SettingsPaneLayoutRegionTopLeft")]
    [InlineData((int)PaneLayoutPreset.LeftSplitAndRight, 1, "SettingsPaneLayoutRegionBottomLeft")]
    [InlineData((int)PaneLayoutPreset.LeftSplitAndRight, 2, "SettingsPaneLayoutRegionRight")]
    [InlineData((int)PaneLayoutPreset.LeftAndRightSplit, 0, "SettingsPaneLayoutRegionLeft")]
    [InlineData((int)PaneLayoutPreset.LeftAndRightSplit, 1, "SettingsPaneLayoutRegionTopRight")]
    [InlineData((int)PaneLayoutPreset.LeftAndRightSplit, 2, "SettingsPaneLayoutRegionBottomRight")]
    public void GetRegionLabelKeyFollowsPresetAndRegion(int preset, int regionIndex, string expectedKey)
    {
        Assert.Equal(expectedKey, SettingsPaneLayoutSectionViewModel.GetRegionLabelKey((PaneLayoutPreset)preset, regionIndex));
    }

    [Fact]
    public void RegionViewChangeSwapsDuplicateAndAppliesToCoordinator()
    {
        using var coordinator = new StubSettingsCoordinator();
        var notifyCount = 0;
        var section = new SettingsPaneLayoutSectionViewModel(coordinator, () => notifyCount++);
        section.RefreshFromCoordinator();

        section.PaneRegion1View = PaneViewType.Map;

        Assert.Equal(PaneViewType.Map, section.PaneRegion1View);
        Assert.Equal(PaneViewType.Preview, section.PaneRegion2View);
        Assert.Equal(PaneViewType.File, section.PaneRegion3View);
        Assert.Equal(PaneViewType.Map, coordinator.PaneRegion1View);
        Assert.Equal(PaneViewType.File, coordinator.PaneRegion3View);
        Assert.Equal(1, notifyCount);
        Assert.Equal(1, coordinator.ChangePaneLayoutCallCount);
    }

    [Fact]
    public void RefreshFromCoordinatorDoesNotNotifyOrApply()
    {
        using var coordinator = new StubSettingsCoordinator
        {
            PaneLayoutPreset = PaneLayoutPreset.LeftCenterRight,
            PaneRegion1View = PaneViewType.Preview,
            PaneRegion2View = PaneViewType.File,
            PaneRegion3View = PaneViewType.Map
        };
        var notifyCount = 0;
        var section = new SettingsPaneLayoutSectionViewModel(coordinator, () => notifyCount++);

        section.RefreshFromCoordinator();

        Assert.Equal(PaneLayoutPreset.LeftCenterRight, section.SelectedPaneLayoutPreset);
        Assert.Equal(PaneViewType.Preview, section.PaneRegion1View);
        Assert.Equal(PaneViewType.File, section.PaneRegion2View);
        Assert.Equal(PaneViewType.Map, section.PaneRegion3View);
        Assert.Equal(0, notifyCount);
        Assert.Equal(0, coordinator.ChangePaneLayoutCallCount);
    }

    [Fact]
    public void ResetToDefaultsRestoresDefaultsWithoutNotifying()
    {
        using var coordinator = new StubSettingsCoordinator();
        var notifyCount = 0;
        var section = new SettingsPaneLayoutSectionViewModel(coordinator, () => notifyCount++);
        section.RefreshFromCoordinator();
        section.PaneLayoutPresetIndex = 0;
        notifyCount = 0;
        coordinator.ResetChangePaneLayoutCallCount();

        section.ResetToDefaults();

        Assert.Equal(AppSettings.DefaultPaneLayoutPreset, section.SelectedPaneLayoutPreset);
        Assert.Equal(AppSettings.DefaultPaneRegion1View, section.PaneRegion1View);
        Assert.Equal(AppSettings.DefaultPaneRegion2View, section.PaneRegion2View);
        Assert.Equal(AppSettings.DefaultPaneRegion3View, section.PaneRegion3View);
        Assert.Equal(0, notifyCount);
        Assert.Equal(0, coordinator.ChangePaneLayoutCallCount);
    }

    [Fact]
    public void PresetChangeRaisesRegionLabelNotifications()
    {
        using var coordinator = new StubSettingsCoordinator();
        var section = new SettingsPaneLayoutSectionViewModel(coordinator, () => { });
        section.RefreshFromCoordinator();

        var changedProperties = new System.Collections.Generic.List<string?>();
        section.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        section.PaneLayoutPresetIndex = 0;

        Assert.Contains(nameof(SettingsPaneLayoutSectionViewModel.Region1Label), changedProperties);
        Assert.Contains(nameof(SettingsPaneLayoutSectionViewModel.Region2Label), changedProperties);
        Assert.Contains(nameof(SettingsPaneLayoutSectionViewModel.Region3Label), changedProperties);
        Assert.Contains(nameof(SettingsPaneLayoutSectionViewModel.PaneLayoutPresetIndex), changedProperties);
    }

    private sealed class StubSettingsCoordinator : ISettingsCoordinator
    {
        public bool SettingsFileExistsAtStartup => false;

        public string? LanguageOverride => null;

        public string? ExternalContentBaseUrl => null;

        public bool ShowQuickStartOnStartup { get; set; }

        public PaneLayoutPreset PaneLayoutPreset { get; set; } = AppSettings.DefaultPaneLayoutPreset;

        public PaneViewType PaneRegion1View { get; set; } = AppSettings.DefaultPaneRegion1View;

        public PaneViewType PaneRegion2View { get; set; } = AppSettings.DefaultPaneRegion2View;

        public PaneViewType PaneRegion3View { get; set; } = AppSettings.DefaultPaneRegion3View;

        public int ChangePaneLayoutCallCount { get; private set; }

        public event EventHandler<PaneLayoutChangedEventArgs>? PaneLayoutChanged;

        public void ResetChangePaneLayoutCallCount()
        {
            ChangePaneLayoutCallCount = 0;
        }

        public Task LoadAsync() => Task.CompletedTask;

        public void ScheduleSave()
        {
        }

        public Task SaveAsync() => Task.CompletedTask;

        public Task ChangeLanguageAsync(string? languageTag, bool showRestartPrompt) => Task.CompletedTask;

        public void ChangeTheme(ThemePreference preference)
        {
        }

        public void ChangeMapZoomLevel(int level)
        {
        }

        public void ChangeMapTileSource(MapTileSourceType sourceType)
        {
        }

        public void ChangePaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
        {
            ChangePaneLayoutCallCount++;
            PaneLayoutPreset = preset;
            PaneRegion1View = region1View;
            PaneRegion2View = region2View;
            PaneRegion3View = region3View;
            PaneLayoutChanged?.Invoke(this, new PaneLayoutChangedEventArgs(preset, region1View, region2View, region3View));
        }

        public Task ExportSettingsAsync() => Task.CompletedTask;

        public Task ImportSettingsAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
