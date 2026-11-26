using System;
using System.Collections.Generic;
using Mapsui;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Panes.Map.MapsuiAdapter;
using PhotoGeoExplorer.Services.MapRendering;
using PhotoGeoExplorer.Services.PanZoom;
using Xunit;

namespace PhotoGeoExplorer.Tests;

/// <summary>地図の入力の一本化（#362 段階 1）。視点の計算、慣性、入力の順序、表示面への反映。</summary>
public sealed class MapInputTests
{
    private const double Width = 843;
    private const double Height = 678;
    private const double Resolution = 2.388657;

    private static readonly MapViewState View = new(true, 15849000, 5429700, Resolution, Width, Height);

    [Theory]
    [InlineData(571.0, 339.0, 2.0, 0.0, 0.0)]
    [InlineData(100.0, 600.0, 0.5, -14.0, 9.0)]
    [InlineData(421.5, 339.0, 1.0, 30.0, -20.0)]
    public void GestureMovesThePointUnderThePivotByTheTranslation(double x, double y, double scale, double tx, double ty)
    {
        var next = MapViewGesture.Apply(View, new PanZoomPoint(x, y), scale, tx, ty);

        Assert.Equal(Resolution / scale, next.Resolution, 1e-12);
        Assert.Equal(WorldX(View, x), WorldX(next, x + tx), 1e-6);
        Assert.Equal(WorldY(View, y), WorldY(next, y + ty), 1e-6);
    }

    [Fact]
    public void GestureKeepsAViewWithoutSize()
    {
        var view = View with { HasSize = false };

        Assert.Equal(view, MapViewGesture.Apply(view, new PanZoomPoint(1, 1), 2, 3, 4));
    }

