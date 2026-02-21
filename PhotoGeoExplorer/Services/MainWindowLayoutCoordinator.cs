using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Preview;

namespace PhotoGeoExplorer.Services;

internal sealed class MainWindowLayoutCoordinator
{
    internal readonly record struct TogglePreviewMaximizePlan(
        bool HasChanges,
        bool ShouldStoreCurrentLayout,
        bool PreviewMaximized,
        GridLength StoredFileBrowserWidth,
        GridLength StoredSplitterWidth,
        GridLength StoredDetailWidth,
        GridLength FileBrowserWidth,
        GridLength SplitterWidth,
        GridLength DetailWidth,
        bool ShowPreviewOnly);

    private readonly PaneLayoutHostService _paneLayoutHostService;
    private readonly PreviewPaneViewControl _previewPaneControl;
    private readonly ColumnDefinition _fileBrowserColumn;
    private readonly ColumnDefinition _splitterColumn;
    private readonly ColumnDefinition _detailColumn;
    private readonly Grid _mainContentGrid;
    private readonly Grid _rightVerticalSplitHost;
    private readonly RowDefinition _previewRow;
    private readonly RowDefinition _mapRow;
    private readonly RowDefinition _mapSplitterRow;
    private readonly Grid _leftVerticalSplitHost;
    private readonly RowDefinition _leftTopRow;
    private readonly RowDefinition _leftBottomRow;
    private readonly RowDefinition _leftSplitterRow;
    private readonly Grid _rightHorizontalSplitHost;
    private readonly ColumnDefinition _rightLeftColumn;
    private readonly ColumnDefinition _rightRightColumn;
    private readonly ColumnDefinition _rightSplitterColumn;
    private bool _layoutStored;
    private bool _previewMaximized;
    private PaneLayoutPreset _currentPaneLayoutPreset = AppSettings.DefaultPaneLayoutPreset;
    private PaneViewType _currentPaneRegion1View = AppSettings.DefaultPaneRegion1View;
    private PaneViewType _currentPaneRegion2View = AppSettings.DefaultPaneRegion2View;
    private PaneViewType _currentPaneRegion3View = AppSettings.DefaultPaneRegion3View;
    private GridLength _storedDetailWidth;
    private GridLength _storedFileBrowserWidth;
    private GridLength _storedSplitterWidth;

    public MainWindowLayoutCoordinator(
        PaneLayoutHostService paneLayoutHostService,
        PreviewPaneViewControl previewPaneControl,
        ColumnDefinition fileBrowserColumn,
        ColumnDefinition splitterColumn,
        ColumnDefinition detailColumn,
        Grid mainContentGrid,
        Grid rightVerticalSplitHost,
        RowDefinition previewRow,
        RowDefinition mapRow,
        RowDefinition mapSplitterRow,
        Grid leftVerticalSplitHost,
        RowDefinition leftTopRow,
        RowDefinition leftBottomRow,
        RowDefinition leftSplitterRow,
        Grid rightHorizontalSplitHost,
        ColumnDefinition rightLeftColumn,
        ColumnDefinition rightRightColumn,
        ColumnDefinition rightSplitterColumn)
    {
        _paneLayoutHostService = paneLayoutHostService ?? throw new ArgumentNullException(nameof(paneLayoutHostService));
        _previewPaneControl = previewPaneControl ?? throw new ArgumentNullException(nameof(previewPaneControl));
        _fileBrowserColumn = fileBrowserColumn ?? throw new ArgumentNullException(nameof(fileBrowserColumn));
        _splitterColumn = splitterColumn ?? throw new ArgumentNullException(nameof(splitterColumn));
        _detailColumn = detailColumn ?? throw new ArgumentNullException(nameof(detailColumn));
        _mainContentGrid = mainContentGrid ?? throw new ArgumentNullException(nameof(mainContentGrid));
        _rightVerticalSplitHost = rightVerticalSplitHost ?? throw new ArgumentNullException(nameof(rightVerticalSplitHost));
        _previewRow = previewRow ?? throw new ArgumentNullException(nameof(previewRow));
        _mapRow = mapRow ?? throw new ArgumentNullException(nameof(mapRow));
        _mapSplitterRow = mapSplitterRow ?? throw new ArgumentNullException(nameof(mapSplitterRow));
        _leftVerticalSplitHost = leftVerticalSplitHost ?? throw new ArgumentNullException(nameof(leftVerticalSplitHost));
        _leftTopRow = leftTopRow ?? throw new ArgumentNullException(nameof(leftTopRow));
        _leftBottomRow = leftBottomRow ?? throw new ArgumentNullException(nameof(leftBottomRow));
        _leftSplitterRow = leftSplitterRow ?? throw new ArgumentNullException(nameof(leftSplitterRow));
        _rightHorizontalSplitHost = rightHorizontalSplitHost ?? throw new ArgumentNullException(nameof(rightHorizontalSplitHost));
        _rightLeftColumn = rightLeftColumn ?? throw new ArgumentNullException(nameof(rightLeftColumn));
        _rightRightColumn = rightRightColumn ?? throw new ArgumentNullException(nameof(rightRightColumn));
        _rightSplitterColumn = rightSplitterColumn ?? throw new ArgumentNullException(nameof(rightSplitterColumn));
    }

