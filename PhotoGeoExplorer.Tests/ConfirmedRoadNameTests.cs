using PhotoGeoExplorer.Models;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class ConfirmedRoadNameTests
{
    [Theory]
    [InlineData("山の手通り", "203")]
    [InlineData(null, "1167")]
    [InlineData("丸山通", null)]
    public void FromKeepsTheNameAndTheRouteNumberOfTheCandidate(string? name, string? route)
    {
        var candidate = new RoadNameCandidate(name, route, DistanceMeters: 12.4);

        var road = ConfirmedRoadName.From(candidate);

        Assert.Equal(new ConfirmedRoadName(name, route), road);
        Assert.True(road.Matches(candidate));
    }

    [Theory]
    [InlineData("山の手通り", "203", "山の手通り", "204")]
    [InlineData("山の手通り", "203", "丸山通", "203")]
    [InlineData("山の手通り", null, null, "203")]
    public void MatchesComparesBothTheNameAndTheRouteNumber(string? name, string? route, string? otherName, string? otherRoute)
    {
        var road = new ConfirmedRoadName(name, route);

        Assert.False(road.Matches(new RoadNameCandidate(otherName, otherRoute, DistanceMeters: 5)));
    }

    [Fact]
    public void MatchesIgnoresTheDistance()
    {
        var road = new ConfirmedRoadName("丸山通", null);

        Assert.True(road.Matches(new RoadNameCandidate("丸山通", null, DistanceMeters: 49)));
    }

    [Fact]
    public void MethodsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => ConfirmedRoadName.From(null!));
        Assert.Throws<ArgumentNullException>(() => new ConfirmedRoadName("丸山通", null).Matches(null!));
    }
}
