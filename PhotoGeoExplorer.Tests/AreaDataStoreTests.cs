using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Features;
using PhotoGeoExplorer.Services.AreaNames;
using Xunit;
using static PhotoGeoExplorer.Tests.AreaShapefileTestData;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 一時フォルダーに作った ZIP を取り込んで、保存・一覧・読み込み・削除を確かめる。実データには依存しない
/// </summary>
public sealed class AreaDataStoreTests : IDisposable
{
    private static readonly string[] ShapefileFileNames = ["r2ka31.dbf", "r2ka31.prj", "r2ka31.shp", "r2ka31.shx"];
    private static readonly string[] OnlyTottoriCode = ["31"];
    private static readonly string[] TokyoAndTottoriCodes = ["13", "31"];
    private static readonly string[] TokyoAndTottoriNames = ["東京都", "鳥取県"];
    private static readonly string[] ShapefileExtensions = [".shp", ".shx", ".dbf", ".prj"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhotoGeoExplorer_AreaDataStoreTests_" + Guid.NewGuid().ToString("N"));

    public AreaDataStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    private string Root => Path.Combine(_directory, "AreaData");

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task ImportAsyncInstallsThePackAndMakesItListedAndLoadable()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures());

        var info = await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(("31", "鳥取県", 2020), (info.PrefectureCode, info.PrefectureName, info.CensusYear));
        Assert.True(info.SizeBytes > 0);
        Assert.Equal(new[] { info }, await store.ListAsync(CancellationToken.None).ConfigureAwait(true));

        var dataset = await store.LoadAsync("31", CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(dataset);
        Assert.Equal("東町一丁目", dataset.Find(35.05, 134.05)?.Name.Town);
        Assert.Equal(2020, dataset.CensusYear);
    }

    [Theory]
    [InlineData("r2ka31", 2020)]
    [InlineData("R2KA31", 2020)]
    [InlineData("r1ka31", 2019)]
    [InlineData("r7ka31", 2025)]
    [InlineData("h27ka31", 2015)]
    [InlineData("h22ka31", 2010)]
    [InlineData("h7ka31", 1995)]
    public async Task ImportAsyncReadsTheCensusYearFromTheFileName(string baseName, int expectedYear)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive(baseName, GeographicProjection, AllAttributes, TottoriFeatures());

