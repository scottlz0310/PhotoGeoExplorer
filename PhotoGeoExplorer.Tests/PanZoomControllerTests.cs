using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Services.PanZoom;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class PanZoomControllerTests
{
    private const int Precision = 9;

    private static PanZoomController CreateFitted(double viewportWidth = 800, double viewportHeight = 600, double contentWidth = 800, double contentHeight = 600)
    {
        var controller = new PanZoomController(1.0, 6.0);
        controller.SetLayout(new PanZoomSize(viewportWidth, viewportHeight), new PanZoomSize(contentWidth, contentHeight));
        return controller;
    }

    private static List<PanZoomTransform> RecordChanges(PanZoomController controller)
    {
        var changes = new List<PanZoomTransform>();
        controller.Changed += (_, transform) => changes.Add(transform);
        return changes;
    }

    [Theory]
    [InlineData(0.0, 6.0)]
    [InlineData(-1.0, 6.0)]
    [InlineData(1.5, 6.0)]
    [InlineData(double.NaN, 6.0)]
    [InlineData(1.0, 0.5)]
    [InlineData(1.0, double.PositiveInfinity)]
    [InlineData(1.0, double.NaN)]
    public void ConstructorRejectsScaleRangesThatExcludeFit(double minScale, double maxScale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanZoomController(minScale, maxScale));
    }

    [Theory]
    [InlineData(800, 600, 1000, 500, 0, 100, 800, 400)]
    [InlineData(800, 600, 400, 600, 200, 0, 400, 600)]
    [InlineData(800, 600, 100, 75, 0, 0, 800, 600)]
    [InlineData(800, 600, 1600, 1200, 0, 0, 800, 600)]
    public void SetLayoutPlacesContentCenteredInsideTheViewport(
        double viewportWidth,
        double viewportHeight,
        double contentWidth,
        double contentHeight,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        var controller = CreateFitted(viewportWidth, viewportHeight, contentWidth, contentHeight);

        Assert.Equal(new PanZoomRect(expectedX, expectedY, expectedWidth, expectedHeight), controller.ContentBounds);
        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
    }

    [Theory]
    [InlineData(2.0, 200, 150)]
    [InlineData(1.5, 0, 0)]
    [InlineData(3.0, 799, 599)]
    [InlineData(6.0, 400, 300)]
    public void ZoomAtKeepsTheContentUnderTheAnchorFixed(double factor, double anchorX, double anchorY)
    {
        var controller = CreateFitted();
        var anchor = new PanZoomPoint(anchorX, anchorY);
        var contentBefore = controller.Transform.ToContent(anchor);

        controller.ZoomAt(factor, anchor);

        Assert.Equal(factor, controller.Transform.Scale, Precision);
        var viewAfter = controller.Transform.ToView(contentBefore);
        Assert.Equal(anchor.X, viewAfter.X, Precision);
        Assert.Equal(anchor.Y, viewAfter.Y, Precision);
    }

    [Theory]
    [InlineData(100.0, 6.0)]
    [InlineData(6.0, 6.0)]
    public void ZoomAtClampsToTheMaximumScale(double factor, double expectedScale)
    {
        var controller = CreateFitted();

        controller.ZoomAt(factor, new PanZoomPoint(400, 300));

        Assert.Equal(expectedScale, controller.Transform.Scale, Precision);
    }

    [Fact]
    public void ZoomOutBelowFitIsIgnoredWithoutRaisingChanged()
    {
        var controller = CreateFitted();
        var changes = RecordChanges(controller);

        controller.ZoomAt(0.5, new PanZoomPoint(400, 300));

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
        Assert.Empty(changes);
    }

    [Fact]
    public void ZoomOutFromZoomedStateReturnsToFitAndRecentersTheContent()
    {
        var controller = CreateFitted();
        controller.ZoomAt(2.0, new PanZoomPoint(100, 100));

        controller.ZoomAt(0.5, new PanZoomPoint(700, 500));

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
    }

    [Theory]
    [InlineData(-5000, 0, -800, -300)]
    [InlineData(5000, 0, 0, -300)]
    [InlineData(0, -5000, -400, -600)]
    [InlineData(0, 5000, -400, 0)]
    [InlineData(-100, -50, -500, -350)]
    public void PanByStopsAtTheContentEdgesWhenZoomedIn(double deltaX, double deltaY, double expectedX, double expectedY)
    {
        // 中心基準で 2 倍にすると平行移動は (-400, -300) で、動ける範囲は x が [-800, 0]、y が [-600, 0]
        var controller = CreateFitted();
        controller.ZoomAtCenter(2.0);

        controller.PanBy(deltaX, deltaY);

        Assert.Equal(expectedX, controller.Transform.TranslateX, Precision);
        Assert.Equal(expectedY, controller.Transform.TranslateY, Precision);
    }

    [Fact]
    public void PanByCannotMoveContentThatFitsTheViewport()
    {
        var controller = CreateFitted();
        var changes = RecordChanges(controller);

        controller.PanBy(120, -80);

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
        Assert.Empty(changes);
    }

    [Fact]
    public void AxisSmallerThanTheViewportStaysCenteredWhileTheOtherAxisPans()
    {
        // 横長の画像（表示枠は 800×400）。1.2 倍で高さ 480 は 600 未満のため、縦は動かせない。幅 960 は 800 を超えるので横は動かせる
        var controller = CreateFitted(contentWidth: 1000, contentHeight: 500);
        controller.ZoomAtCenter(1.2);

        controller.PanBy(-30, 100);

        Assert.Equal(-110.0, controller.Transform.TranslateX, Precision);
        Assert.Equal(-60.0, controller.Transform.TranslateY, Precision);
    }

    [Fact]
    public void ManipulateAppliesScaleAroundThePivotThenTranslatesInOneChange()
    {
        var controller = CreateFitted();
        var changes = RecordChanges(controller);

        controller.Manipulate(new PanZoomPoint(400, 300), 2.0, -100, 50);

        var expected = new PanZoomTransform(2.0, -500, -250);
        Assert.Equal(expected.Scale, controller.Transform.Scale, Precision);
        Assert.Equal(expected.TranslateX, controller.Transform.TranslateX, Precision);
        Assert.Equal(expected.TranslateY, controller.Transform.TranslateY, Precision);
        Assert.Single(changes);
    }

    [Fact]
    public void ManipulateDoesNotTranslateWhenTheScaleIsClamped()
    {
        var controller = CreateFitted();
        controller.ZoomAtCenter(6.0);
        var before = controller.Transform;

        controller.Manipulate(new PanZoomPoint(400, 300), 2.0, 0, 0);

        Assert.Equal(before, controller.Transform);
    }

    [Theory]
    [InlineData(120.0, 1.1)]
    [InlineData(240.0, 1.21)]
    [InlineData(60.0, 1.0488088481701516)]
    public void ZoomByWheelConvertsTheWheelDeltaToAScaleProportionally(double wheelDelta, double expectedScale)
    {
        var controller = CreateFitted();

        controller.ZoomByWheel(wheelDelta, 1.1, new PanZoomPoint(400, 300));

        Assert.Equal(expectedScale, controller.Transform.Scale, Precision);
    }

    [Fact]
    public void ZoomByWheelOutReversesZoomByWheelIn()
    {
        var controller = CreateFitted();
        var anchor = new PanZoomPoint(250, 120);
        controller.ZoomByWheel(120, 1.1, anchor);
        controller.ZoomByWheel(120, 1.1, anchor);

        controller.ZoomByWheel(-240, 1.1, anchor);

        Assert.Equal(1.0, controller.Transform.Scale, Precision);
        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFactorsAreRejected(double factor)
    {
        var controller = CreateFitted();

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ZoomAt(factor, new PanZoomPoint(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ZoomByWheel(120, factor, new PanZoomPoint(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Manipulate(new PanZoomPoint(0, 0), factor, 0, 0));
    }

    [Fact]
    public void ZoomAtCenterRejectsAnInvalidFactorEvenBeforeTheLayoutIsKnown()
    {
        var controller = new PanZoomController(1.0, 6.0);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => controller.ZoomAtCenter(double.NaN));

        Assert.Equal("factor", exception.ParamName);
    }

    [Fact]
    public void FitReturnsToIdentityAndRaisesChanged()
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(100, 100));
        var changes = RecordChanges(controller);

        controller.Fit();

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
        Assert.Single(changes);
    }

    [Fact]
    public void FitWhenAlreadyFittedDoesNotRaiseChanged()
    {
        var controller = CreateFitted();
        var changes = RecordChanges(controller);

        controller.Fit();

        Assert.Empty(changes);
    }

    [Fact]
    public void OperationsAreIgnoredUntilTheLayoutIsKnown()
    {
        var controller = new PanZoomController(1.0, 6.0);
        var changes = RecordChanges(controller);

        controller.PanBy(10, 10);
        controller.ZoomAt(2.0, new PanZoomPoint(0, 0));
        controller.ZoomAtCenter(2.0);

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
        Assert.Empty(changes);
    }

    [Fact]
    public void ZoomedStateSurvivesATemporarilyEmptyViewport()
    {
        var controller = CreateFitted();
        controller.ZoomAt(2.0, new PanZoomPoint(200, 150));
        var zoomed = controller.Transform;

        controller.SetLayout(new PanZoomSize(0, 0), new PanZoomSize(800, 600));
        controller.SetLayout(new PanZoomSize(800, 600), new PanZoomSize(800, 600));

        Assert.Equal(zoomed.Scale, controller.Transform.Scale, Precision);
        Assert.Equal(zoomed.TranslateX, controller.Transform.TranslateX, Precision);
        Assert.Equal(zoomed.TranslateY, controller.Transform.TranslateY, Precision);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(400, 300)]
    [InlineData(1000, 600)]
    public void SetLayoutKeepsTheContentPointAtTheViewportCenterWhenTheViewportResizes(double newWidth, double newHeight)
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(200, 150));
        var centerBefore = NormalizedViewportCenter(controller);

        controller.SetLayout(new PanZoomSize(newWidth, newHeight), new PanZoomSize(800, 600));

        var centerAfter = NormalizedViewportCenter(controller);
        Assert.Equal(centerBefore.X, centerAfter.X, Precision);
        Assert.Equal(centerBefore.Y, centerAfter.Y, Precision);
        Assert.Equal(3.0, controller.Transform.Scale, Precision);
    }

    [Fact]
    public void SetLayoutWhileFittedStaysFitted()
    {
        var controller = CreateFitted();

        controller.SetLayout(new PanZoomSize(1280, 720), new PanZoomSize(800, 600));

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(400, 300)]
    [InlineData(1000, 600)]
    public void SetLayoutRestoresTheCenterWhenReturningFromAnEmptyLayoutAtADifferentSize(double newWidth, double newHeight)
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(700, 500));
        var centerBefore = NormalizedViewportCenter(controller);

        controller.SetLayout(new PanZoomSize(0, 0), new PanZoomSize(800, 600));
        controller.SetLayout(new PanZoomSize(newWidth, newHeight), new PanZoomSize(800, 600));

        var centerAfter = NormalizedViewportCenter(controller);
        Assert.Equal(centerBefore.X, centerAfter.X, Precision);
        Assert.Equal(centerBefore.Y, centerAfter.Y, Precision);
        Assert.Equal(3.0, controller.Transform.Scale, Precision);
    }

    [Fact]
    public void SetLayoutKeepsThePendingCenterAcrossConsecutiveEmptyLayouts()
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(700, 500));
        var centerBefore = NormalizedViewportCenter(controller);

        controller.SetLayout(new PanZoomSize(0, 0), new PanZoomSize(800, 600));
        controller.SetLayout(new PanZoomSize(1200, 900), new PanZoomSize(0, 0));
        controller.SetLayout(new PanZoomSize(0, 0), new PanZoomSize(0, 0));
        controller.SetLayout(new PanZoomSize(1600, 1200), new PanZoomSize(800, 600));

        var centerAfter = NormalizedViewportCenter(controller);
        Assert.Equal(centerBefore.X, centerAfter.X, Precision);
        Assert.Equal(centerBefore.Y, centerAfter.Y, Precision);
    }

    [Fact]
    public void FitWhileTheLayoutIsEmptyDiscardsThePendingCenter()
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(700, 500));
        controller.SetLayout(new PanZoomSize(0, 0), new PanZoomSize(800, 600));

        controller.Fit();
        controller.SetLayout(new PanZoomSize(1600, 1200), new PanZoomSize(800, 600));

        Assert.Equal(PanZoomTransform.Identity, controller.Transform);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteInputsAreRejectedWithoutCorruptingTheTransform(double value)
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(200, 150));
        var before = controller.Transform;
        var changes = RecordChanges(controller);
        var anchor = new PanZoomPoint(100, 100);

        var rejected = new (Action Call, string ParamName)[]
        {
            (() => controller.PanBy(value, 0), "deltaX"),
            (() => controller.PanBy(0, value), "deltaY"),
            (() => controller.ZoomAt(1.5, new PanZoomPoint(value, 0)), "anchor"),
            (() => controller.ZoomAt(1.5, new PanZoomPoint(0, value)), "anchor"),
            (() => controller.ZoomByWheel(value, 1.1, anchor), "wheelDelta"),
            (() => controller.ZoomByWheel(120, 1.1, new PanZoomPoint(value, 0)), "anchor"),
            (() => controller.Manipulate(new PanZoomPoint(value, 0), 1.0, 0, 0), "pivot"),
            (() => controller.Manipulate(new PanZoomPoint(0, value), 1.0, 0, 0), "pivot"),
            (() => controller.Manipulate(anchor, 1.0, value, 0), "translationX"),
            (() => controller.Manipulate(anchor, 1.0, 0, value), "translationY"),
        };

        foreach (var (call, paramName) in rejected)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(call);
            Assert.Equal(paramName, exception.ParamName);
        }

        Assert.Equal(before, controller.Transform);
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(1e10, 6.0)]
    [InlineData(1e300, 6.0)]
    [InlineData(-1e10, 1.0)]
    [InlineData(-1e300, 1.0)]
    public void ZoomByWheelWithAnExtremeFiniteDeltaClampsToTheScaleLimits(double wheelDelta, double expectedScale)
    {
        var controller = CreateFitted();
        controller.ZoomAtCenter(2.0);

        controller.ZoomByWheel(wheelDelta, 1.1, new PanZoomPoint(300, 200));

        Assert.Equal(expectedScale, controller.Transform.Scale, Precision);
        Assert.True(double.IsFinite(controller.Transform.TranslateX));
        Assert.True(double.IsFinite(controller.Transform.TranslateY));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 600.0, 800.0, 600.0)]
    [InlineData(double.NaN, 600.0, 800.0, 600.0)]
    [InlineData(800.0, 600.0, double.PositiveInfinity, 600.0)]
    [InlineData(1e308, 1e308, 1e-308, 1e-308)]
    public void SetLayoutWithNonFiniteOrOverflowingSizesKeepsTheTransformFinite(
        double viewportWidth,
        double viewportHeight,
        double contentWidth,
        double contentHeight)
    {
        var controller = CreateFitted();
        controller.ZoomAt(3.0, new PanZoomPoint(200, 150));
        var before = controller.Transform;

        controller.SetLayout(new PanZoomSize(viewportWidth, viewportHeight), new PanZoomSize(contentWidth, contentHeight));
        controller.PanBy(50, 50);
        controller.ZoomAtCenter(1.5);

        Assert.True(controller.ContentBounds.IsEmpty);
        Assert.Equal(before, controller.Transform);
    }

    [Theory]
    [InlineData(800.0, 600.0, false)]
    [InlineData(0.0, 600.0, true)]
    [InlineData(800.0, -1.0, true)]
    [InlineData(double.NaN, 600.0, true)]
    [InlineData(double.PositiveInfinity, 600.0, true)]
    [InlineData(800.0, double.NegativeInfinity, true)]
    public void SizeAndRectTreatNonPositiveAndNonFiniteDimensionsAsEmpty(double width, double height, bool expectedEmpty)
    {
        Assert.Equal(expectedEmpty, new PanZoomSize(width, height).IsEmpty);
        Assert.Equal(expectedEmpty, new PanZoomRect(0, 0, width, height).IsEmpty);
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(0.0, double.NegativeInfinity)]
    public void RectTreatsNonFinitePositionsAsEmpty(double x, double y)
    {
        Assert.True(new PanZoomRect(x, y, 800, 600).IsEmpty);
    }

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 0.0, 0.0, true)]
    [InlineData(10.5, -3.0, 0.5, -20.0, 8.0, true)]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, -1.0, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, double.NaN, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, double.PositiveInfinity, 0.0, 0.0, false)]
    [InlineData(double.NaN, 0.0, 1.0, 0.0, 0.0, false)]
    [InlineData(0.0, double.PositiveInfinity, 1.0, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, 1.0, double.NaN, 0.0, false)]
    [InlineData(0.0, 0.0, 1.0, 0.0, double.NegativeInfinity, false)]
    public void IsValidManipulationAcceptsOnlyValuesThatManipulateAccepts(
        double pivotX,
        double pivotY,
        double scaleFactor,
        double translationX,
        double translationY,
        bool expected)
    {
        var pivot = new PanZoomPoint(pivotX, pivotY);

        Assert.Equal(expected, PanZoomController.IsValidManipulation(pivot, scaleFactor, translationX, translationY));

        // 受け付けない値は Manipulate が拒否し、受け付ける値は例外にならない（検証関数と本体の食い違いを防ぐ）
        var controller = CreateFitted();
        if (expected)
        {
            controller.Manipulate(pivot, scaleFactor, translationX, translationY);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => controller.Manipulate(pivot, scaleFactor, translationX, translationY));
        }
    }

    [Theory]
    [InlineData(0.0, 0.0, true)]
    [InlineData(-5.5, 120.0, true)]
    [InlineData(double.NaN, 0.0, false)]
    [InlineData(0.0, double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, 1.0, false)]
    public void IsValidPointRequiresFiniteCoordinates(double x, double y, bool expected)
    {
        Assert.Equal(expected, PanZoomController.IsValidPoint(new PanZoomPoint(x, y)));
    }

    private static PanZoomPoint NormalizedViewportCenter(PanZoomController controller)
    {
        var content = controller.Transform.ToContent(new PanZoomPoint(controller.Viewport.Width / 2, controller.Viewport.Height / 2));
        var bounds = controller.ContentBounds;
        return new PanZoomPoint((content.X - bounds.X) / bounds.Width, (content.Y - bounds.Y) / bounds.Height);
    }
}