    public void ApplyPaneLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
    {
        _currentPaneLayoutPreset = preset;
        _currentPaneRegion1View = region1View;
        _currentPaneRegion2View = region2View;
        _currentPaneRegion3View = region3View;

        if (!ShouldApplyPaneLayout(_previewMaximized))
        {
            return;
        }

        _paneLayoutHostService.ApplyLayout(preset, region1View, region2View, region3View);
        _previewPaneControl.RequestRefitIfNeeded();
    }

    public void TogglePreviewMaximize(bool maximize)
    {
        if (maximize == _previewMaximized)
        {
            return;
        }

        var plan = ComputeTogglePreviewMaximizePlan(
            maximize,
            _previewMaximized,
            _layoutStored,
            _fileBrowserColumn.Width,
            _splitterColumn.Width,
            _detailColumn.Width,
            _storedFileBrowserWidth,
            _storedSplitterWidth,
            _storedDetailWidth);
        if (plan.ShouldStoreCurrentLayout)
        {
            _storedFileBrowserWidth = plan.StoredFileBrowserWidth;
            _storedSplitterWidth = plan.StoredSplitterWidth;
            _storedDetailWidth = plan.StoredDetailWidth;
            _layoutStored = true;
        }

        _previewMaximized = plan.PreviewMaximized;
        _fileBrowserColumn.Width = plan.FileBrowserWidth;
        _splitterColumn.Width = plan.SplitterWidth;
        _detailColumn.Width = plan.DetailWidth;
        if (plan.ShowPreviewOnly)
        {
            _paneLayoutHostService.ShowPreviewOnly();
        }
        else
        {
            ApplyPaneLayout(
                _currentPaneLayoutPreset,
                _currentPaneRegion1View,
                _currentPaneRegion2View,
                _currentPaneRegion3View);
        }

        _previewPaneControl.SetFitToWindow();
    }

    public void ApplyMainSplitterDelta(double horizontalChange)
    {
        if (_previewMaximized)
        {
            return;
        }

        if (!TryComputeSplitterLengths(
                skipComputation: false,
                hostVisibility: Visibility.Visible,
                containerLength: _mainContentGrid.ActualWidth,
                splitterLength: _splitterColumn.ActualWidth,
                currentPrimaryLength: _fileBrowserColumn.ActualWidth,
                deltaLength: horizontalChange,
                minPrimaryLength: 220,
                minSecondaryLength: 320,
                out var fileBrowserWidth,
                out var detailWidth))
        {
            return;
        }

        _fileBrowserColumn.Width = fileBrowserWidth;
        _detailColumn.Width = detailWidth;
    }

    public void ApplyMapSplitterDelta(double verticalChange)
    {
        if (!TryComputeSplitterLengths(
                skipComputation: false,
                hostVisibility: _rightVerticalSplitHost.Visibility,
                containerLength: _rightVerticalSplitHost.ActualHeight,
                splitterLength: _mapSplitterRow.ActualHeight,
                currentPrimaryLength: _previewRow.ActualHeight,
                deltaLength: verticalChange,
                minPrimaryLength: 200,
                minSecondaryLength: 200,
                out var previewHeight,
                out var mapHeight))
        {
            return;
        }

        _previewRow.Height = previewHeight;
        _mapRow.Height = mapHeight;
    }

    public void ApplyLeftSplitterDelta(double verticalChange)
    {
        if (!TryComputeSplitterLengths(
                skipComputation: false,
                hostVisibility: _leftVerticalSplitHost.Visibility,
                containerLength: _leftVerticalSplitHost.ActualHeight,
                splitterLength: _leftSplitterRow.ActualHeight,
                currentPrimaryLength: _leftTopRow.ActualHeight,
                deltaLength: verticalChange,
                minPrimaryLength: 200,
                minSecondaryLength: 200,
                out var leftTopHeight,
                out var leftBottomHeight))
        {
            return;
        }

        _leftTopRow.Height = leftTopHeight;
        _leftBottomRow.Height = leftBottomHeight;
    }

    public void ApplyRightHorizontalSplitterDelta(double horizontalChange)
    {
        if (!TryComputeSplitterLengths(
                skipComputation: false,
                hostVisibility: _rightHorizontalSplitHost.Visibility,
                containerLength: _rightHorizontalSplitHost.ActualWidth,
                splitterLength: _rightSplitterColumn.ActualWidth,
                currentPrimaryLength: _rightLeftColumn.ActualWidth,
                deltaLength: horizontalChange,
                minPrimaryLength: 220,
                minSecondaryLength: 220,
                out var rightLeftWidth,
                out var rightRightWidth))
        {
            return;
        }

        _rightLeftColumn.Width = rightLeftWidth;
        _rightRightColumn.Width = rightRightWidth;
    }

    internal static bool ShouldApplyPaneLayout(bool previewMaximized)
    {
        return !previewMaximized;
    }

