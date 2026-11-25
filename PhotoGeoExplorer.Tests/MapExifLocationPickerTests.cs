using System;
using System.Threading.Tasks;
using PhotoGeoExplorer.Panes.Map;
using PhotoGeoExplorer.Services.MapRendering;
using Windows.Foundation;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapExifLocationPickerTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ConstructorThrowsWhenCallbackIsNull(bool canPickIsNull, bool hideIsNull, bool restoreIsNull)
    {
        Assert.Throws<ArgumentNullException>(() => new MapExifLocationPicker(
            canPickIsNull ? null! : () => true,
            hideIsNull ? null! : () => false,
            restoreIsNull ? null! : () => { }));
    }

    [Fact]
    public async Task PickExifLocationAsyncReturnsNullWhenCannotPick()
    {
        var harness = new PickerHarness(canPick: false);

        var result = await harness.Picker.PickExifLocationAsync().ConfigureAwait(true);

        Assert.Null(result);
        Assert.False(harness.Picker.IsPicking);
        Assert.Equal(0, harness.HideCallCount);
    }

    [Fact]
    public void PickExifLocationAsyncReturnsSameTaskWhilePicking()
    {
        var harness = new PickerHarness();

        var first = harness.Picker.PickExifLocationAsync();
        var second = harness.Picker.PickExifLocationAsync();

        Assert.Same(first, second);
        Assert.True(harness.Picker.IsPicking);
        Assert.Equal(1, harness.HideCallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickExifLocationAsyncTracksStatusRestoreOnlyWhenStatusWasHidden(bool statusWasHidden)
    {
        var harness = new PickerHarness(hideResult: statusWasHidden);

        _ = harness.Picker.PickExifLocationAsync();

        Assert.Equal(statusWasHidden, harness.Picker.IsStatusRestorePending);
    }

    [Fact]
    public async Task HandlePointerPressedWithRightButtonCancelsPick()
    {
        var harness = new PickerHarness();
        var pickTask = harness.Picker.PickExifLocationAsync();

        var handled = harness.Picker.HandlePointerPressed(
            isLeftButtonPressed: false,
            isRightButtonPressed: true,
            position: new Point(10, 10));

        Assert.True(handled);
        Assert.False(harness.Picker.IsPicking);
        Assert.False(harness.Picker.IsStatusRestorePending);
        Assert.Equal(1, harness.RestoreCallCount);
        Assert.Null(await pickTask.ConfigureAwait(true));
    }

    [Fact]
    public void HandlePointerPressedWithLeftButtonIsNotHandled()
    {
        var harness = new PickerHarness();
        _ = harness.Picker.PickExifLocationAsync();

        var handled = harness.Picker.HandlePointerPressed(
            isLeftButtonPressed: true,
            isRightButtonPressed: false,
            position: new Point(10, 10));

        Assert.False(handled);
        Assert.True(harness.Picker.IsPicking);
    }

    [Fact]
    public async Task HandlePointerReleasedCompletesPickWithTheGeoPosition()
    {
        var harness = new PickerHarness();
        var pickTask = harness.Picker.PickExifLocationAsync();
        var position = new GeoPoint(35.681236, 139.767125);

        harness.Picker.HandlePointerPressed(true, false, new Point(100, 100));
        var handled = harness.Picker.HandlePointerReleased(new Point(103, 98), () => position);

        Assert.True(handled);
        Assert.False(harness.Picker.IsPicking);
        Assert.Equal(1, harness.RestoreCallCount);
        var result = await pickTask.ConfigureAwait(true);
        Assert.NotNull(result);
        Assert.Equal(position.Latitude, result.Value.Latitude);
        Assert.Equal(position.Longitude, result.Value.Longitude);
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(0, 7)]
    [InlineData(-7, -7)]
    public void HandlePointerReleasedIgnoresDragBeyondThreshold(double deltaX, double deltaY)
    {
        var harness = new PickerHarness();
        _ = harness.Picker.PickExifLocationAsync();

        harness.Picker.HandlePointerPressed(true, false, new Point(100, 100));
        var handled = harness.Picker.HandlePointerReleased(
            new Point(100 + deltaX, 100 + deltaY),
            () => new GeoPoint(0, 0));

        Assert.False(handled);
        Assert.True(harness.Picker.IsPicking);
        Assert.Equal(0, harness.RestoreCallCount);
    }

    [Fact]
    public void HandlePointerReleasedWithoutActivePointerIsIgnored()
    {
        var harness = new PickerHarness();
        _ = harness.Picker.PickExifLocationAsync();

        var handled = harness.Picker.HandlePointerReleased(new Point(0, 0), () => new GeoPoint(0, 0));

        Assert.False(handled);
        Assert.True(harness.Picker.IsPicking);
    }

    [Fact]
    public void HandlePointerReleasedKeepsPickingWhenWorldPositionIsUnavailable()
    {
        var harness = new PickerHarness();
        _ = harness.Picker.PickExifLocationAsync();

        harness.Picker.HandlePointerPressed(true, false, new Point(100, 100));
        var handled = harness.Picker.HandlePointerReleased(new Point(100, 100), () => null);

        Assert.False(handled);
        Assert.True(harness.Picker.IsPicking);
    }

    [Fact]
    public void HandlePointerReleasedThrowsWhenWorldPositionResolverIsNull()
    {
        var harness = new PickerHarness();

        Assert.Throws<ArgumentNullException>(() =>
            harness.Picker.HandlePointerReleased(new Point(0, 0), null!));
    }

    [Fact]
    public void CancelWithoutPendingPickDoesNotRestoreStatus()
    {
        var harness = new PickerHarness();

        harness.Picker.Cancel();

        Assert.Equal(0, harness.RestoreCallCount);
        Assert.False(harness.Picker.IsPicking);
    }

    [Fact]
    public async Task HandlePointerCaptureLostKeepsPickingAndAllowsRetry()
    {
        var harness = new PickerHarness();
        var pickTask = harness.Picker.PickExifLocationAsync();

        harness.Picker.HandlePointerPressed(true, false, new Point(100, 100));
        harness.Picker.HandlePointerCaptureLost();
        var handledAfterLost = harness.Picker.HandlePointerReleased(new Point(100, 100), () => new GeoPoint(0, 0));

        Assert.False(handledAfterLost);
        Assert.True(harness.Picker.IsPicking);

        harness.Picker.HandlePointerPressed(true, false, new Point(50, 50));
        var handledRetry = harness.Picker.HandlePointerReleased(new Point(50, 50), () => new GeoPoint(0, 0));

        Assert.True(handledRetry);
        Assert.NotNull(await pickTask.ConfigureAwait(true));
    }

    private sealed class PickerHarness
    {
        public PickerHarness(bool canPick = true, bool hideResult = true)
        {
            Picker = new MapExifLocationPicker(
                () => canPick,
                () =>
                {
                    HideCallCount++;
                    return hideResult;
                },
                () => RestoreCallCount++);
        }

        public MapExifLocationPicker Picker { get; }

        public int HideCallCount { get; private set; }

        public int RestoreCallCount { get; private set; }
    }
}
