using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoGeoExplorer.Models;

namespace PhotoGeoExplorer.Services;

internal sealed class PaneLayoutHostService
{
    internal enum PaneRegionHostSlot
    {
        LeftSingle,
        LeftTop,
        LeftBottom,
        RightSingle,
        RightTop,
        RightBottom,
        RightHorizontalLeft,
        RightHorizontalRight
    }

    internal enum PaneContentKind
    {
        File,
        Preview,
        Map
    }

    internal readonly record struct PaneLayoutVisibilityState(
        Visibility LeftSingleHost,
        Visibility LeftVerticalSplitHost,
        Visibility RightSingleHost,
        Visibility RightVerticalSplitHost,
        Visibility RightHorizontalSplitHost,
        Visibility LeftRowSplitter,
        Visibility MapRowSplitter,
        Visibility RightColumnSplitter,
        Visibility MapPane);

    internal readonly record struct PaneRegionHostLayout(
        PaneRegionHostSlot Region1,
        PaneRegionHostSlot Region2,
        PaneRegionHostSlot Region3);

    internal readonly record struct PaneLayoutPlacementPlan(
        PaneLayoutVisibilityState VisibilityState,
        PaneRegionHostLayout HostLayout,
        PaneContentKind Region1Content,
        PaneContentKind Region2Content,
        PaneContentKind Region3Content);

    private readonly Grid _fileBrowserPane;
    private readonly Grid _detailPane;
    private readonly Thumb _mainSplitter;
    private readonly Grid _leftSingleHost;
    private readonly Grid _leftVerticalSplitHost;
    private readonly Grid _leftTopHost;
    private readonly Grid _leftBottomHost;
    private readonly Thumb _leftRowSplitter;
    private readonly Grid _rightSingleHost;
    private readonly Grid _rightVerticalSplitHost;
    private readonly Grid _rightTopHost;
    private readonly Grid _rightBottomHost;
    private readonly Grid _mapPane;
    private readonly Thumb _mapRowSplitter;
    private readonly Grid _rightHorizontalSplitHost;
    private readonly Grid _rightHorizontalLeftHost;
    private readonly Grid _rightHorizontalRightHost;
    private readonly Thumb _rightColumnSplitter;
    private readonly Grid _paneStagingArea;
    private readonly FrameworkElement _fileBrowserPaneControl;
    private readonly FrameworkElement _previewPaneControl;
    private readonly FrameworkElement _mapPaneControl;

    public PaneLayoutHostService(
        Grid fileBrowserPane,
        Grid detailPane,
        Thumb mainSplitter,
        Grid leftSingleHost,
        Grid leftVerticalSplitHost,
        Grid leftTopHost,
        Grid leftBottomHost,
        Thumb leftRowSplitter,
        Grid rightSingleHost,
        Grid rightVerticalSplitHost,
        Grid rightTopHost,
        Grid rightBottomHost,
        Grid mapPane,
        Thumb mapRowSplitter,
        Grid rightHorizontalSplitHost,
        Grid rightHorizontalLeftHost,
        Grid rightHorizontalRightHost,
        Thumb rightColumnSplitter,
        Grid paneStagingArea,
        FrameworkElement fileBrowserPaneControl,
        FrameworkElement previewPaneControl,
        FrameworkElement mapPaneControl)
    {
        _fileBrowserPane = fileBrowserPane ?? throw new ArgumentNullException(nameof(fileBrowserPane));
        _detailPane = detailPane ?? throw new ArgumentNullException(nameof(detailPane));
        _mainSplitter = mainSplitter ?? throw new ArgumentNullException(nameof(mainSplitter));
        _leftSingleHost = leftSingleHost ?? throw new ArgumentNullException(nameof(leftSingleHost));
        _leftVerticalSplitHost = leftVerticalSplitHost ?? throw new ArgumentNullException(nameof(leftVerticalSplitHost));
        _leftTopHost = leftTopHost ?? throw new ArgumentNullException(nameof(leftTopHost));
        _leftBottomHost = leftBottomHost ?? throw new ArgumentNullException(nameof(leftBottomHost));
        _leftRowSplitter = leftRowSplitter ?? throw new ArgumentNullException(nameof(leftRowSplitter));
        _rightSingleHost = rightSingleHost ?? throw new ArgumentNullException(nameof(rightSingleHost));
        _rightVerticalSplitHost = rightVerticalSplitHost ?? throw new ArgumentNullException(nameof(rightVerticalSplitHost));
        _rightTopHost = rightTopHost ?? throw new ArgumentNullException(nameof(rightTopHost));
        _rightBottomHost = rightBottomHost ?? throw new ArgumentNullException(nameof(rightBottomHost));
        _mapPane = mapPane ?? throw new ArgumentNullException(nameof(mapPane));
        _mapRowSplitter = mapRowSplitter ?? throw new ArgumentNullException(nameof(mapRowSplitter));
        _rightHorizontalSplitHost = rightHorizontalSplitHost ?? throw new ArgumentNullException(nameof(rightHorizontalSplitHost));
        _rightHorizontalLeftHost = rightHorizontalLeftHost ?? throw new ArgumentNullException(nameof(rightHorizontalLeftHost));
        _rightHorizontalRightHost = rightHorizontalRightHost ?? throw new ArgumentNullException(nameof(rightHorizontalRightHost));
        _rightColumnSplitter = rightColumnSplitter ?? throw new ArgumentNullException(nameof(rightColumnSplitter));
        _paneStagingArea = paneStagingArea ?? throw new ArgumentNullException(nameof(paneStagingArea));
        _fileBrowserPaneControl = fileBrowserPaneControl ?? throw new ArgumentNullException(nameof(fileBrowserPaneControl));
        _previewPaneControl = previewPaneControl ?? throw new ArgumentNullException(nameof(previewPaneControl));
        _mapPaneControl = mapPaneControl ?? throw new ArgumentNullException(nameof(mapPaneControl));
    }