    internal static TogglePreviewMaximizePlan ComputeTogglePreviewMaximizePlan(
        bool maximize,
        bool previewMaximized,
        bool layoutStored,
        GridLength currentFileBrowserWidth,
        GridLength currentSplitterWidth,
        GridLength currentDetailWidth,
        GridLength storedFileBrowserWidth,
        GridLength storedSplitterWidth,
        GridLength storedDetailWidth)
    {
        if (maximize == previewMaximized)
        {
            return new TogglePreviewMaximizePlan(
                HasChanges: false,
                ShouldStoreCurrentLayout: false,
                PreviewMaximized: previewMaximized,
                StoredFileBrowserWidth: storedFileBrowserWidth,
                StoredSplitterWidth: storedSplitterWidth,
                StoredDetailWidth: storedDetailWidth,
                FileBrowserWidth: currentFileBrowserWidth,
                SplitterWidth: currentSplitterWidth,
                DetailWidth: currentDetailWidth,
                ShowPreviewOnly: maximize);
        }

        if (maximize)
        {
            var shouldStoreCurrentLayout = !layoutStored;
            return new TogglePreviewMaximizePlan(
                HasChanges: true,
                ShouldStoreCurrentLayout: shouldStoreCurrentLayout,
                PreviewMaximized: true,
                StoredFileBrowserWidth: shouldStoreCurrentLayout ? currentFileBrowserWidth : storedFileBrowserWidth,
                StoredSplitterWidth: shouldStoreCurrentLayout ? currentSplitterWidth : storedSplitterWidth,
                StoredDetailWidth: shouldStoreCurrentLayout ? currentDetailWidth : storedDetailWidth,
                FileBrowserWidth: new GridLength(0),
                SplitterWidth: new GridLength(0),
                DetailWidth: new GridLength(1, GridUnitType.Star),
                ShowPreviewOnly: true);
        }

        return new TogglePreviewMaximizePlan(
            HasChanges: true,
            ShouldStoreCurrentLayout: false,
            PreviewMaximized: false,
            StoredFileBrowserWidth: storedFileBrowserWidth,
            StoredSplitterWidth: storedSplitterWidth,
            StoredDetailWidth: storedDetailWidth,
            FileBrowserWidth: ResolveStoredLength(storedFileBrowserWidth, new GridLength(2, GridUnitType.Star)),
            SplitterWidth: ResolveStoredLength(storedSplitterWidth, GridLength.Auto),
            DetailWidth: ResolveStoredLength(storedDetailWidth, new GridLength(3, GridUnitType.Star)),
            ShowPreviewOnly: false);
    }

    internal static bool TryComputeHostSplitterPrimaryLength(
        bool skipComputation,
        Visibility hostVisibility,
        double containerLength,
        double splitterLength,
        double currentPrimaryLength,
        double deltaLength,
        double minPrimaryLength,
        double minSecondaryLength,
        out double clampedPrimaryLength)
    {
        if (skipComputation || hostVisibility != Visibility.Visible)
        {
            clampedPrimaryLength = 0;
            return false;
        }

        return TryComputePanePrimaryLength(
            containerLength,
            splitterLength,
            currentPrimaryLength,
            deltaLength,
            minPrimaryLength,
            minSecondaryLength,
            out clampedPrimaryLength);
    }

    internal static bool TryComputeSplitterLengths(
        bool skipComputation,
        Visibility hostVisibility,
        double containerLength,
        double splitterLength,
        double currentPrimaryLength,
        double deltaLength,
        double minPrimaryLength,
        double minSecondaryLength,
        out GridLength primaryLength,
        out GridLength secondaryLength)
    {
        if (!TryComputeHostSplitterPrimaryLength(
                skipComputation,
                hostVisibility,
                containerLength,
                splitterLength,
                currentPrimaryLength,
                deltaLength,
                minPrimaryLength,
                minSecondaryLength,
                out var clampedPrimaryLength))
        {
            primaryLength = default;
            secondaryLength = default;
            return false;
        }

        primaryLength = new GridLength(clampedPrimaryLength, GridUnitType.Pixel);
        secondaryLength = new GridLength(1, GridUnitType.Star);
        return true;
    }

    internal static GridLength ResolveStoredLength(GridLength storedLength, GridLength fallbackLength)
    {
        return storedLength.Value > 0 ? storedLength : fallbackLength;
    }

    internal static bool TryComputePanePrimaryLength(
        double containerLength,
        double splitterLength,
        double currentPrimaryLength,
        double deltaLength,
        double minPrimaryLength,
        double minSecondaryLength,
        out double clampedPrimaryLength)
    {
        var availableLength = containerLength - splitterLength;
        if (availableLength <= 0)
        {
            clampedPrimaryLength = 0;
            return false;
        }

        var targetPrimaryLength = currentPrimaryLength + deltaLength;
        var maxPrimaryLength = availableLength - minSecondaryLength;
        if (maxPrimaryLength < minPrimaryLength)
        {
            clampedPrimaryLength = Math.Clamp(targetPrimaryLength, 0, availableLength);
            return true;
        }

        clampedPrimaryLength = Math.Clamp(targetPrimaryLength, minPrimaryLength, maxPrimaryLength);
        return true;
    }
}
