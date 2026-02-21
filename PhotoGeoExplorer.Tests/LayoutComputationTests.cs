using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Preview;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Tests;

public sealed class LayoutComputationTests
{
    [Fact]
    public void TryComputePanePrimaryLengthReturnsFalseWhenAvailableLengthIsNonPositive()
    {
        var result = MainWindowLayoutCoordinator.TryComputePanePrimaryLength(
            containerLength: 100,
            splitterLength: 120,
            currentPrimaryLength: 300,
            deltaLength: 20,
            minPrimaryLength: 220,
            minSecondaryLength: 320,
            out var clamped);

        Assert.False(result);
        Assert.Equal(0, clamped);
    }

    [Fact]
    public void TryComputePanePrimaryLengthClampsToConfiguredRange()
    {
        var lowResult = MainWindowLayoutCoordinator.TryComputePanePrimaryLength(
            containerLength: 1200,
            splitterLength: 10,
            currentPrimaryLength: 300,
            deltaLength: -500,
            minPrimaryLength: 220,
            minSecondaryLength: 320,
            out var lowClamped);
        var highResult = MainWindowLayoutCoordinator.TryComputePanePrimaryLength(
            containerLength: 1200,
            splitterLength: 10,
            currentPrimaryLength: 700,
            deltaLength: 500,
            minPrimaryLength: 220,
            minSecondaryLength: 320,
            out var highClamped);

        Assert.True(lowResult);
        Assert.Equal(220, lowClamped);
        Assert.True(highResult);
        Assert.Equal(870, highClamped);
    }

    [Fact]
    public void TryComputePanePrimaryLengthHandlesOverconstrainedRanges()
    {
        var result = MainWindowLayoutCoordinator.TryComputePanePrimaryLength(
            containerLength: 450,
            splitterLength: 10,
            currentPrimaryLength: 100,
            deltaLength: 500,
            minPrimaryLength: 300,
            minSecondaryLength: 300,
            out var clamped);

        Assert.True(result);
        Assert.Equal(440, clamped);
    }

    [Fact]
    public void ResolveStoredLengthUsesFallbackWhenStoredLengthIsZero()
    {
        var fallback = new GridLength(3, GridUnitType.Star);
        var resolved = MainWindowLayoutCoordinator.ResolveStoredLength(new GridLength(0), fallback);

        Assert.Equal(fallback, resolved);
    }

    [Fact]
    public void ResolveStoredLengthUsesStoredLengthWhenPositive()
    {
        var stored = new GridLength(240, GridUnitType.Pixel);
        var fallback = new GridLength(3, GridUnitType.Star);
        var resolved = MainWindowLayoutCoordinator.ResolveStoredLength(stored, fallback);

        Assert.Equal(stored, resolved);
    }

    [Fact]
    public void ShouldApplyPaneLayoutReturnsExpectedValue()
    {
        Assert.True(MainWindowLayoutCoordinator.ShouldApplyPaneLayout(previewMaximized: false));
        Assert.False(MainWindowLayoutCoordinator.ShouldApplyPaneLayout(previewMaximized: true));
    }

    [Fact]
    public void ComputeTogglePreviewMaximizePlanReturnsUnchangedWhenStateIsSame()
    {
        var plan = MainWindowLayoutCoordinator.ComputeTogglePreviewMaximizePlan(
            maximize: false,
            previewMaximized: false,
            layoutStored: true,
            currentFileBrowserWidth: new GridLength(2, GridUnitType.Star),
            currentSplitterWidth: GridLength.Auto,
            currentDetailWidth: new GridLength(3, GridUnitType.Star),
            storedFileBrowserWidth: new GridLength(220, GridUnitType.Pixel),
            storedSplitterWidth: new GridLength(1, GridUnitType.Pixel),
            storedDetailWidth: new GridLength(320, GridUnitType.Pixel));

        Assert.False(plan.HasChanges);
        Assert.False(plan.ShouldStoreCurrentLayout);
        Assert.False(plan.PreviewMaximized);
    }

