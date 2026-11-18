using System.IO;
using Xunit;

namespace PhotoGeoExplorer.E2E;

public sealed class AppLogTailTests
{
    private const long ProductionMaxBytes = 1024 * 1024;
    private const int ProductionMaxFiles = 5;

    [Fact]
    public void ReadAppendedRecoversSwitchLineAfterRotationFromBoundaryLength()
    {
        using var tempDir = new TempLogDirectory();
        var log = new RotatingLogFile(
            new PhysicalLogFileSystem(),
            NullLogLock.Instance,
            tempDir.LogPath,
            ProductionMaxBytes,
            ProductionMaxFiles);

        // 1 MB 上限（1,048,576 bytes）の直前である 1,048,556 bytes から切替成功行→次のログで繰り下げを起こす。
        const string initialSeed = "INITIAL_LOG_ENTRY ";
        var initialPayload = initialSeed + new string('a', 1_048_556 - initialSeed.Length);
        log.Append(initialPayload);
        Assert.Equal(1_048_556, new FileInfo(tempDir.LogPath).Length);

        var tail = AppLogTail.Capture(tempDir.LogPath);

        const string switchLine = "2026-11-18 12:00:00.000 +09:00 [test] INFO Switched map tile source to OpenFreeMapVector (displaying OpenFreeMapVector)\n";
        const string nextLine = "2026-11-18 12:00:01.000 +09:00 [test] INFO Following log entry after rotation\n";
        log.Append(switchLine);
        log.Append(nextLine);

        var generation1Path = log.GetGenerationPath(1);
        Assert.True(File.Exists(generation1Path));
        Assert.Contains(switchLine, File.ReadAllText(generation1Path), StringComparison.Ordinal);
        Assert.DoesNotContain(switchLine, File.ReadAllText(tempDir.LogPath), StringComparison.Ordinal);

        var appended = tail.ReadAppended();
        Assert.Equal(switchLine + nextLine, appended);
        Assert.DoesNotContain(initialSeed, appended, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadAppendedTracksAppendedContentAcrossGenerations(int rotationCount)
    {
        using var tempDir = new TempLogDirectory();
        var log = new RotatingLogFile(
            new PhysicalLogFileSystem(),
            NullLogLock.Instance,
            tempDir.LogPath,
            maxBytes: 40,
            maxFiles: 5);

        const string beforeMarker = "before-capture-entry\n";
        log.Append(beforeMarker);

        var tail = AppLogTail.Capture(tempDir.LogPath);

        var expected = new System.Text.StringBuilder();
        var entriesToWrite = (rotationCount * 2) + 1;
        for (var i = 0; i < entriesToWrite; i++)
        {
            var entry = $"after-entry-{i:D2}-padding-for-rotation\n";
            log.Append(entry);
            _ = expected.Append(entry);
        }

        if (rotationCount > 0)
        {
            Assert.True(File.Exists(log.GetGenerationPath(rotationCount)));
        }

        var appended = tail.ReadAppended();
        Assert.Equal(expected.ToString(), appended);
        Assert.DoesNotContain(beforeMarker, appended, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAppendedIgnoresPreexistingGenerationsWhenCurrentLogIsAbsent()
    {
        using var tempDir = new TempLogDirectory();
        var log = new RotatingLogFile(
            new PhysicalLogFileSystem(),
            NullLogLock.Instance,
            tempDir.LogPath,
            maxBytes: 40,
            maxFiles: 5);

        const string staleSuccess = "Switched map tile source to OpenFreeMapVector (displaying OpenFreeMapVector)\n";
        log.Append(staleSuccess);
        log.Append("second-stale-entry-triggers-rotate-12345\n");

        // 直前にローテーションが起きて app.log が消え、過去の app.1.log だけが残っている状態を再現する。
        File.Delete(tempDir.LogPath);
        Assert.True(File.Exists(log.GetGenerationPath(1)));

        var tail = AppLogTail.Capture(tempDir.LogPath);
        Assert.Equal(string.Empty, tail.ReadAppended());

        const string newFirst = "new-session-entry-1-padding-to-exceed-40\n";
        const string newSecond = "new-session-entry-2-after-rotation\n";
        log.Append(newFirst);
        log.Append(newSecond);

        Assert.True(File.Exists(log.GetGenerationPath(2)));
        var appended = tail.ReadAppended();
        Assert.Equal(newFirst + newSecond, appended);
        Assert.DoesNotContain(staleSuccess, appended, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAppendedIncludesStagedRotatingLogWhenGenerationIsLocked()
    {
        using var tempDir = new TempLogDirectory();
        var log = new RotatingLogFile(
            new PhysicalLogFileSystem(),
            NullLogLock.Instance,
            tempDir.LogPath,
            maxBytes: 20,
            maxFiles: 4);

        log.Append("old-generation-1-data\n");
        log.Append("old-generation-2-data\n");

        var tail = AppLogTail.Capture(tempDir.LogPath);

        const string stagedEntry = "staged-entry-exceeds-20-bytes\n";
        const string activeEntry = "active-entry-after-staging\n";
        log.Append(stagedEntry);

        // app.1.log を Delete 共有なしでロックし、次の Append で app.rotating.log が残る状態を作る。
        using (new FileStream(log.GetGenerationPath(1), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            log.Append(activeEntry);
            Assert.True(File.Exists(log.StagingPath));

            var appended = tail.ReadAppended();
            Assert.Equal(stagedEntry + activeEntry, appended);
        }
    }

    [Fact]
    public void ReadAppendedReturnsEmptyWhenLogDirectoryOrFileDoesNotExist()
    {
        using var tempDir = new TempLogDirectory();
        var missingPath = Path.Combine(tempDir.DirectoryPath, "missing", "app.log");

        var tail = AppLogTail.Capture(missingPath);
        Assert.Equal(string.Empty, tail.ReadAppended());
    }

    [Fact]
    public void ReadAppendedReturnsRemainingGenerationsWhenAnchorIsEvicted()
    {
        using var tempDir = new TempLogDirectory();
        var log = new RotatingLogFile(
            new PhysicalLogFileSystem(),
            NullLogLock.Instance,
            tempDir.LogPath,
            maxBytes: 20,
            maxFiles: 3);

        log.Append("initial-anchor-line-exceeds-20\n");
        var tail = AppLogTail.Capture(tempDir.LogPath);

        const string entry1 = "line-1-exceeds-20-bytes-padding\n";
        const string entry2 = "line-2-exceeds-20-bytes-padding\n";
        const string entry3 = "line-3-exceeds-20-bytes-padding\n";
        log.Append(entry1);
        log.Append(entry2);
        log.Append(entry3);

        var appended = tail.ReadAppended();
        Assert.Equal(entry1 + entry2 + entry3, appended);
    }

    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerE2ETests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            LogPath = Path.Combine(DirectoryPath, "app.log");
        }

        public string DirectoryPath { get; }

        public string LogPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