    public void ApplyLayout(PaneLayoutPreset preset, PaneViewType region1View, PaneViewType region2View, PaneViewType region3View)
    {
        var plan = CreateLayoutPlan(preset, region1View, region2View, region3View);
        _fileBrowserPane.Visibility = Visibility.Visible;
        _detailPane.Visibility = Visibility.Visible;
        _mainSplitter.Visibility = Visibility.Visible;

        ApplyVisibilityState(plan.VisibilityState);
        ClearPaneHosts();
        PlacePaneInHost(plan.Region1Content, ResolveHost(plan.HostLayout.Region1));
        PlacePaneInHost(plan.Region2Content, ResolveHost(plan.HostLayout.Region2));
        PlacePaneInHost(plan.Region3Content, ResolveHost(plan.HostLayout.Region3));
    }

    public void ShowPreviewOnly()
    {
        var state = GetPreviewOnlyVisibilityState();
        _fileBrowserPane.Visibility = Visibility.Collapsed;
        _mainSplitter.Visibility = Visibility.Collapsed;
        ApplyVisibilityState(state);
        ClearPaneHosts();
        PlacePaneInHost(PaneContentKind.Preview, _rightSingleHost);
    }

    private void ApplyVisibilityState(PaneLayoutVisibilityState state)
    {
        _leftSingleHost.Visibility = state.LeftSingleHost;
        _leftVerticalSplitHost.Visibility = state.LeftVerticalSplitHost;
        _rightSingleHost.Visibility = state.RightSingleHost;
        _rightVerticalSplitHost.Visibility = state.RightVerticalSplitHost;
        _rightHorizontalSplitHost.Visibility = state.RightHorizontalSplitHost;
        _leftRowSplitter.Visibility = state.LeftRowSplitter;
        _mapRowSplitter.Visibility = state.MapRowSplitter;
        _rightColumnSplitter.Visibility = state.RightColumnSplitter;
        _mapPane.Visibility = state.MapPane;
    }

    private void PlacePaneInHost(PaneContentKind paneContent, Grid host)
    {
        var pane = ResolvePaneContentElement(paneContent);
        RemoveFromParent(pane);
        host.Children.Add(pane);
    }

    private FrameworkElement ResolvePaneContentElement(PaneContentKind paneContent)
    {
        return paneContent switch
        {
            PaneContentKind.File => _fileBrowserPaneControl,
            PaneContentKind.Map => _mapPaneControl,
            _ => _previewPaneControl
        };
    }

    private Grid ResolveHost(PaneRegionHostSlot slot)
    {
        return slot switch
        {
            PaneRegionHostSlot.LeftSingle => _leftSingleHost,
            PaneRegionHostSlot.LeftTop => _leftTopHost,
            PaneRegionHostSlot.LeftBottom => _leftBottomHost,
            PaneRegionHostSlot.RightSingle => _rightSingleHost,
            PaneRegionHostSlot.RightTop => _rightTopHost,
            PaneRegionHostSlot.RightBottom => _rightBottomHost,
            PaneRegionHostSlot.RightHorizontalLeft => _rightHorizontalLeftHost,
            PaneRegionHostSlot.RightHorizontalRight => _rightHorizontalRightHost,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown pane host slot.")
        };
    }

    private void ClearPaneHosts()
    {
        foreach (var host in EnumeratePaneHosts())
        {
            host.Children.Clear();
        }
    }