    [Fact]
    public void ComputeTogglePreviewMaximizePlanStoresAndMaximizesWhenRequested()
    {
        var plan = MainWindowLayoutCoordinator.ComputeTogglePreviewMaximizePlan(
            maximize: true,
            previewMaximized: false,
            layoutStored: false,
            currentFileBrowserWidth: new GridLength(220, GridUnitType.Pixel),
            currentSplitterWidth: new GridLength(4, GridUnitType.Pixel),
            currentDetailWidth: new GridLength(1, GridUnitType.Star),
            storedFileBrowserWidth: new GridLength(0),
            storedSplitterWidth: new GridLength(0),
            storedDetailWidth: new GridLength(0));

        Assert.True(plan.HasChanges);
        Assert.True(plan.ShouldStoreCurrentLayout);
        Assert.True(plan.PreviewMaximized);
        Assert.True(plan.ShowPreviewOnly);
        Assert.Equal(new GridLength(220, GridUnitType.Pixel), plan.StoredFileBrowserWidth);
        Assert.Equal(new GridLength(4, GridUnitType.Pixel), plan.StoredSplitterWidth);
        Assert.Equal(new GridLength(0), plan.FileBrowserWidth);
        Assert.Equal(new GridLength(0), plan.SplitterWidth);
        Assert.Equal(new GridLength(1, GridUnitType.Star), plan.DetailWidth);
    }

    [Fact]
    public void ComputeTogglePreviewMaximizePlanRestoresStoredOrFallbackLengths()
    {
        var storedPlan = MainWindowLayoutCoordinator.ComputeTogglePreviewMaximizePlan(
            maximize: false,
            previewMaximized: true,
            layoutStored: true,
            currentFileBrowserWidth: new GridLength(0),
            currentSplitterWidth: new GridLength(0),
            currentDetailWidth: new GridLength(1, GridUnitType.Star),
            storedFileBrowserWidth: new GridLength(260, GridUnitType.Pixel),
            storedSplitterWidth: new GridLength(6, GridUnitType.Pixel),
            storedDetailWidth: new GridLength(480, GridUnitType.Pixel));

        var fallbackPlan = MainWindowLayoutCoordinator.ComputeTogglePreviewMaximizePlan(
            maximize: false,
            previewMaximized: true,
            layoutStored: true,
            currentFileBrowserWidth: new GridLength(0),
            currentSplitterWidth: new GridLength(0),
            currentDetailWidth: new GridLength(1, GridUnitType.Star),
            storedFileBrowserWidth: new GridLength(0),
            storedSplitterWidth: new GridLength(0),
            storedDetailWidth: new GridLength(0));

        Assert.Equal(new GridLength(260, GridUnitType.Pixel), storedPlan.FileBrowserWidth);
        Assert.Equal(new GridLength(6, GridUnitType.Pixel), storedPlan.SplitterWidth);
        Assert.Equal(new GridLength(480, GridUnitType.Pixel), storedPlan.DetailWidth);

        Assert.Equal(new GridLength(2, GridUnitType.Star), fallbackPlan.FileBrowserWidth);
        Assert.Equal(GridLength.Auto, fallbackPlan.SplitterWidth);
        Assert.Equal(new GridLength(3, GridUnitType.Star), fallbackPlan.DetailWidth);
    }

    [Fact]
    public void TryComputeHostSplitterPrimaryLengthReturnsFalseWhenSkippedOrHostHidden()
    {
        var skipped = MainWindowLayoutCoordinator.TryComputeHostSplitterPrimaryLength(
            skipComputation: true,
            hostVisibility: Visibility.Visible,
            containerLength: 1000,
            splitterLength: 10,
            currentPrimaryLength: 400,
            deltaLength: 30,
            minPrimaryLength: 200,
            minSecondaryLength: 200,
            out var skippedLength);
        var hidden = MainWindowLayoutCoordinator.TryComputeHostSplitterPrimaryLength(
            skipComputation: false,
            hostVisibility: Visibility.Collapsed,
            containerLength: 1000,
            splitterLength: 10,
            currentPrimaryLength: 400,
            deltaLength: 30,
            minPrimaryLength: 200,
            minSecondaryLength: 200,
            out var hiddenLength);

        Assert.False(skipped);
        Assert.Equal(0, skippedLength);
        Assert.False(hidden);
        Assert.Equal(0, hiddenLength);
    }

