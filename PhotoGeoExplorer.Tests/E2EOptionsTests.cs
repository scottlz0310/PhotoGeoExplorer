using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class E2EOptionsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    public void IsAreaDataDownloadDisabledFollowsTheEnvironmentVariable(string? value, bool expected)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.DisableAreaDataDownloadVariable] = value };

        Assert.Equal(expected, E2EOptions.IsAreaDataDownloadDisabled(name => variables.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("1", true)]
    public void IsAreaDataDownloadDisabledReadsTheProcessEnvironmentByDefault(string? value, bool expected)
    {
        // 実アプリが使う、引数を省略した経路。このクラスだけが対象の環境変数を変更するため、並列実行でも干渉しない
        var original = Environment.GetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable);
        try
        {
            Environment.SetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable, value);

            Assert.Equal(expected, E2EOptions.IsAreaDataDownloadDisabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(E2EOptions.DisableAreaDataDownloadVariable, original);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("yes", false)]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void IsControlChannelEnabledRequiresExplicitOptIn(string? value, bool expected)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.ControlChannelVariable] = value };

        Assert.Equal(expected, E2EOptions.IsControlChannelEnabled(name => variables.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetStartupMapViewReturnsFalseWithoutErrorWhenUnset(string? value)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.MapViewVariable] = value };

        var ok = E2EOptions.TryGetStartupMapView(out _, out var error, name => variables.GetValueOrDefault(name));

        Assert.False(ok);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("43.768436,142.362436,17.5", 43.768436, 142.362436, 17.5)]
    [InlineData(" -33.8688 , 151.2093 , 12 ", -33.8688, 151.2093, 12.0)]
    [InlineData("0,0,0", 0.0, 0.0, 0.0)]
    [InlineData("90,180,22", 90.0, 180.0, 22.0)]
    public void TryGetStartupMapViewParsesValidCoordinatesAndZoom(
        string value,
        double expectedLat,
        double expectedLon,
        double expectedZoom)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.MapViewVariable] = value };

        var ok = E2EOptions.TryGetStartupMapView(out var target, out var error, name => variables.GetValueOrDefault(name));

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expectedLat, target.Latitude, 6);
        Assert.Equal(expectedLon, target.Longitude, 6);
        Assert.Equal(expectedZoom, target.Zoom, 6);
    }

    [Theory]
    [InlineData("43.768436,142.362436")]
    [InlineData("43.768436,142.362436,17,extra")]
    [InlineData("abc,142.362436,17")]
    [InlineData("91,142.362436,17")]
    [InlineData("-91,142.362436,17")]
    [InlineData("43.768436,181,17")]
    [InlineData("43.768436,-181,17")]
    [InlineData("43.768436,142.362436,-0.1")]
    [InlineData("43.768436,142.362436,22.1")]
    [InlineData("NaN,142.362436,17")]
    [InlineData("43.768436,Infinity,17")]
    public void TryGetStartupMapViewRejectsInvalidValuesWithError(string value)
    {
        var variables = new Dictionary<string, string?> { [E2EOptions.MapViewVariable] = value };

        var ok = E2EOptions.TryGetStartupMapView(out _, out var error, name => variables.GetValueOrDefault(name));

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