        var info = await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expectedYear, info.CensusYear);
    }

    [Theory]
    [InlineData("area")]
    [InlineData("r0ka31")]
    [InlineData("r2ka3")]
    [InlineData("r2ka311")]
    [InlineData("x2ka31")]
    [InlineData("r2")]
    [InlineData("r2ka31_copy")]
    public async Task ImportAsyncRejectsAFileNameWithoutACensusYear(string baseName)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive(baseName, GeographicProjection, AllAttributes, TottoriFeatures());

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.UnsupportedFileName, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncAcceptsFilesInsideAFolderOfTheArchive()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), folderInArchive: "data/nested");

        var info = await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("31", info.PrefectureCode);
        Assert.Equal(
            ShapefileFileNames,
            Directory.GetFiles(Path.Combine(Root, "31")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ImportAsyncIgnoresOtherFilesInTheArchive()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), extraEntries: ["readme.txt", "../evil.shp.txt"]);

        await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(4, Directory.GetFiles(Path.Combine(Root, "31")).Length);
        Assert.False(File.Exists(Path.Combine(_directory, "evil.shp.txt")));
    }

    [Theory]
    [InlineData("../..")]
    [InlineData("..")]
    [InlineData("a/../../b")]
    [InlineData("/absolute")]
    public async Task ImportAsyncNeverUsesThePathsInsideTheArchive(string folderInArchive)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), folderInArchive: folderInArchive);

        var info = await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("31", info.PrefectureCode);

        // 取り込み先のフォルダーの外（ルートの親を含む）に、構成ファイルが書かれていない
        var outside = Directory.GetFiles(_directory, "r2ka31.*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(Path.Combine(Root, "31") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !Path.GetDirectoryName(path)!.Contains("src-", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(outside);
        Assert.Equal(ShapefileFileNames, Directory.GetFiles(Path.Combine(Root, "31")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(".prj")]
    [InlineData(".dbf")]
    [InlineData(".shx")]
    public async Task ImportAsyncRejectsAnArchiveMissingAShapefilePart(string missingExtension)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), omitExtension: missingExtension);

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.InvalidArchive, exception.Error);
        Assert.Contains(missingExtension, exception.Message, StringComparison.Ordinal);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncRejectsACorruptShapefile()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), corruptDbf: true);

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Theory]
    // レビューで再現された、ライブラリが IOException ではない例外を投げる破損（不正な更新日・重複フィールド名・負の点数）と、
    // ランダム破損の実験で見つかった、巨大なメモリを確保させる破損（巨大な点数・NaN の座標）
    [InlineData("dbf-invalid-update-date")]
    [InlineData("dbf-duplicate-field-names")]
    [InlineData("shp-negative-point-count")]
    [InlineData("shp-huge-point-count")]
    [InlineData("shp-nan-coordinate")]
    [InlineData("shp-truncated")]
    public async Task ImportAsyncReportsEveryKindOfCorruptShapefileAsCorruptData(string damage)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), mutateSourceDirectory: directory =>
        {
            var dbf = Path.Combine(directory, "r2ka31.dbf");
            var shp = Path.Combine(directory, "r2ka31.shp");
            switch (damage)
            {
                case "dbf-invalid-update-date":
                    var dbfBytes = File.ReadAllBytes(dbf);
                    dbfBytes[1] = 126;
                    dbfBytes[2] = 99;
                    dbfBytes[3] = 99;
                    File.WriteAllBytes(dbf, dbfBytes);
                    break;
                case "dbf-duplicate-field-names":
                    var fieldBytes = File.ReadAllBytes(dbf);
                    Array.Copy(fieldBytes, 32, fieldBytes, 64, 11);
                    File.WriteAllBytes(dbf, fieldBytes);
                    break;
                case "shp-negative-point-count":
                    var negative = File.ReadAllBytes(shp);
                    BitConverter.GetBytes(-1).CopyTo(negative, 148);
                    File.WriteAllBytes(shp, negative);
                    break;
                case "shp-huge-point-count":
                    var huge = File.ReadAllBytes(shp);
                    BitConverter.GetBytes(int.MaxValue).CopyTo(huge, 148);
                    File.WriteAllBytes(shp, huge);
                    break;
                case "shp-nan-coordinate":
                    var nan = File.ReadAllBytes(shp);
                    BitConverter.GetBytes(double.NaN).CopyTo(nan, 156);
                    File.WriteAllBytes(shp, nan);
                    break;
                default:
                    File.WriteAllBytes(shp, File.ReadAllBytes(shp)[..102]);
                    break;
            }
        });

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncRejectsAnArchiveWithSeveralShapefiles()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), duplicateAs: "r2ka32");

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.InvalidArchive, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncRejectsAnEmptyArchiveAndAFileThatIsNotAnArchive()
    {
        var store = new AreaDataStore(Root);
        var empty = Path.Combine(_directory, "empty.zip");
        await using (await ZipFile.OpenAsync(empty, ZipArchiveMode.Create, CancellationToken.None).ConfigureAwait(true))
        {
        }

        var notZip = Path.Combine(_directory, "not-a-zip.zip");
        await File.WriteAllTextAsync(notZip, "this is not a zip archive").ConfigureAwait(true);

        foreach (var path in new[] { empty, notZip })
        {
            var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
                () => store.ImportAsync(path, CancellationToken.None)).ConfigureAwait(true);
            Assert.Equal(AreaDataFormatError.InvalidArchive, exception.Error);
        }

        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncThrowsAnIOExceptionWhenTheArchiveDoesNotExist()
    {
        var store = new AreaDataStore(Root);

        await Assert.ThrowsAnyAsync<IOException>(
            () => store.ImportAsync(Path.Combine(_directory, "missing.zip"), CancellationToken.None)).ConfigureAwait(true);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(null, "MissingProjection")]
    [InlineData(ProjectedProjection, "UnsupportedProjection")]
    public async Task ImportAsyncRejectsADataSetThatIsNotInLatitudeAndLongitude(string? projection, string expectedError)
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", projection, AllAttributes, TottoriFeatures(), omitExtension: projection is null ? ".prj" : null);

        // .prj のない ZIP は、構成ファイルの不足として先に拒否される
        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(projection is null ? "InvalidArchive" : expectedError, exception.Error.ToString());
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncRejectsADataSetLackingARequiredAttribute()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes.Where(name => name != "S_NAME").ToArray(), TottoriFeatures());

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.MissingAttribute, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncRejectsADataSetOfSeveralPrefectures()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive(
            "r2ka31",
            GeographicProjection,
            AllAttributes,
            [CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0), CreateFeature("32201005001", "別の県", hcode: 8101, west: 134.1)]);

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.MixedPrefectures, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncUsesThePrefectureOfTheDataNotOfTheFileName()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka05", GeographicProjection, AllAttributes, TottoriFeatures());

        var info = await store.ImportAsync(archive, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("31", info.PrefectureCode);
        Assert.True(Directory.Exists(Path.Combine(Root, "31")));
        Assert.False(Directory.Exists(Path.Combine(Root, "05")));
    }

    [Fact]
    public async Task ImportAsyncRejectsAnArchiveLargerThanTheLimit()
    {
        var store = new AreaDataStore(Root, maxExtractedBytes: 100);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures());

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(
            () => store.ImportAsync(archive, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.TooLarge, exception.Error);
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ImportAsyncReplacesThePackOfTheSamePrefecture()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("h27ka31", GeographicProjection, AllAttributes, TottoriFeatures(), name: "old"), CancellationToken.None).ConfigureAwait(true);

        var info = await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), name: "new"), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2020, info.CensusYear);
        var listed = await store.ListAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(new[] { info }, listed);
        Assert.Equal(
            ShapefileFileNames,
            Directory.GetFiles(Path.Combine(Root, "31")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OnlyTottoriCode, Directory.GetDirectories(Root).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task ARejectedImportKeepsTheExistingPack()
    {
        var store = new AreaDataStore(Root);
        var installed = await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), name: "good"), CancellationToken.None).ConfigureAwait(true);

        var broken = CreateArchive("h27ka31", ProjectedProjection, AllAttributes, TottoriFeatures(), name: "broken");
        await Assert.ThrowsAsync<AreaDataFormatException>(() => store.ImportAsync(broken, CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(new[] { installed }, await store.ListAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.NotNull(await store.LoadAsync("31", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ImportAsyncKeepsSeveralPrefecturesSideBySideInCodeOrder()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures(), name: "a"), CancellationToken.None).ConfigureAwait(true);
        await store.ImportAsync(
            CreateArchive("r2ka13", GeographicProjection, AllAttributes, [CreateFeature("13101001001", "丸の内一丁目", hcode: 8101, west: 139.7)], name: "b"),
            CancellationToken.None).ConfigureAwait(true);

        var listed = await store.ListAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(TokyoAndTottoriCodes, listed.Select(pack => pack.PrefectureCode).ToArray());
        Assert.Equal(TokyoAndTottoriNames, listed.Select(pack => pack.PrefectureName).ToArray());
    }

    [Fact]
    public async Task ImportAsyncRemovesLeftoverTemporaryDirectories()
    {
        Directory.CreateDirectory(Path.Combine(Root, ".import-leftover"));
        Directory.CreateDirectory(Path.Combine(Root, ".replaced-leftover"));
        var store = new AreaDataStore(Root);

        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(OnlyTottoriCode, Directory.GetDirectories(Root).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task ImportAsyncThrowsWhenAlreadyCanceledAndInstallsNothing()
    {
        var store = new AreaDataStore(Root);
        var archive = CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(archive, cancellation.Token)).ConfigureAwait(true);

        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
    }

    [Fact]
    public async Task ListAsyncIgnoresDirectoriesThatAreNotCompletePacks()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);

        // 都道府県コードではない名前、.shp だけで他の構成ファイルがない、調査年を判別できない名前
        Directory.CreateDirectory(Path.Combine(Root, "backup"));
        Directory.CreateDirectory(Path.Combine(Root, "99"));
        Directory.CreateDirectory(Path.Combine(Root, "13"));
        await File.WriteAllTextAsync(Path.Combine(Root, "13", "r2ka13.shp"), "x").ConfigureAwait(true);
        Directory.CreateDirectory(Path.Combine(Root, "14"));
        foreach (var extension in ShapefileExtensions)
        {
            await File.WriteAllTextAsync(Path.Combine(Root, "14", "area" + extension), "x").ConfigureAwait(true);
        }

        var listed = await store.ListAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(OnlyTottoriCode, listed.Select(pack => pack.PrefectureCode).ToArray());
        Assert.Null(await store.LoadAsync("13", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ListAsyncReturnsNothingBeforeAnythingIsImported()
    {
        var store = new AreaDataStore(Root);

        Assert.Empty(await store.ListAsync(CancellationToken.None).ConfigureAwait(true));
        Assert.Null(await store.LoadAsync("31", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task LoadAsyncReportsBrokenInstalledDataAsAnIOException()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(Root, "31", "r2ka31.dbf"), "broken").ConfigureAwait(true);

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(() => store.LoadAsync("31", CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.CorruptData, exception.Error);
    }

    [Fact]
    public async Task LoadAsyncRejectsDataOfADifferentPrefectureThanTheDirectory()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);
        Directory.Move(Path.Combine(Root, "31"), Path.Combine(Root, "32"));

        var exception = await Assert.ThrowsAsync<AreaDataFormatException>(() => store.LoadAsync("32", CancellationToken.None)).ConfigureAwait(true);

        Assert.Equal(AreaDataFormatError.MixedPrefectures, exception.Error);
    }

    [Fact]
    public async Task DeleteAsyncRemovesThePack()
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);

        Assert.True(await store.DeleteAsync("31", CancellationToken.None).ConfigureAwait(true));

        Assert.False(Directory.Exists(Path.Combine(Root, "31")));
        await AssertNothingInstalledAsync(store).ConfigureAwait(true);
        Assert.False(await store.DeleteAsync("31", CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("00")]
    [InlineData("48")]
    [InlineData("ab")]
    [InlineData("..")]
    [InlineData("../31")]
    public async Task DeleteAsyncRejectsAnInvalidPrefectureCodeWithoutTouchingAnything(string code)
    {
        var store = new AreaDataStore(Root);
        await store.ImportAsync(CreateArchive("r2ka31", GeographicProjection, AllAttributes, TottoriFeatures()), CancellationToken.None).ConfigureAwait(true);

        Assert.Throws<ArgumentException>(() =>
        {
            _ = store.DeleteAsync(code, CancellationToken.None);
        });

        Assert.Single(await store.ListAsync(CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ConstructorRejectsAnEmptyRootDirectory(string? root)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AreaDataStore(root!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsANonPositiveSizeLimit(long limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AreaDataStore(Root, limit));
    }

    [Fact]
    public void DefaultRootDirectoryIsUnderTheLocalApplicationDataFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoGeoExplorer", "AreaData");

        Assert.Equal(expected, AreaDataStore.DefaultRootDirectory);
    }

    private static Feature[] TottoriFeatures()
        =>
        [
            CreateFeature("31201005001", "東町一丁目", hcode: 8101, west: 134.0),
            CreateFeature("312010870", "東品治町", hcode: 8101, west: 134.1)
        ];

    private static async Task AssertNothingInstalledAsync(AreaDataStore store)
    {
        Assert.Empty(await store.ListAsync(CancellationToken.None).ConfigureAwait(true));
    }

    // 圧縮前のファイルを _directory\src-<name> へ書き出し、ZIP にまとめて返す。ZIP の内容は引数で崩せる
    private string CreateArchive(
        string baseName,
        string? projection,
        IReadOnlyCollection<string> attributeNames,
        Feature[] features,
        string name = "archive",
        string? folderInArchive = null,
        string? omitExtension = null,
        string? duplicateAs = null,
        bool corruptDbf = false,
        Action<string>? mutateSourceDirectory = null,
        IReadOnlyCollection<string>? extraEntries = null)
    {
        var sourceDirectory = Path.Combine(_directory, "src-" + name + "-" + Guid.NewGuid().ToString("N"));
        WriteShapefile(sourceDirectory, baseName, projection, attributeNames, features);

        if (corruptDbf)
        {
            File.WriteAllText(Path.Combine(sourceDirectory, baseName + ".dbf"), "broken");
        }

        mutateSourceDirectory?.Invoke(sourceDirectory);

        var zipPath = Path.Combine(_directory, name + "-" + Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var extension = Path.GetExtension(file);
            if (string.Equals(extension, omitExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryName = folderInArchive is null ? Path.GetFileName(file) : folderInArchive + "/" + Path.GetFileName(file);
            archive.CreateEntryFromFile(file, entryName);
            if (duplicateAs is not null && string.Equals(extension, ".shp", StringComparison.OrdinalIgnoreCase))
            {
                archive.CreateEntryFromFile(file, duplicateAs + extension);
            }
        }

        foreach (var extra in extraEntries ?? [])
        {
            var entry = archive.CreateEntry(extra);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("extra");
        }

        return zipPath;
    }

    private static string WriteShapefile(string directory, string baseName, string? projection, IReadOnlyCollection<string> attributeNames, Feature[] features)
        => AreaShapefileTestData.WriteShapefile(directory, baseName, projection, attributeNames, features);
}