    [Fact]
    public void TryComputeHostSplitterPrimaryLengthDelegatesToPaneComputationWhenVisible()
    {
        var result = MainWindowLayoutCoordinator.TryComputeHostSplitterPrimaryLength(
            skipComputation: false,
            hostVisibility: Visibility.Visible,
            containerLength: 1000,
            splitterLength: 10,
            currentPrimaryLength: 300,
            deltaLength: 50,
            minPrimaryLength: 200,
            minSecondaryLength: 200,
            out var clamped);

        Assert.True(result);
        Assert.Equal(350, clamped);
    }

    [Fact]
    public void GetVisibilityStateReturnsExpectedValuesForLeftCenterRight()
    {
        var state = PaneLayoutHostService.GetVisibilityState(PaneLayoutPreset.LeftCenterRight);

        Assert.Equal(Visibility.Visible, state.LeftSingleHost);
        Assert.Equal(Visibility.Collapsed, state.LeftVerticalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.RightSingleHost);
        Assert.Equal(Visibility.Collapsed, state.RightVerticalSplitHost);
        Assert.Equal(Visibility.Visible, state.RightHorizontalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.MapPane);
    }

    [Fact]
    public void GetVisibilityStateReturnsExpectedValuesForLeftSplitAndRight()
    {
        var state = PaneLayoutHostService.GetVisibilityState(PaneLayoutPreset.LeftSplitAndRight);

        Assert.Equal(Visibility.Collapsed, state.LeftSingleHost);
        Assert.Equal(Visibility.Visible, state.LeftVerticalSplitHost);
        Assert.Equal(Visibility.Visible, state.RightSingleHost);
        Assert.Equal(Visibility.Collapsed, state.RightVerticalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.RightHorizontalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.MapPane);
    }

    [Fact]
    public void GetVisibilityStateReturnsExpectedValuesForLeftAndRightSplit()
    {
        var state = PaneLayoutHostService.GetVisibilityState(PaneLayoutPreset.LeftAndRightSplit);

        Assert.Equal(Visibility.Visible, state.LeftSingleHost);
        Assert.Equal(Visibility.Collapsed, state.LeftVerticalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.RightSingleHost);
        Assert.Equal(Visibility.Visible, state.RightVerticalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.RightHorizontalSplitHost);
        Assert.Equal(Visibility.Visible, state.MapPane);
    }