    private Grid[] EnumeratePaneHosts()
    {
        return
        [
            _leftSingleHost,
            _leftTopHost,
            _leftBottomHost,
            _rightSingleHost,
            _rightTopHost,
            _rightBottomHost,
            _rightHorizontalLeftHost,
            _rightHorizontalRightHost,
            _paneStagingArea
        ];
    }

    private static void RemoveFromParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
    }

    internal static PaneLayoutVisibilityState GetVisibilityState(PaneLayoutPreset preset)
    {
        return preset switch
        {
            PaneLayoutPreset.LeftCenterRight => new PaneLayoutVisibilityState(
                LeftSingleHost: Visibility.Visible,
                LeftVerticalSplitHost: Visibility.Collapsed,
                RightSingleHost: Visibility.Collapsed,
                RightVerticalSplitHost: Visibility.Collapsed,
                RightHorizontalSplitHost: Visibility.Visible,
                LeftRowSplitter: Visibility.Collapsed,
                MapRowSplitter: Visibility.Collapsed,
                RightColumnSplitter: Visibility.Visible,
                MapPane: Visibility.Collapsed),
            PaneLayoutPreset.LeftSplitAndRight => new PaneLayoutVisibilityState(
                LeftSingleHost: Visibility.Collapsed,
                LeftVerticalSplitHost: Visibility.Visible,
                RightSingleHost: Visibility.Visible,
                RightVerticalSplitHost: Visibility.Collapsed,
                RightHorizontalSplitHost: Visibility.Collapsed,
                LeftRowSplitter: Visibility.Visible,
                MapRowSplitter: Visibility.Collapsed,
                RightColumnSplitter: Visibility.Collapsed,
                MapPane: Visibility.Collapsed),
            _ => new PaneLayoutVisibilityState(
                LeftSingleHost: Visibility.Visible,
                LeftVerticalSplitHost: Visibility.Collapsed,
                RightSingleHost: Visibility.Collapsed,
                RightVerticalSplitHost: Visibility.Visible,
                RightHorizontalSplitHost: Visibility.Collapsed,
                LeftRowSplitter: Visibility.Collapsed,
                MapRowSplitter: Visibility.Visible,
                RightColumnSplitter: Visibility.Collapsed,
                MapPane: Visibility.Visible)
        };
    }

    internal static PaneRegionHostLayout GetRegionHostLayout(PaneLayoutPreset preset)
    {
        return preset switch
        {
            PaneLayoutPreset.LeftCenterRight => new PaneRegionHostLayout(
                PaneRegionHostSlot.LeftSingle,
                PaneRegionHostSlot.RightHorizontalLeft,
                PaneRegionHostSlot.RightHorizontalRight),
            PaneLayoutPreset.LeftSplitAndRight => new PaneRegionHostLayout(
                PaneRegionHostSlot.LeftTop,
                PaneRegionHostSlot.LeftBottom,
                PaneRegionHostSlot.RightSingle),
            _ => new PaneRegionHostLayout(
                PaneRegionHostSlot.LeftSingle,
                PaneRegionHostSlot.RightTop,
                PaneRegionHostSlot.RightBottom)
        };
    }

    internal static PaneContentKind ResolvePaneContent(PaneViewType paneView)
    {
        return paneView switch
        {
            PaneViewType.File => PaneContentKind.File,
            PaneViewType.Map => PaneContentKind.Map,
            _ => PaneContentKind.Preview
        };
    }

    internal static PaneLayoutPlacementPlan CreateLayoutPlan(
        PaneLayoutPreset preset,
        PaneViewType region1View,
        PaneViewType region2View,
        PaneViewType region3View)
    {
        return new PaneLayoutPlacementPlan(
            VisibilityState: GetVisibilityState(preset),
            HostLayout: GetRegionHostLayout(preset),
            Region1Content: ResolvePaneContent(region1View),
            Region2Content: ResolvePaneContent(region2View),
            Region3Content: ResolvePaneContent(region3View));
    }

    internal static PaneLayoutVisibilityState GetPreviewOnlyVisibilityState()
    {
        return new PaneLayoutVisibilityState(
            LeftSingleHost: Visibility.Collapsed,
            LeftVerticalSplitHost: Visibility.Collapsed,
            RightSingleHost: Visibility.Visible,
            RightVerticalSplitHost: Visibility.Collapsed,
            RightHorizontalSplitHost: Visibility.Collapsed,
            LeftRowSplitter: Visibility.Collapsed,
            MapRowSplitter: Visibility.Collapsed,
            RightColumnSplitter: Visibility.Collapsed,
            MapPane: Visibility.Collapsed);
    }
}
