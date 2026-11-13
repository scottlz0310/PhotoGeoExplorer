using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using PhotoGeoExplorer.Services.Export;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class GeoExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "geo-export-tests-" + Guid.NewGuid().ToString("N"));
    private readonly GeoExportService _service = new();

    public GeoExportServiceTests()
    {
        Directory.CreateDirectory(Source);
    }

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "out");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task CopyAsyncCopiesIntoNewFoldersAndLeavesTheOriginalsUntouched()
    {
        var a = CreateSource("a.jpg", "AAA");
        var b = CreateSource("b.jpg", "BBB");
        var modified = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, modified);

        var result = await _service.CopyAsync(
            [new ExportPlanItem(a, Path.Combine("鳥取市", "東品治町", "a.jpg")), new ExportPlanItem(b, Path.Combine("岩美町", "浦富", "b.jpg"))],
            Destination,
            resolveConflictAsync: null,
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal((2, 0, 0, false), (result.SuccessCount, result.SkipCount, result.FailureCount, result.Canceled));
        Assert.Equal("AAA", Read(Path.Combine(Destination, "鳥取市", "東品治町", "a.jpg")));
        Assert.Equal("BBB", Read(Path.Combine(Destination, "岩美町", "浦富", "b.jpg")));
        Assert.Equal("AAA", Read(a));
        Assert.Equal("BBB", Read(b));
        Assert.Equal(["a.jpg", "b.jpg"], Directory.GetFiles(Source).Select(Path.GetFileName).Order());
        Assert.Equal(modified, File.GetLastWriteTimeUtc(Path.Combine(Destination, "鳥取市", "東品治町", "a.jpg")));
    }

    [Theory]
    [InlineData("Overwrite", "NEW", 1, 0)]
    [InlineData("Skip", "OLD", 0, 1)]
    public async Task CopyAsyncAppliesTheChoiceForAFileThatAlreadyExists(string choiceName, string expectedContent, int success, int skipped)
    {
        var source = CreateSource("a.jpg", "NEW");
        var existing = CreateDestination("a.jpg", "OLD");
        var asked = new List<string>();

        var result = await _service.CopyAsync(
            [new ExportPlanItem(source, "a.jpg")],
            Destination,
            fileName =>
            {
                asked.Add(fileName);
                return Task.FromResult(Enum.Parse<ConflictResolution>(choiceName));
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(["a.jpg"], asked);
        Assert.Equal((success, skipped, false), (result.SuccessCount, result.SkipCount, result.Canceled));
        Assert.Equal(expectedContent, Read(existing));
    }

    [Fact]
    public async Task CopyAsyncStopsAtCancelAndCopiesNothingMore()
    {
        var a = CreateSource("a.jpg", "A");
        var b = CreateSource("b.jpg", "B");
        CreateDestination("a.jpg", "OLD");

        var result = await _service.CopyAsync(
            [new ExportPlanItem(a, "a.jpg"), new ExportPlanItem(b, "b.jpg")],
            Destination,
            _ => Task.FromResult(ConflictResolution.Cancel),
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Canceled);
        Assert.Equal((0, 0), (result.SuccessCount, result.SkipCount));
        Assert.Equal("OLD", Read(Path.Combine(Destination, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(Destination, "b.jpg")));
    }

    [Fact]
    public async Task CopyAsyncAsksOnlyOnceForOverwriteAll()
    {
        var items = new List<ExportPlanItem>();
        foreach (var name in new[] { "a.jpg", "b.jpg", "c.jpg" })
        {
            items.Add(new ExportPlanItem(CreateSource(name, "NEW"), name));
            CreateDestination(name, "OLD");
        }

        var asked = 0;
        var result = await _service.CopyAsync(
            items,
            Destination,
            _ =>
            {
                asked++;
                return Task.FromResult(ConflictResolution.OverwriteAll);
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, asked);
        Assert.Equal((3, 0), (result.SuccessCount, result.SkipCount));
        Assert.All(items, item => Assert.Equal("NEW", Read(Path.Combine(Destination, item.RelativePath))));
    }

    [Fact]
    public async Task CopyAsyncAsksOnlyOnceForSkipAll()
    {
        var items = new List<ExportPlanItem>();
        foreach (var name in new[] { "a.jpg", "b.jpg" })
        {
            items.Add(new ExportPlanItem(CreateSource(name, "NEW"), name));
            CreateDestination(name, "OLD");
        }

        // 衝突のない 3 つ目は、コピーされる
        items.Add(new ExportPlanItem(CreateSource("c.jpg", "NEW"), "c.jpg"));
        var asked = 0;

        var result = await _service.CopyAsync(
            items,
            Destination,
            _ =>
            {
                asked++;
                return Task.FromResult(ConflictResolution.SkipAll);
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, asked);
        Assert.Equal((1, 2), (result.SuccessCount, result.SkipCount));
        Assert.Equal("OLD", Read(Path.Combine(Destination, "a.jpg")));
        Assert.Equal("NEW", Read(Path.Combine(Destination, "c.jpg")));
    }

    [Fact]
    public async Task CopyAsyncSkipsWithoutOverwritingWhenNoChoiceIsGiven()
    {
        var source = CreateSource("a.jpg", "NEW");
        var existing = CreateDestination("a.jpg", "OLD");

        var result = await _service.CopyAsync([new ExportPlanItem(source, "a.jpg")], Destination, null, null, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal((0, 1), (result.SuccessCount, result.SkipCount));
        Assert.Equal("OLD", Read(existing));
    }

    [Fact]
    public async Task CopyAsyncReportsAFailureAndContinuesWithTheNextFile()
    {
        var b = CreateSource("b.jpg", "B");

        var result = await _service.CopyAsync(
            [new ExportPlanItem(Path.Combine(Source, "missing.jpg"), "a.jpg"), new ExportPlanItem(b, "b.jpg")],
            Destination,
            null,
            null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal((1, 0, 1), (result.SuccessCount, result.SkipCount, result.FailureCount));
        Assert.Equal(FileOperationError.IoError, result.Failures[0].Error);
        Assert.Equal("a.jpg", result.Failures[0].FileName);
        Assert.Equal("B", Read(Path.Combine(Destination, "b.jpg")));
    }

    [Theory]
    [InlineData("..", "evil.jpg")]
    [InlineData("..", "..", "evil.jpg")]
    public async Task CopyAsyncRejectsARelativePathThatLeavesTheDestination(params string[] segments)
    {
        var source = CreateSource("a.jpg", "A");

        var result = await _service.CopyAsync(
            [new ExportPlanItem(source, Path.Combine(segments))],
            Destination,
            null,
            null,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, result.FailureCount);
        Assert.Equal(FileOperationError.InvalidName, result.Failures[0].Error);
        Assert.False(File.Exists(Path.Combine(_root, "evil.jpg")));
        Assert.False(File.Exists(Path.Combine(_root, "..", "evil.jpg")));
    }

    [Fact]
    public async Task CopyAsyncDoesNothingWhenTheSourceIsTheDestination()
    {
        var source = CreateSource("a.jpg", "A");

        var result = await _service.CopyAsync([new ExportPlanItem(source, "a.jpg")], Source, null, null, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal((0, 1, 0), (result.SuccessCount, result.SkipCount, result.FailureCount));
        Assert.Equal("A", Read(source));
    }

    [Fact]
    public async Task CopyAsyncStopsWhenTheTokenIsCanceledAndReturnsTheCountSoFar()
    {
        var items = new List<ExportPlanItem>();
        foreach (var name in new[] { "a.jpg", "b.jpg", "c.jpg" })
        {
            items.Add(new ExportPlanItem(CreateSource(name, name), name));
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress(done =>
        {
            if (done == 1)
            {
                cancellation.Cancel();
            }
        });

        var result = await _service.CopyAsync(items, Destination, null, progress, cancellation.Token).ConfigureAwait(true);

        Assert.True(result.Canceled);
        Assert.Equal(1, result.SuccessCount);
        Assert.True(File.Exists(Path.Combine(Destination, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(Destination, "b.jpg")));
    }

    [Fact]
    public async Task CopyAsyncReportsTheNumberOfProcessedFiles()
    {
        var items = new List<ExportPlanItem>();
        foreach (var name in new[] { "a.jpg", "b.jpg", "c.jpg" })
        {
            items.Add(new ExportPlanItem(CreateSource(name, name), name));
        }

        var reported = new List<int>();

        await _service.CopyAsync(items, Destination, null, new SynchronousProgress(reported.Add), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal([1, 2, 3], reported);
    }

    [Fact]
    public async Task WriteCsvAsyncWritesAFileWithABom()
    {
        var path = Path.Combine(_root, "out.csv");
        var labels = new GeoCsvLabels("F", "P", "C", "T", "K", "La", "Lo", "D", "S", "R", "W", "N", "L");

        await _service.WriteCsvAsync(
            path,
            [new PhotoGeoRecord("/src/a.jpg", null, null, PhotoAreaKind.NoLocation, null)],
            labels,
            CancellationToken.None).ConfigureAwait(true);

        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Contains("a.jpg", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteCsvAsyncOverwritesAnExistingFile()
    {
        var path = Path.Combine(_root, "out.csv");
        await File.WriteAllTextAsync(path, "old content that is much longer than the new header line should be").ConfigureAwait(true);
        var labels = new GeoCsvLabels("F", "P", "C", "T", "K", "La", "Lo", "D", "S", "R", "W", "N", "L");

        await _service.WriteCsvAsync(path, [], labels, CancellationToken.None).ConfigureAwait(true);

        Assert.DoesNotContain("old content", await File.ReadAllTextAsync(path).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyAsyncNeverOverwritesTheOriginalOfAnotherSelectedPhoto()
    {
        // 元のフォルダーへのリネーム出力で、出力先が別の選択写真の元のファイルと重なった
        var a = CreateSource("IMG_0001.jpg", "PHOTO_A");
        var b = CreateSource("20240102_Test_001.jpg", "PHOTO_B");

        var result = await _service.CopyAsync(
            [new ExportPlanItem(a, "20240102_Test_001.jpg"), new ExportPlanItem(b, "20240102_Test_002.jpg")],
            Source,
            _ => Task.FromResult(ConflictResolution.OverwriteAll),
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        // 原本 B は、そのまま残り、002 には B の内容がコピーされる
        Assert.Equal("PHOTO_B", Read(b));
        Assert.Equal("PHOTO_A", Read(a));
        Assert.Equal("PHOTO_B", Read(Path.Combine(Source, "20240102_Test_002.jpg")));
        Assert.Equal((1, 0, 1), (result.SuccessCount, result.SkipCount, result.FailureCount));
        Assert.Equal(FileOperationError.AlreadyExists, result.Failures[0].Error);
        Assert.Equal(a, result.Failures[0].Path);
    }

    [Fact]
    public async Task CopyAsyncDoesNotAskAboutAnOriginalItWillNotOverwrite()
    {
        var a = CreateSource("a.jpg", "A");
        var b = CreateSource("b.jpg", "B");
        var asked = 0;

        await _service.CopyAsync(
            [new ExportPlanItem(a, "B.JPG"), new ExportPlanItem(b, Path.Combine("sub", "b.jpg"))],
            Source,
            _ =>
            {
                asked++;
                return Task.FromResult(ConflictResolution.Overwrite);
            },
            progress: null,
            CancellationToken.None).ConfigureAwait(true);

        // 大文字小文字の違いでも、原本とみなして、上書きするかを尋ねない
        Assert.Equal(0, asked);
        Assert.Equal("B", Read(b));
    }

    [Theory]
    [InlineData("D:\\", "D:\\20240102_Test_001.jpg", true)]
    [InlineData("D:\\", "D:\\folder\\a.jpg", true)]
    [InlineData("D:\\", "E:\\a.jpg", false)]
    [InlineData("C:\\out", "C:\\out\\a.jpg", true)]
    [InlineData("C:\\out\\", "C:\\out\\a.jpg", true)]
    [InlineData("C:\\out", "C:\\OUT\\sub\\a.jpg", true)]
    // 名前が前方一致するだけの別のフォルダーは、出力先の中ではない
    [InlineData("C:\\out", "C:\\out2\\a.jpg", false)]
    [InlineData("C:\\out", "C:\\out", false)]
    [InlineData("C:\\out", "C:\\a.jpg", false)]
    public void IsPathUnderRootChecksTheBoundaryOfTheDestination(string root, string path, bool expected)
    {
        Assert.Equal(expected, GeoExportService.IsPathUnderRoot(path, root));
    }

    [Fact]
    public async Task CopyAsyncRejectsInvalidArguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CopyAsync(null!, Destination, null, null, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CopyAsync([], string.Empty, null, null, CancellationToken.None)).ConfigureAwait(true);
    }

    private static string Read(string path) => File.ReadAllText(path);

    private string CreateSource(string name, string content)
    {
        var path = Path.Combine(Source, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateDestination(string name, string content)
    {
        Directory.CreateDirectory(Destination);
        var path = Path.Combine(Destination, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
