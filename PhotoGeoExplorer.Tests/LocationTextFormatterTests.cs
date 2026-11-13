using System.Linq;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class LocationTextFormatterTests
{
    [Theory]
    [InlineData(43.468212, 142.482015, "43.468212, 142.482015")]
    [InlineData(43.4682124, 142.4820156, "43.468212, 142.482016")]
    [InlineData(35.5, 139.0, "35.500000, 139.000000")]
    [InlineData(-33.856784, -151.215297, "-33.856784, -151.215297")]
    [InlineData(0.0000004, -0.0000004, "0.000000, -0.000000")]
    public void FormatCoordinatesUsesSixDecimalPlaces(double latitude, double longitude, string expected)
    {
        Assert.Equal(expected, LocationTextFormatter.FormatCoordinates(latitude, longitude));
    }

    [Fact]
    public void FormatCoordinatesIgnoresCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("43.468212, 142.482015", LocationTextFormatter.FormatCoordinates(43.468212, 142.482015));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("北海道", "上富良野町", "宮町", false, "上富良野町 宮町")]
    [InlineData("北海道", "上富良野町", "宮町", true, "北海道 上富良野町 宮町")]
    [InlineData("東京都", "千代田区", "", false, "千代田区")]
    [InlineData("東京都", "千代田区", "", true, "東京都 千代田区")]
    [InlineData("", "千代田区", "丸の内", true, "千代田区 丸の内")]
    public void FormatAreaNameJoinsNonEmptyParts(
        string prefecture,
        string city,
        string town,
        bool includePrefecture,
        string expected)
    {
        var areaName = new AreaName("01460001000", prefecture, city, town, SnappedToNearest: false, CensusYear: 2020);

        Assert.Equal(expected, LocationTextFormatter.FormatAreaName(areaName, includePrefecture));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "道路1")]
    [InlineData(2, "道路1 (+1)")]
    [InlineData(3, "道路1 (+2)")]
    public void FormatRoadNameShowsFirstCandidateAndRemainingCount(int candidateCount, string expected)
    {
        var candidates = Enumerable.Range(1, candidateCount)
            .Select(index => new RoadNameCandidate($"道路{index}", Ref: null, DistanceMeters: index * 10))
            .ToArray();

        Assert.Equal(expected, LocationTextFormatter.FormatRoadName(candidates, route => $"路線番号 {route}"));
    }

    [Theory]
    [InlineData("国道53号", "53・373", "国道53号")]
    [InlineData("山の手通り", "203", "山の手通り")]
    [InlineData("丸山通", null, "丸山通")]
    [InlineData(null, "203", "路線番号 203")]
    [InlineData(null, "53・373", "路線番号 53・373")]
    public void FormatRoadCandidateUsesTheNameAndFallsBackToTheRouteNumber(string? name, string? route, string expected)
    {
        var candidate = new RoadNameCandidate(name, route, DistanceMeters: 12);

        Assert.Equal(expected, LocationTextFormatter.FormatRoadCandidate(candidate, number => $"路線番号 {number}"));
    }

    [Theory]
    [InlineData(0d, "0 m")]
    [InlineData(0.4d, "0 m")]
    [InlineData(0.5d, "1 m")]
    [InlineData(12.4d, "12 m")]
    [InlineData(49.5d, "50 m")]
    [InlineData(1234.6d, "1235 m")]
    public void FormatDistanceRoundsToWholeMeters(double meters, string expected)
    {
        Assert.Equal(expected, LocationTextFormatter.FormatDistance(meters));
    }

    [Fact]
    public void FormatDistanceIgnoresCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("1235 m", LocationTextFormatter.FormatDistance(1234.6d));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatRoadNameShowsTheRouteNumberOfARoadWithoutAName()
    {
        var candidates = new[]
        {
            new RoadNameCandidate(Name: null, Ref: "203", DistanceMeters: 5),
            new RoadNameCandidate("丸山通", Ref: null, DistanceMeters: 20),
        };

        Assert.Equal("路線番号 203 (+1)", LocationTextFormatter.FormatRoadName(candidates, route => $"路線番号 {route}"));
    }
}