    [Fact]
    public void GetRegionHostLayoutReturnsExpectedSlots()
    {
        var leftCenterRight = PaneLayoutHostService.GetRegionHostLayout(PaneLayoutPreset.LeftCenterRight);
        var leftSplitAndRight = PaneLayoutHostService.GetRegionHostLayout(PaneLayoutPreset.LeftSplitAndRight);
        var leftAndRightSplit = PaneLayoutHostService.GetRegionHostLayout(PaneLayoutPreset.LeftAndRightSplit);

        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftSingle, leftCenterRight.Region1);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightHorizontalLeft, leftCenterRight.Region2);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightHorizontalRight, leftCenterRight.Region3);

        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftTop, leftSplitAndRight.Region1);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftBottom, leftSplitAndRight.Region2);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightSingle, leftSplitAndRight.Region3);

        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftSingle, leftAndRightSplit.Region1);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightTop, leftAndRightSplit.Region2);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightBottom, leftAndRightSplit.Region3);
    }

    [Fact]
    public void ResolvePaneContentReturnsExpectedKinds()
    {
        Assert.Equal(PaneLayoutHostService.PaneContentKind.File, PaneLayoutHostService.ResolvePaneContent(PaneViewType.File));
        Assert.Equal(PaneLayoutHostService.PaneContentKind.Map, PaneLayoutHostService.ResolvePaneContent(PaneViewType.Map));
        Assert.Equal(PaneLayoutHostService.PaneContentKind.Preview, PaneLayoutHostService.ResolvePaneContent(PaneViewType.Preview));
        Assert.Equal(PaneLayoutHostService.PaneContentKind.Preview, PaneLayoutHostService.ResolvePaneContent((PaneViewType)999));
    }

    [Fact]
    public void CreateLayoutPlanReturnsExpectedMapping()
    {
        var plan = PaneLayoutHostService.CreateLayoutPlan(
            PaneLayoutPreset.LeftSplitAndRight,
            PaneViewType.Map,
            PaneViewType.File,
            PaneViewType.Preview);

        Assert.Equal(Visibility.Visible, plan.VisibilityState.LeftVerticalSplitHost);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftTop, plan.HostLayout.Region1);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.LeftBottom, plan.HostLayout.Region2);
        Assert.Equal(PaneLayoutHostService.PaneRegionHostSlot.RightSingle, plan.HostLayout.Region3);
        Assert.Equal(PaneLayoutHostService.PaneContentKind.Map, plan.Region1Content);
        Assert.Equal(PaneLayoutHostService.PaneContentKind.File, plan.Region2Content);
        Assert.Equal(PaneLayoutHostService.PaneContentKind.Preview, plan.Region3Content);
    }

    [Fact]
    public void GetPreviewOnlyVisibilityStateReturnsExpectedValues()
    {
        var state = PaneLayoutHostService.GetPreviewOnlyVisibilityState();

        Assert.Equal(Visibility.Collapsed, state.LeftSingleHost);
        Assert.Equal(Visibility.Collapsed, state.LeftVerticalSplitHost);
        Assert.Equal(Visibility.Visible, state.RightSingleHost);
        Assert.Equal(Visibility.Collapsed, state.RightVerticalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.RightHorizontalSplitHost);
        Assert.Equal(Visibility.Collapsed, state.MapPane);
    }

    [Fact]
    public void MainWindowLayoutCoordinatorConstructorValidatesAllParameters()
    {
        var ctor = typeof(MainWindowLayoutCoordinator).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        AssertConstructorNullGuards(ctor);
    }

    [Fact]
    public void PaneLayoutHostServiceConstructorValidatesAllParameters()
    {
        var ctor = typeof(PaneLayoutHostService).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        AssertConstructorNullGuards(ctor);
    }

    [Fact]
    public void PaneLayoutHostServiceResolveHostReturnsExpectedField()
    {
        var service = CreatePaneLayoutHostServiceForTest();
        var resolveHost = typeof(PaneLayoutHostService).GetMethod("ResolveHost", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Same(GetPrivateField<Grid>(service, "_leftSingleHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.LeftSingle]));
        Assert.Same(GetPrivateField<Grid>(service, "_leftTopHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.LeftTop]));
        Assert.Same(GetPrivateField<Grid>(service, "_leftBottomHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.LeftBottom]));
        Assert.Same(GetPrivateField<Grid>(service, "_rightSingleHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.RightSingle]));
        Assert.Same(GetPrivateField<Grid>(service, "_rightTopHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.RightTop]));
        Assert.Same(GetPrivateField<Grid>(service, "_rightBottomHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.RightBottom]));
        Assert.Same(GetPrivateField<Grid>(service, "_rightHorizontalLeftHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.RightHorizontalLeft]));
        Assert.Same(GetPrivateField<Grid>(service, "_rightHorizontalRightHost"), resolveHost.Invoke(service, [PaneLayoutHostService.PaneRegionHostSlot.RightHorizontalRight]));

        var ex = Assert.Throws<TargetInvocationException>(() => resolveHost.Invoke(service, [(PaneLayoutHostService.PaneRegionHostSlot)999]));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public void PaneLayoutHostServiceEnumeratePaneHostsReturnsAllHosts()
    {
        var service = CreatePaneLayoutHostServiceForTest();
        var enumerate = typeof(PaneLayoutHostService).GetMethod("EnumeratePaneHosts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var hosts = Assert.IsType<Grid[]>(enumerate.Invoke(service, null));

        Assert.Equal(9, hosts.Length);
        Assert.Same(GetPrivateField<Grid>(service, "_leftSingleHost"), hosts[0]);
        Assert.Same(GetPrivateField<Grid>(service, "_leftTopHost"), hosts[1]);
        Assert.Same(GetPrivateField<Grid>(service, "_leftBottomHost"), hosts[2]);
        Assert.Same(GetPrivateField<Grid>(service, "_rightSingleHost"), hosts[3]);
        Assert.Same(GetPrivateField<Grid>(service, "_rightTopHost"), hosts[4]);
        Assert.Same(GetPrivateField<Grid>(service, "_rightBottomHost"), hosts[5]);
        Assert.Same(GetPrivateField<Grid>(service, "_rightHorizontalLeftHost"), hosts[6]);
        Assert.Same(GetPrivateField<Grid>(service, "_rightHorizontalRightHost"), hosts[7]);
        Assert.Same(GetPrivateField<Grid>(service, "_paneStagingArea"), hosts[8]);
    }

    [Fact]
    public void PaneLayoutHostServiceResolvePaneContentElementReturnsExpectedField()
    {
        var service = CreatePaneLayoutHostServiceForTest();
        var resolvePaneContentElement = typeof(PaneLayoutHostService).GetMethod("ResolvePaneContentElement", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Same(
            GetPrivateField<FrameworkElement>(service, "_fileBrowserPaneControl"),
            resolvePaneContentElement.Invoke(service, [PaneLayoutHostService.PaneContentKind.File]));
        Assert.Same(
            GetPrivateField<FrameworkElement>(service, "_mapPaneControl"),
            resolvePaneContentElement.Invoke(service, [PaneLayoutHostService.PaneContentKind.Map]));
        Assert.Same(
            GetPrivateField<FrameworkElement>(service, "_previewPaneControl"),
            resolvePaneContentElement.Invoke(service, [PaneLayoutHostService.PaneContentKind.Preview]));
        Assert.Same(
            GetPrivateField<FrameworkElement>(service, "_previewPaneControl"),
            resolvePaneContentElement.Invoke(service, [(PaneLayoutHostService.PaneContentKind)999]));
    }

    [Fact]
    public void ApplyPaneLayoutUpdatesCurrentStateWhenPreviewIsMaximized()
    {
        var coordinator = CreateMainWindowLayoutCoordinatorForTest();
        SetPrivateField(coordinator, "_previewMaximized", true);

        coordinator.ApplyPaneLayout(PaneLayoutPreset.LeftSplitAndRight, PaneViewType.Map, PaneViewType.File, PaneViewType.Preview);

        Assert.Equal(PaneLayoutPreset.LeftSplitAndRight, GetPrivateField<PaneLayoutPreset>(coordinator, "_currentPaneLayoutPreset"));
        Assert.Equal(PaneViewType.Map, GetPrivateField<PaneViewType>(coordinator, "_currentPaneRegion1View"));
        Assert.Equal(PaneViewType.File, GetPrivateField<PaneViewType>(coordinator, "_currentPaneRegion2View"));
        Assert.Equal(PaneViewType.Preview, GetPrivateField<PaneViewType>(coordinator, "_currentPaneRegion3View"));
    }

    [Fact]
    public void TogglePreviewMaximizeReturnsEarlyWhenNoStateChange()
    {
        var coordinator = CreateMainWindowLayoutCoordinatorForTest();
        coordinator.TogglePreviewMaximize(maximize: false);
        Assert.False(GetPrivateField<bool>(coordinator, "_previewMaximized"));
    }

    [Fact]
    public void ApplyMainSplitterDeltaReturnsEarlyWhenPreviewIsMaximized()
    {
        var coordinator = CreateMainWindowLayoutCoordinatorForTest();
        SetPrivateField(coordinator, "_previewMaximized", true);
        coordinator.ApplyMainSplitterDelta(horizontalChange: 120);
    }

    [Fact]
    public void PreviewPaneComputationHelpersReturnExpectedResults()
    {
        Assert.False(PreviewPaneViewControl.HasMeaningfulRasterizationScaleChange(1.0, 1.00001));
        Assert.True(PreviewPaneViewControl.HasMeaningfulRasterizationScaleChange(1.0, 1.01));
        Assert.True(PreviewPaneViewControl.ShouldRetryFit(0, 200, 2));
        Assert.False(PreviewPaneViewControl.ShouldRetryFit(300, 200, 2));
        Assert.False(PreviewPaneViewControl.ShouldRetryFit(0, 200, 0));
        Assert.False(PreviewPaneViewControl.HasZoomFactorChanged(2.0, 2.00001));
        Assert.True(PreviewPaneViewControl.HasZoomFactorChanged(2.0, 2.1));

        var zoomOffsets = PreviewPaneViewControl.CalculateZoomOffsets(
            currentZoom: 2.0,
            horizontalOffset: 10,
            verticalOffset: 20,
            cursorX: 30,
            cursorY: 40,
            targetZoom: 3.0);
        var dragOffsets = PreviewPaneViewControl.CalculateDragOffsets(
            dragStartHorizontalOffset: 100,
            dragStartVerticalOffset: 200,
            deltaX: 15,
            deltaY: -20);

        Assert.Equal(30, zoomOffsets.TargetOffsetX);
        Assert.Equal(50, zoomOffsets.TargetOffsetY);
        Assert.Equal(85, dragOffsets.HorizontalOffset);
        Assert.Equal(220, dragOffsets.VerticalOffset);
    }

    [Fact]
    public void PaneLayoutChangedEventArgsStoresConstructorValues()
    {
        var args = new PaneLayoutChangedEventArgs(
            PaneLayoutPreset.LeftSplitAndRight,
            PaneViewType.Map,
            PaneViewType.Preview,
            PaneViewType.File);

        Assert.Equal(PaneLayoutPreset.LeftSplitAndRight, args.Preset);
        Assert.Equal(PaneViewType.Map, args.Region1View);
        Assert.Equal(PaneViewType.Preview, args.Region2View);
        Assert.Equal(PaneViewType.File, args.Region3View);
    }

    [Fact]
    public void TryComputeSplitterLengthsReturnsFalseWhenHostComputationFails()
    {
        var result = MainWindowLayoutCoordinator.TryComputeSplitterLengths(
            skipComputation: true,
            hostVisibility: Visibility.Visible,
            containerLength: 1000,
            splitterLength: 10,
            currentPrimaryLength: 300,
            deltaLength: 40,
            minPrimaryLength: 200,
            minSecondaryLength: 200,
            out var primaryLength,
            out var secondaryLength);

        Assert.False(result);
        Assert.Equal(default, primaryLength);
        Assert.Equal(default, secondaryLength);
    }

    [Fact]
    public void TryComputeSplitterLengthsReturnsPixelAndStarLengthsWhenHostComputationSucceeds()
    {
        var result = MainWindowLayoutCoordinator.TryComputeSplitterLengths(
            skipComputation: false,
            hostVisibility: Visibility.Visible,
            containerLength: 1000,
            splitterLength: 10,
            currentPrimaryLength: 300,
            deltaLength: 50,
            minPrimaryLength: 200,
            minSecondaryLength: 200,
            out var primaryLength,
            out var secondaryLength);

        Assert.True(result);
        Assert.Equal(350, primaryLength.Value);
        Assert.Equal(GridUnitType.Pixel, primaryLength.GridUnitType);
        Assert.Equal(1, secondaryLength.Value);
        Assert.Equal(GridUnitType.Star, secondaryLength.GridUnitType);
    }

    private static void AssertConstructorNullGuards(ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters();
        var baselineArgs = parameters.Select(p => CreatePlaceholder(p.ParameterType)).ToArray();

        for (var i = 0; i < parameters.Length; i++)
        {
            var args = (object?[])baselineArgs.Clone();
            args[i] = null;
            var ex = Assert.Throws<TargetInvocationException>(() => ctor.Invoke(args));
            var argumentNullException = Assert.IsType<ArgumentNullException>(ex.InnerException);
            Assert.Equal(parameters[i].Name, argumentNullException.ParamName);
        }
    }

    private static MainWindowLayoutCoordinator CreateMainWindowLayoutCoordinatorForTest()
    {
        var ctor = typeof(MainWindowLayoutCoordinator).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var args = ctor.GetParameters().Select(p => CreatePlaceholder(p.ParameterType)).ToArray();
        return (MainWindowLayoutCoordinator)ctor.Invoke(args);
    }

    private static PaneLayoutHostService CreatePaneLayoutHostServiceForTest()
    {
        var ctor = typeof(PaneLayoutHostService).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var args = ctor.GetParameters().Select(p => CreatePlaceholder(p.ParameterType)).ToArray();
        return (PaneLayoutHostService)ctor.Invoke(args);
    }

    private static object CreatePlaceholder(Type parameterType)
    {
        var concreteType = parameterType == typeof(FrameworkElement)
            ? typeof(Grid)
            : parameterType;
        return RuntimeHelpers.GetUninitializedObject(concreteType);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)field.GetValue(instance)!;
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(instance, value);
    }
}
