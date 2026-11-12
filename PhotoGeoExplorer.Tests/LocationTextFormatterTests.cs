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

        Assert.Equal(expected, LocationTextFormatter.FormatRoadName(candidates));
    }
}
