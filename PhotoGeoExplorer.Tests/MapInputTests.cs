using System;
using System.Threading;
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

    [Fact]
    public void ControllerTargetAppliesGesturesAndProportionalWheelZoom()
    {
        var controller = new PanZoomController(1, 10);
        controller.SetLayout(new PanZoomSize(400, 300), new PanZoomSize(400, 300));
        var target = new PanZoomControllerTarget(controller, 1.1);

        Assert.True(target.ZoomByWheel(240, new PanZoomPoint(200, 150), isHorizontal: false));
        Assert.Equal(1.21, controller.Transform.Scale, 1e-9);

        // 横のホイールは受けない
        Assert.False(target.ZoomByWheel(240, new PanZoomPoint(200, 150), isHorizontal: true));
        Assert.Equal(1.21, controller.Transform.Scale, 1e-9);

        target.Manipulate(new PanZoomPoint(200, 150), 1, 10, 0);
        target.CompleteManipulation();
        Assert.Equal((-42.0) + 10, controller.Transform.TranslateX, 1e-9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void ControllerTargetRejectsAnInvalidWheelFactor(double factor)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PanZoomControllerTarget(new PanZoomController(1, 2), factor));

    [Fact]
    public void SurfaceTargetForwardsToTheSurfaceAndIgnoresInputWithoutOne()
    {
        using var fake = new FakeMapSurface();
        IMapSurface? surface = fake;
        var target = new MapSurfaceInputTarget(() => surface);

        target.Manipulate(new PanZoomPoint(1, 2), 1.5, 3, 4);
        target.CompleteManipulation();
        Assert.True(target.ZoomByWheel(-120, new PanZoomPoint(5, 6), isHorizontal: false));
        Assert.True(target.ZoomByWheel(120, new PanZoomPoint(7, 8), isHorizontal: true));
        surface = null;
        target.Manipulate(new PanZoomPoint(1, 2), 1.5, 3, 4);
        target.CompleteManipulation();
        target.ZoomByWheel(120, new PanZoomPoint(5, 6), isHorizontal: false);

        Assert.Equal((new PanZoomPoint(1, 2), 1.5, 3.0, 4.0), Assert.Single(fake.Gestures));
        Assert.Equal(1, fake.CompleteGestureCount);
        // 地図は、横のホイールもズームにする
        Assert.Equal([(-120, new PanZoomPoint(5, 6)), (120, new PanZoomPoint(7, 8))], fake.Wheels);
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
            surface.ApplyGesture(pivot, scale, tx, ty);
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
            surface.ApplyGesture(pivot, scale, 0, 0);
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
        surface.ApplyGesture(new PanZoomPoint(700, 100), 2, 50, 60);
        var locked = surface.View;
        surface.RestorePan();
        surface.ApplyGesture(new PanZoomPoint(700, 100), 1, 50, 0);

        Assert.Equal(before.CenterX, locked.CenterX, 1e-6);
        Assert.Equal(before.CenterY, locked.CenterY, 1e-6);
        Assert.Equal(before.Resolution / 2, locked.Resolution, 1e-12);
        Assert.Equal(locked.CenterX - (50 * locked.Resolution), surface.View.CenterX, 1e-6);
    }

    [Fact]
    public void SurfaceSlidesAfterAFastDragAndKeepsTheControlLocked()
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        var fetches = 0;

        Drag(surface, stepDip: 40, intervalMilliseconds: 10);
        var released = surface.View;
        map.Navigator.FetchRequested += (_, _) => fetches++;
        surface.CompleteGesture();
        RunAnimations(map);

        // 指の向き（右）へ地図が滑るため、中心は西へ動く。滑り終えたらタイルを取得する
        Assert.True(surface.View.CenterX < released.CenterX - (50 * released.Resolution));
        Assert.Equal(released.CenterY, surface.View.CenterY, 1e-6);
        Assert.True(fetches > 0);
        Assert.True(map.Navigator.PanLock);
    }

    [Theory]
    [InlineData("slow")]
    [InlineData("panStopped")]
    [InlineData("panStoppedUntilRelease")]
    public void SurfaceDoesNotSlideAfterASlowDragOrWhilePanIsStopped(string situation)
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        if (situation != "slow")
        {
            surface.LockPan();
        }

        Drag(surface, stepDip: situation == "slow" ? 2 : 40, intervalMilliseconds: 20);
        if (situation == "panStoppedUntilRelease")
        {
            // 矩形選択は、ボタンを離すとすぐにパンの停止を解く
            surface.RestorePan();
        }

        var released = surface.View;
        surface.CompleteGesture();
        RunAnimations(map);

        Assert.Equal(released.CenterX, surface.View.CenterX, 1e-6);
    }

    [Fact]
    public void SurfaceForgetsTheSpeedOfTheFormerGesture()
    {
        using var map = CreateMap();
        using var surface = new MapsuiMapSurface(map);
        Drag(surface, stepDip: 40, intervalMilliseconds: 10);
        surface.CompleteGesture();
        RunAnimations(map);
        var settled = surface.View;

        surface.CompleteGesture();
        RunAnimations(map);

        Assert.Equal(settled.CenterX, surface.View.CenterX, 1e-6);
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

    // 速さは実時刻から求めるため、間隔を空けて動かす
    private static void Drag(MapsuiMapSurface surface, double stepDip, int intervalMilliseconds)
    {
        for (var step = 0; step < 6; step++)
        {
            surface.ApplyGesture(new PanZoomPoint(100 + (step * stepDip), 100), 1, stepDip, 0);
            Thread.Sleep(intervalMilliseconds);
        }
    }

    // 終了の処理は、終了時刻を過ぎた次の更新で行われるため、動きが止まった後もしばらく更新する
    private static void RunAnimations(Map map)
    {
        for (var frame = 0; frame < 120; frame++)
        {
            map.Navigator.UpdateAnimations();
            Thread.Sleep(10);
        }
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

}
