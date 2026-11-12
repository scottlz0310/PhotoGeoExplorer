using System.Linq;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PriorityCollapseLayoutTests
{
    private const double Spacing = 8;
    private const double MinTrimmedWidth = 32;

    // 地図ヘッダーと同じ並び: 字地名(100) │ 道路名(150) │ 緯度経度(120)。区切りは幅 10
    [Theory]
    [InlineData(double.PositiveInfinity, true, true, true, "100,10,150,10,120")]
    [InlineData(422, true, true, true, "100,10,150,10,120")]
    // 1. 緯度経度を非表示にする（直前の区切りも消える）
    [InlineData(421, true, true, true, "100,10,150,-,-")]
    [InlineData(276, true, true, true, "100,10,150,-,-")]
    // 2. 道路名を詰める
    [InlineData(200, true, true, true, "100,10,74,-,-")]
    // 3. 道路名が下限に達したら、字地名を詰める
    [InlineData(100, true, true, true, "42,10,32,-,-")]
    [InlineData(10, true, true, true, "32,10,32,-,-")]
    // 表示対象でない項目は場所を空けず、区切りも出さない
    [InlineData(500, false, false, true, "-,-,-,-,120")]
    [InlineData(100, false, false, true, "-,-,-,-,-")]
    [InlineData(500, true, false, true, "100,10,-,-,120")]
    [InlineData(500, false, true, true, "-,-,150,10,120")]
    [InlineData(500, true, true, false, "100,10,150,-,-")]
    [InlineData(500, true, false, false, "100,-,-,-,-")]
    [InlineData(500, false, false, false, "-,-,-,-,-")]
    public void ComputeCollapsesInPriorityOrder(
        double availableWidth,
        bool hasAreaName,
        bool hasRoadName,
        bool hasCoordinates,
        string expected)
    {
        var items = new[]
        {
            new PriorityCollapseItem(100, Priority: 1, PriorityCollapseMode.Trim, IsPresent: hasAreaName),
            new PriorityCollapseItem(10, IsSeparator: true),
            new PriorityCollapseItem(150, Priority: 2, PriorityCollapseMode.Trim, IsPresent: hasRoadName),
            new PriorityCollapseItem(10, IsSeparator: true),
            new PriorityCollapseItem(120, Priority: 3, PriorityCollapseMode.Hide, IsPresent: hasCoordinates)
        };

        var widths = PriorityCollapseLayout.Compute(items, availableWidth, Spacing, MinTrimmedWidth);

        Assert.Equal(expected, Describe(widths));
    }

    [Fact]
    public void ComputeNeverCollapsesItemsInKeepMode()
    {
        var items = new[]
        {
            new PriorityCollapseItem(100),
            new PriorityCollapseItem(100, Priority: 1, PriorityCollapseMode.Hide)
        };

        var widths = PriorityCollapseLayout.Compute(items, availableWidth: 50, Spacing, MinTrimmedWidth);

        Assert.Equal("100,-", Describe(widths));
    }

    [Fact]
    public void ComputeDoesNotWidenItemsNarrowerThanTheMinimumTrimmedWidth()
    {
        var items = new[] { new PriorityCollapseItem(20, Priority: 1, PriorityCollapseMode.Trim) };

        var widths = PriorityCollapseLayout.Compute(items, availableWidth: 5, Spacing, MinTrimmedWidth);

        Assert.Equal("20", Describe(widths));
    }

    private static string Describe(double?[] widths)
        => string.Join(',', widths.Select(width => width?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"));
}
