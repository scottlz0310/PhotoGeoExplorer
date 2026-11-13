using System;
using System.IO;
using System.Linq;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class ExportPlanBuilderTests
{
    private static readonly ExportFolderNames FolderNames = new("水面", "地域データなし", "位置情報なし", "名称なし");
    private static readonly ExportRenameNames RenameNames = new("水面", "地域データなし", "位置情報なし", "名称なし", "日付なし");

    private static readonly AreaName Higashi = Area("312010870", "鳥取県", "鳥取市", "東品治町");
    private static readonly AreaName Fuyo = Area("312010120", "鳥取県", "鳥取市", "扇町");
    private static readonly AreaName Iwami = Area("313010010", "鳥取県", "岩美町", "浦富");

    [Fact]
    public void FolderCopyPlanPutsEachPhotoUnderItsCityAndTown()
    {
        var plan = ExportPlanBuilder.BuildFolderCopyPlan(
        [
            Resolved("/src/a.jpg", Higashi),
            Resolved("/src/b.jpg", Fuyo),
            Resolved("/src/c.jpg", Iwami),
            Resolved("/src/d.jpg", Higashi)
        ],
        FolderNames);

        Assert.Equal(
            [
                Path.Combine("鳥取市", "扇町", "b.jpg"),
                Path.Combine("鳥取市", "東品治町", "a.jpg"),
                Path.Combine("鳥取市", "東品治町", "d.jpg"),
                Path.Combine("岩美町", "浦富", "c.jpg")
            ],
            plan.Select(item => item.RelativePath));
        Assert.Equal(["/src/b.jpg", "/src/a.jpg", "/src/d.jpg", "/src/c.jpg"], plan.Select(item => item.SourcePath));
    }

    [Fact]
    public void FolderCopyPlanUsesDedicatedFoldersForPhotosWithoutAnArea()
    {
        var plan = ExportPlanBuilder.BuildFolderCopyPlan(
        [
            Resolved("/src/land.jpg", Higashi),
            Resolved("/src/sea.jpg", Area("313010999", "鳥取県", "岩美町", "水面", isWater: true)),
            new PhotoGeoRecord("/src/nodata.jpg", null, new PhotoCoordinate(35, 139), PhotoAreaKind.NoAreaData, null),
            new PhotoGeoRecord("/src/nogps.jpg", null, null, PhotoAreaKind.NoLocation, null)
        ],
        FolderNames);

        Assert.Equal(
            [
                Path.Combine("鳥取市", "東品治町", "land.jpg"),
                Path.Combine("水面", "sea.jpg"),
                Path.Combine("地域データなし", "nodata.jpg"),
                Path.Combine("位置情報なし", "nogps.jpg")
            ],
            plan.Select(item => item.RelativePath));
    }

    [Fact]
    public void FolderCopyPlanAddsThePrefectureOnlyWhenTheSameCityNameSpansPrefectures()
    {
        var plan = ExportPlanBuilder.BuildFolderCopyPlan(
        [
            Resolved("/src/a.jpg", Area("131020010", "東京都", "中央区", "日本橋")),
            Resolved("/src/b.jpg", Area("271280010", "大阪府", "中央区", "大手前")),
            Resolved("/src/c.jpg", Higashi)
        ],
        FolderNames);

        Assert.Equal(
            [
                Path.Combine("中央区（東京都）", "日本橋", "a.jpg"),
                Path.Combine("中央区（大阪府）", "大手前", "b.jpg"),
                Path.Combine("鳥取市", "東品治町", "c.jpg")
            ],
            plan.Select(item => item.RelativePath));
    }

    [Fact]
    public void FolderCopyPlanSanitizesFolderNamesAndNamesAnEmptyTown()
    {
        var plan = ExportPlanBuilder.BuildFolderCopyPlan(
        [
            Resolved("/src/a.jpg", Area("312010001", "鳥取県", "鳥取市", "A/B")),
            Resolved("/src/b.jpg", Area("312010002", "鳥取県", "鳥取市", string.Empty))
        ],
        FolderNames);

        Assert.Equal(
            [Path.Combine("鳥取市", "A_B", "a.jpg"), Path.Combine("鳥取市", "名称なし", "b.jpg")],
            plan.Select(item => item.RelativePath));
    }

    [Fact]
    public void FolderCopyPlanNumbersFilesThatWouldOverlapInTheSameFolder()
    {
        // 別のフォルダーから選んだ同名のファイルは、同じ出力先で重なる（大文字小文字は区別しない）
        var plan = ExportPlanBuilder.BuildFolderCopyPlan(
        [
            Resolved("/x/IMG.jpg", Higashi),
            Resolved("/y/img.JPG", Higashi),
            Resolved("/z/IMG.jpg", Higashi)
        ],
        FolderNames);

        Assert.Equal(
            [
                Path.Combine("鳥取市", "東品治町", "IMG.jpg"),
                Path.Combine("鳥取市", "東品治町", "img (2).JPG"),
                Path.Combine("鳥取市", "東品治町", "IMG (3).jpg")
            ],
            plan.Select(item => item.RelativePath));
    }

    [Fact]
    public void FolderCopyPlanOfNoPhotosIsEmpty()
    {
        Assert.Empty(ExportPlanBuilder.BuildFolderCopyPlan([], FolderNames));
    }

    [Fact]
    public void RenamePlanBuildsDateAreaAndSequence()
    {
        var plan = ExportPlanBuilder.BuildRenamePlan(
        [
            Resolved("/src/b.jpg", Higashi, new DateTimeOffset(2024, 1, 2, 10, 0, 0, TimeSpan.FromHours(9))),
            Resolved("/src/a.jpg", Higashi, new DateTimeOffset(2024, 1, 2, 9, 0, 0, TimeSpan.FromHours(9))),
            Resolved("/src/c.JPG", Higashi, new DateTimeOffset(2024, 1, 3, 9, 0, 0, TimeSpan.FromHours(9))),
            Resolved("/src/d.jpg", Fuyo, new DateTimeOffset(2024, 1, 2, 11, 0, 0, TimeSpan.FromHours(9)))
        ],
        RenameNames);

        // 連番は、同じ日付・字名の中で、撮影日時の順に 001 から
        Assert.Equal(
            [
                ("/src/a.jpg", "20240102_東品治町_001.jpg"),
                ("/src/b.jpg", "20240102_東品治町_002.jpg"),
                ("/src/d.jpg", "20240102_扇町_001.jpg"),
                ("/src/c.JPG", "20240103_東品治町_001.JPG")
            ],
            plan.Select(item => (item.SourcePath, item.RelativePath)));
    }

    [Fact]
    public void RenamePlanUsesTheNamesForPhotosWithoutADateOrAnArea()
    {
        var takenAt = new DateTimeOffset(2024, 1, 2, 9, 0, 0, TimeSpan.Zero);
        var plan = ExportPlanBuilder.BuildRenamePlan(
        [
            new PhotoGeoRecord("/src/nogps.jpg", takenAt, null, PhotoAreaKind.NoLocation, null),
            new PhotoGeoRecord("/src/nodata.jpg", takenAt, new PhotoCoordinate(35, 139), PhotoAreaKind.NoAreaData, null),
            Resolved("/src/sea.jpg", Area("313010999", "鳥取県", "岩美町", "水面", isWater: true), takenAt),
            Resolved("/src/nodate.jpg", Higashi, takenAt: null)
        ],
        RenameNames);

        var names = plan.ToDictionary(item => item.SourcePath, item => item.RelativePath);
        Assert.Equal("20240102_位置情報なし_001.jpg", names["/src/nogps.jpg"]);
        Assert.Equal("20240102_地域データなし_001.jpg", names["/src/nodata.jpg"]);
        Assert.Equal("20240102_水面_001.jpg", names["/src/sea.jpg"]);
        Assert.Equal("日付なし_東品治町_001.jpg", names["/src/nodate.jpg"]);
    }
    [Theory]
    [InlineData(1, "001")]
    [InlineData(9, "009")]
    [InlineData(10, "010")]
    [InlineData(999, "999")]
    [InlineData(1000, "1000")]
    public void RenamePlanPadsTheSequenceToThreeDigits(int count, string expectedLast)
    {
        var takenAt = new DateTimeOffset(2024, 1, 2, 9, 0, 0, TimeSpan.Zero);
        var records = Enumerable.Range(0, count)
            .Select(i => Resolved($"/src/{i:D5}.jpg", Higashi, takenAt.AddSeconds(i)))
            .ToList();

        var plan = ExportPlanBuilder.BuildRenamePlan(records, RenameNames);

        Assert.Equal($"20240102_東品治町_{expectedLast}.jpg", plan[^1].RelativePath);
        Assert.Equal(count, plan.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RenamePlanSanitizesAndLimitsTheAreaName()
    {
        var longName = string.Concat(Enumerable.Repeat("あ", 100));
        var plan = ExportPlanBuilder.BuildRenamePlan(
        [
            Resolved("/src/a.jpg", Area("312010001", "鳥取県", "鳥取市", "A/B:C"), new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            Resolved("/src/b.jpg", Area("312010002", "鳥取県", "鳥取市", longName), new DateTimeOffset(2024, 1, 2, 0, 0, 1, TimeSpan.Zero)),
            Resolved("/src/c.jpg", Area("312010003", "鳥取県", "鳥取市", string.Empty), new DateTimeOffset(2024, 1, 2, 0, 0, 2, TimeSpan.Zero))
        ],
        RenameNames);

        Assert.Equal("20240102_A_B_C_001.jpg", plan[0].RelativePath);
        Assert.Equal("20240102_" + string.Concat(Enumerable.Repeat("あ", 40)) + "_001.jpg", plan[1].RelativePath);
        Assert.Equal("20240102_名称なし_001.jpg", plan[2].RelativePath);
    }

    [Fact]
    public void RenamePlanKeepsTheOriginalExtensionAndDoesNotKeepTheFolder()
    {
        var plan = ExportPlanBuilder.BuildRenamePlan(
            [Resolved("/very/deep/folder/IMG_0001.HEIC", Higashi, new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero))],
            RenameNames);

        var item = Assert.Single(plan);
        Assert.Equal("20240102_東品治町_001.HEIC", item.RelativePath);
        Assert.Equal(string.Empty, Path.GetDirectoryName(item.RelativePath));
    }

    [Fact]
    public void PlansRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => ExportPlanBuilder.BuildFolderCopyPlan(null!, FolderNames));
        Assert.Throws<ArgumentNullException>(() => ExportPlanBuilder.BuildFolderCopyPlan([], null!));
        Assert.Throws<ArgumentNullException>(() => ExportPlanBuilder.BuildRenamePlan(null!, RenameNames));
        Assert.Throws<ArgumentNullException>(() => ExportPlanBuilder.BuildRenamePlan([], null!));
    }

    private static AreaName Area(string keyCode, string prefecture, string city, string town, bool isWater = false)
        => new(keyCode, prefecture, city, town, SnappedToNearest: false, CensusYear: 2020, IsWater: isWater);

    private static PhotoGeoRecord Resolved(string path, AreaName area, DateTimeOffset? takenAt = null)
        => new(path, takenAt, new PhotoCoordinate(35, 134), PhotoAreaKind.Resolved, area);
}