    [Theory]
    [InlineData(double.NaN, 1.0, 0.0)]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(1.0, double.PositiveInfinity, 0.0)]
    [InlineData(1.0, 1.0, double.NaN)]
    public void GestureRejectsInvalidValues(double pivotX, double scale, double translationX)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => MapViewGesture.Apply(View, new PanZoomPoint(pivotX, 0), scale, translationX, 0));

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.6, 0.8)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void SlowOrInvalidReleaseDoesNotSlide(double x, double y)
    {
        var behavior = MapInertia.Resolve(x, y);

        var deceleration = Assert.NotNull(behavior.Deceleration);
        Assert.True(MapInertia.MinSpeed * MapInertia.MinSpeed / (2 * deceleration) < 1);
        Assert.Null(behavior.Displacement);
    }

    [Theory]
    [InlineData(1.5, 0.0)]
    [InlineData(0.0, -3.7)]
    [InlineData(2.4, 3.2)]
    public void ReleaseSlidesAsFarAsTheFormerFling(double x, double y)
    {
        var speed = Math.Sqrt((x * x) + (y * y));

        var behavior = MapInertia.Resolve(x, y);

        // 従来の Fling が滑る距離: (2/π) × 速さ² × 100
        var distance = speed * speed / (2 * Assert.NotNull(behavior.Deceleration));
        Assert.Equal(2 / Math.PI * speed * speed * 100, distance, 1e-9);
        Assert.Null(behavior.Displacement);
    }

    [Theory]
    [InlineData(4.5, 0.0)]
    [InlineData(30.0, 40.0)]
    public void VeryFastReleaseSlidesOnlyAsFarAsTheSpeedLimit(double x, double y)
    {
        var behavior = MapInertia.Resolve(x, y);

        Assert.Null(behavior.Deceleration);
        Assert.Equal(2 / Math.PI * MapInertia.MaxSpeed * MapInertia.MaxSpeed * 100, Assert.NotNull(behavior.Displacement), 1e-9);
    }

    [Fact]
    public void SequencerPassesGesturesAndWheelsToTheTarget()
    {
        var target = new RecordingTarget();
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: true);

        Assert.True(sequencer.Manipulate(new PanZoomPoint(1, 2), 1.1, 3, 4, isInertial: false));
        sequencer.ZoomByWheel(120, new PanZoomPoint(5, 6));

        Assert.Equal(["move", "wheel 120"], target.Calls);
    }

    [Theory]
    [InlineData(double.NaN, 1.0, 120)]
    [InlineData(1.0, 0.0, 0)]
    public void SequencerIgnoresInvalidInput(double pivotX, double scale, int wheelDelta)
    {
        var target = new RecordingTarget();
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: true);

        Assert.True(sequencer.Manipulate(new PanZoomPoint(pivotX, 0), scale, 0, 0, isInertial: false));
        sequencer.ZoomByWheel(wheelDelta, new PanZoomPoint(pivotX, 0));

        Assert.Empty(target.Calls);
    }

    [Fact]
    public void SequencerStopsInertiaWhenTheTargetRefusesIt()
    {
        var target = new RecordingTarget { ManipulateResult = false };
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: true);

        sequencer.StartInertia();

        Assert.False(sequencer.Manipulate(new PanZoomPoint(1, 1), 1, 3, 0, isInertial: true));
    }

    [Fact]
    public void WheelDuringInertiaWaitsUntilTheNextFrameStopsIt()
    {
        var target = new RecordingTarget();
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: true);
        sequencer.StartInertia();

        sequencer.ZoomByWheel(120, new PanZoomPoint(1, 1));
        sequencer.ZoomByWheel(-120, new PanZoomPoint(1, 1));
        Assert.Empty(target.Calls);

        Assert.False(sequencer.Manipulate(new PanZoomPoint(1, 1), 1, 3, 0, isInertial: true));
        Assert.Equal(["wheel 120", "wheel -120"], target.Calls);

        // 打ち切りの後の完了で二重に適用しない
        sequencer.Complete();
        Assert.Equal(2, target.Calls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldWheelIsAppliedWhenTheGestureEndsOrANewOneStarts(bool completes)
    {
        var target = new RecordingTarget();
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: true);
        sequencer.StartInertia();
        sequencer.ZoomByWheel(120, new PanZoomPoint(1, 1));

        if (completes)
        {
            sequencer.Complete();
            Assert.Equal(["wheel 120"], target.Calls);
        }
        else
        {
            sequencer.Manipulate(new PanZoomPoint(1, 1), 1, 3, 0, isInertial: false);
            Assert.Equal(["wheel 120", "move"], target.Calls);
        }
    }

    [Fact]
    public void WheelDoesNotWaitWhenItDoesNotInterruptInertia()
    {
        var target = new RecordingTarget();
        var sequencer = new PanZoomInputSequencer(target, wheelInterruptsInertia: false);
        sequencer.StartInertia();

        sequencer.ZoomByWheel(120, new PanZoomPoint(1, 1));

        Assert.Equal(["wheel 120"], target.Calls);
    }

    [Fact]
    public void ControllerTargetAppliesGesturesAndProportionalWheelZoom()
    {
        var controller = new PanZoomController(1, 10);
        controller.SetLayout(new PanZoomSize(400, 300), new PanZoomSize(400, 300));
        var target = new PanZoomControllerTarget(controller, 1.1);

        target.ZoomByWheel(240, new PanZoomPoint(200, 150));
        Assert.Equal(1.21, controller.Transform.Scale, 1e-9);

        Assert.True(target.Manipulate(new PanZoomPoint(200, 150), 1, 10, 0, isInertial: true));
        Assert.Equal((-42.0) + 10, controller.Transform.TranslateX, 1e-9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void ControllerTargetRejectsAnInvalidWheelFactor(double factor)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PanZoomControllerTarget(new PanZoomController(1, 2), factor));

    [Fact]
    public void SurfaceTargetForwardsToTheSurfaceAndStopsInertiaWithoutOne()
    {
        using var fake = new FakeMapSurface();
        IMapSurface? surface = fake;
        var target = new MapSurfaceInputTarget(() => surface);

        Assert.True(target.Manipulate(new PanZoomPoint(1, 2), 1.5, 3, 4, isInertial: true));
        target.ZoomByWheel(-120, new PanZoomPoint(5, 6));
        surface = null;

        Assert.False(target.Manipulate(new PanZoomPoint(1, 2), 1.5, 3, 4, isInertial: true));
        target.ZoomByWheel(120, new PanZoomPoint(5, 6));
        Assert.Equal((new PanZoomPoint(1, 2), 1.5, 3.0, 4.0, true), Assert.Single(fake.Gestures));
        Assert.Equal((-120, new PanZoomPoint(5, 6)), Assert.Single(fake.Wheels));
    }

    [Fact]
    public void SurfaceBlocksTheControlsOwnMovementZoomAndRotation()
    {
        using var map = new Map();
        using var surface = new MapsuiMapSurface(map);

        Assert.True(map.Navigator.PanLock);
        Assert.True(map.Navigator.ZoomLock);
        Assert.True(map.Navigator.RotationLock);
    }

    [Theory]
    [InlineData(571.0, 339.0, 1.018, 0.0, 0.0)]
    [InlineData(571.0, 339.0, 0.982, 0.0, 0.0)]
    [InlineData(100.0, 600.0, 1.05, -14.0, 9.0)]
    [InlineData(421.5, 339.0, 1.0, 30.0, -20.0)]
    public void SurfaceAppliesGesturesExactlyWithoutRequestingTiles(double x, double y, double scale, double tx, double ty)
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        var fetches = 0;
        map.Navigator.FetchRequested += (_, _) => fetches++;
        var expected = surface.View;
        var pivot = new PanZoomPoint(x, y);

        for (var frame = 0; frame < 80; frame++)
        {
            expected = MapViewGesture.Apply(expected, pivot, scale, tx, ty);
            Assert.True(surface.ApplyGesture(pivot, scale, tx, ty, isInertial: false));
        }

        var actual = surface.View;
        Assert.Equal(expected.Resolution, actual.Resolution, expected.Resolution * 1e-9);
        Assert.Equal(expected.CenterX, actual.CenterX, actual.Resolution * 1e-6);
        Assert.Equal(expected.CenterY, actual.CenterY, actual.Resolution * 1e-6);
        Assert.Equal(0, fetches);
        Assert.True(map.Navigator.PanLock);
        Assert.True(map.Navigator.ZoomLock);
        Assert.Equal(0, map.Navigator.Viewport.Rotation);
    }

    [Theory]
    [InlineData(1.1)]
    [InlineData(0.9)]
    public void SurfaceKeepsThePointUnderThePivotAtZoomBounds(double scale)
    {
        using var map = CreateMap();
        map.Navigator.OverrideZoomBounds = new MMinMax(Resolution / 2, Resolution * 2);
        using var surface = new MapsuiMapSurface(map);
        var pivot = new PanZoomPoint(700, 100);
        var worldX = WorldX(surface.View, pivot.X);
        var worldY = WorldY(surface.View, pivot.Y);

        for (var frame = 0; frame < 30; frame++)
        {
            surface.ApplyGesture(pivot, scale, 0, 0, isInertial: false);
        }

        var after = surface.View;
        Assert.Equal(scale > 1 ? Resolution / 2 : Resolution * 2, after.Resolution, 1e-9);
        Assert.Equal(worldX, WorldX(after, pivot.X), after.Resolution * 1e-6);
        Assert.Equal(worldY, WorldY(after, pivot.Y), after.Resolution * 1e-6);
    }

    [Fact]
    public void SurfaceZoomsAboutTheCenterAndDoesNotMoveWhilePanIsStopped()
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        var before = surface.View;

        surface.LockPan();
        surface.LockPan();
        surface.ApplyGesture(new PanZoomPoint(700, 100), 2, 50, 60, isInertial: false);
        var locked = surface.View;
        surface.RestorePan();
        surface.ApplyGesture(new PanZoomPoint(700, 100), 1, 50, 0, isInertial: false);

        Assert.Equal(before.CenterX, locked.CenterX, 1e-6);
        Assert.Equal(before.CenterY, locked.CenterY, 1e-6);
        Assert.Equal(before.Resolution / 2, locked.Resolution, 1e-12);
        Assert.Equal(locked.CenterX - (50 * locked.Resolution), surface.View.CenterX, 1e-6);
    }

    [Theory]
    [InlineData("view")]
    [InlineData("marker")]
    [InlineData("markers")]
    [InlineData("wheel")]
    public void SurfaceEndsInertiaAfterTheViewIsMovedByAnotherOperation(string operation)
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        var pivot = new PanZoomPoint(100, 100);
        surface.ApplyGesture(pivot, 1, 10, 0, isInertial: false);
        Assert.True(surface.ApplyGesture(pivot, 1, 10, 0, isInertial: true));
        var marker = new MapMarker(
            43.768436,
            142.362436,
            new PhotoItem("a.jpg", 100, DateTimeOffset.UtcNow, isFolder: false),
            new PhotoMetadata(DateTimeOffset.UtcNow, null, null, 43.768436, 142.362436),
            PinImagePath: null);

        switch (operation)
        {
            case "view":
                surface.SetView(15849000, 5429700, Resolution);
                break;
            case "marker":
                surface.ShowMarker(marker, 10);
                break;
            case "markers":
                surface.ShowMarkers([]);
                break;
            default:
                surface.ZoomByWheel(120, pivot);
                break;
        }

        var moved = surface.View;
        Assert.False(surface.ApplyGesture(pivot, 1, 10, 0, isInertial: true));
        Assert.Equal(moved.CenterX, surface.View.CenterX);

        // 次の操作は受ける
        Assert.True(surface.ApplyGesture(pivot, 1, 10, 0, isInertial: false));
        Assert.True(surface.ApplyGesture(pivot, 1, 10, 0, isInertial: true));
    }

    [Theory]
    [InlineData(120, false, 1.0)]
    [InlineData(-120, false, 4.0)]
    [InlineData(120, true, 1.0)]
    public void WheelStepsOneZoomLevelAboutTheAnchorOrTheCenter(int delta, bool panStopped, double expectedResolution)
    {
        using var map = CreateMap(resolution: 2.0);
        map.Navigator.OverrideResolutions = [8, 4, 2, 1, 0.5];
        map.Navigator.MouseWheelAnimation.Duration = 0;
        using var surface = new MapsuiMapSurface(map);
        var anchor = new PanZoomPoint(700, 100);
        var before = surface.View;
        if (panStopped)
        {
            surface.LockPan();
        }

        surface.ZoomByWheel(delta, anchor);

        var after = surface.View;
        Assert.Equal(expectedResolution, after.Resolution, 1e-12);
        if (panStopped)
        {
            Assert.Equal(before.CenterX, after.CenterX, 1e-6);
        }
        else
        {
            Assert.Equal(WorldX(before, anchor.X), WorldX(after, anchor.X), 1e-6);
            Assert.Equal(WorldY(before, anchor.Y), WorldY(after, anchor.Y), 1e-6);
        }

        Assert.True(map.Navigator.PanLock);
        Assert.True(map.Navigator.ZoomLock);
    }

    private static Map CreateMap(double resolution = Resolution)
    {
        var map = new Map();
        map.Navigator.SetSize(Width, Height);
        map.Navigator.SetViewport(new Viewport(15849000, 5429700, resolution, 0, Width, Height), duration: 0);
        return map;
    }

    private static double WorldX(MapViewState view, double screenX)
        => view.CenterX + ((screenX - (view.WidthDip / 2)) * view.Resolution);

    private static double WorldY(MapViewState view, double screenY)
        => view.CenterY - ((screenY - (view.HeightDip / 2)) * view.Resolution);

    private sealed class RecordingTarget : IPanZoomTarget
    {
        public List<string> Calls { get; } = [];

        public bool ManipulateResult { get; init; } = true;

        public bool Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY, bool isInertial)
        {
            Calls.Add("move");
            return ManipulateResult;
        }

        public void ZoomByWheel(int wheelDelta, PanZoomPoint anchor) => Calls.Add($"wheel {wheelDelta}");
    }
}
