using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Tests;

public sealed class RotatingLogFileTests
{
    private const string LogPath = @"C:\Logs\app.log";

    [Theory]
    [InlineData(100, 3, 10, 40)]
    [InlineData(100, 5, 10, 200)]
    [InlineData(1000, 2, 50, 100)]
    [InlineData(64, 5, 64, 30)]
    public void AppendKeepsTotalSizeAndFileCountWithinLimit(long maxBytes, int maxFiles, int payloadLength, int writeCount)
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes, maxFiles);

        for (var i = 0; i < writeCount; i++)
        {
            log.Append(new string('x', payloadLength));
        }

        Assert.True(fileSystem.Files.Count <= maxFiles);
        Assert.All(fileSystem.Files.Values, content => Assert.True(content.Length <= maxBytes + payloadLength));
        Assert.True(fileSystem.Files.Values.Sum(content => content.Length) <= maxFiles * (maxBytes + payloadLength));
        Assert.All(fileSystem.Files.Keys, path => Assert.True(
            path == LogPath || Enumerable.Range(1, maxFiles - 1).Any(generation => path == log.GetGenerationPath(generation)),
            $"想定外のファイル: {path}"));
    }

    [Fact]
    public void AppendRotatesCurrentLogToFirstGenerationWhenLimitIsReached()
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);

        log.Append("aaaaaaaaaa");
        log.Append("bbbbbbbbbb");
        log.Append("cccccccccc");

        Assert.Equal("cccccccccc", fileSystem.Files[LogPath]);
        Assert.Equal("bbbbbbbbbb", fileSystem.Files[log.GetGenerationPath(1)]);
        Assert.Equal("aaaaaaaaaa", fileSystem.Files[log.GetGenerationPath(2)]);
    }

    [Fact]
    public void AppendDropsOldestGenerationWhenFileCountExceedsLimit()
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);

        foreach (var payload in new[] { "aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc", "dddddddddd" })
        {
            log.Append(payload);
        }

        Assert.Equal(3, fileSystem.Files.Count);
        Assert.Equal("dddddddddd", fileSystem.Files[LogPath]);
        Assert.Equal("cccccccccc", fileSystem.Files[log.GetGenerationPath(1)]);
        Assert.Equal("bbbbbbbbbb", fileSystem.Files[log.GetGenerationPath(2)]);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void AppendDoesNotRotateBelowLimit(int payloadLength)
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);

        log.Append(new string('a', payloadLength));

        Assert.Equal(LogPath, Assert.Single(fileSystem.Files.Keys));
    }

    [Fact]
    public void AppendKeepsExistingLogAcrossRestart()
    {
        var fileSystem = new FakeLogFileSystem();
        new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 1000, maxFiles: 3).Append("previous session\n");

        new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 1000, maxFiles: 3).Append("next session\n");

        Assert.Equal("previous session\nnext session\n", fileSystem.Files[LogPath]);
    }

    [Fact]
    public void AppendRotatesExistingOversizedLogOnFirstWrite()
    {
        var fileSystem = new FakeLogFileSystem();
        fileSystem.Files[LogPath] = new string('p', 20);
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);

        log.Append("next");

        Assert.Equal("next", fileSystem.Files[LogPath]);
        Assert.Equal(new string('p', 20), fileSystem.Files[log.GetGenerationPath(1)]);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void AppendWritesAndRetriesRotationWhenCurrentLogIsLocked(Type exceptionType)
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);
        log.Append("aaaaaaaaaa");
        fileSystem.MoveFailure = (source, _) => source == LogPath ? (Exception)Activator.CreateInstance(exceptionType)! : null;

        var exception = Record.Exception(() => log.Append("bbbbbbbbbb"));

        Assert.Null(exception);
        Assert.Equal("aaaaaaaaaabbbbbbbbbb", fileSystem.Files[LogPath]);
        Assert.Single(fileSystem.Files);

        fileSystem.MoveFailure = null;
        log.Append("cccccccccc");

        Assert.Equal("cccccccccc", fileSystem.Files[LogPath]);
        Assert.Equal("aaaaaaaaaabbbbbbbbbb", fileSystem.Files[log.GetGenerationPath(1)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendKeepsStagedLogAndRecoversAfterLockIsReleased(bool blockOnlyPlacement)
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);
        foreach (var payload in new[] { "aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc" })
        {
            log.Append(payload);
        }

        // 世代 1 が他プロセスに（Delete 共有なしで）開かれている状態。繰り下げ（blockOnlyPlacement=false）と
        // 上書き（true では app.1.log への配置だけ）に失敗する。
        var generation1 = log.GetGenerationPath(1);
        fileSystem.MoveFailure = (source, destination) => blockOnlyPlacement
            ? (destination == generation1 ? new IOException("locked") : null)
            : (source == generation1 || destination == generation1 ? new IOException("locked") : null);

        foreach (var payload in new[] { "dddddddddd", "eeeeeeeeee", "ffffffffff", "gggggggggg" })
        {
            var exception = Record.Exception(() => log.Append(payload));
            Assert.Null(exception);
        }

        // 失敗が続いても、退避ログは固定名の 1 ファイルに収まり、増えない。世代名以外のファイルも増えない。
        Assert.True(fileSystem.Files.Count <= 4, string.Join(", ", fileSystem.Files.Keys));
        Assert.All(fileSystem.Files.Keys, path => Assert.True(
            path == LogPath || path == log.StagingPath || path == log.GetGenerationPath(1) || path == log.GetGenerationPath(2),
            $"想定外のファイル: {path}"));
        Assert.Contains(fileSystem.Files.Values, content => content.Contains("cccccccccc", StringComparison.Ordinal));

        fileSystem.MoveFailure = null;
        log.Append("hhhhhhhhhh");
        log.Append("iiiiiiiiii");

        // 解除後は、退避ログが回収されて世代管理に戻り、固定名のファイルは残らない。
        Assert.DoesNotContain(log.StagingPath, fileSystem.Files.Keys);
        Assert.True(fileSystem.Files.Count <= 3, string.Join(", ", fileSystem.Files.Keys));
        Assert.Contains(fileSystem.Files.Values, content => content.Contains("gggggggggg", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendDoesNotRetryRotationOnEveryWriteWhileGenerationIsLocked()
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 800, maxFiles: 3);
        for (var i = 0; i < 80; i++)
        {
            log.Append(new string('a', 10));
        }

        var generation1 = log.GetGenerationPath(1);
        fileSystem.MoveFailure = (source, destination) => source == generation1 || destination == generation1 ? new IOException("locked") : null;
        fileSystem.MoveAttempts = 0;

        // 退避後のログは固定名に残り、現行ログが maxBytes / 8（= 100 バイト）増えるごとにしか再試行しない。
        for (var i = 0; i < 200; i++)
        {
            log.Append(new string('b', 10));
        }

        Assert.InRange(fileSystem.MoveAttempts, 1, 40);
    }

    [Fact]
    public void PhysicalFileSystemKeepsStagedLogBoundedAndRecoversWhenGenerationIsHeldOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "app.log");
            var log = new RotatingLogFile(new PhysicalLogFileSystem(), NullLogLock.Instance, path, maxBytes: 10, maxFiles: 3);
            log.Append("aaaaaaaaaa");
            log.Append("bbbbbbbbbb");
            log.Append("cccccccccc");

            // ログビューアーなどが app.1.log を Delete 共有なしで開いている状態を再現する。
            using (new FileStream(log.GetGenerationPath(1), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                foreach (var payload in new[] { "dddddddddd", "eeeeeeeeee", "ffffffffff", "gggggggggg" })
                {
                    Assert.Null(Record.Exception(() => log.Append(payload)));
                }

                var files = Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                Assert.True(files.Length <= 4, string.Join(", ", files));
                Assert.True(File.Exists(log.StagingPath), "ロック中は退避ログが固定名で残る（失敗経路を通っている）");
                Assert.All(files, name => Assert.Matches(@"^app(\.(1|2|rotating))?\.log$", name!));
            }

            log.Append("hhhhhhhhhh");
            log.Append("iiiiiiiiii");

            var recovered = Directory.GetFiles(directory);
            Assert.True(recovered.Length <= 3, string.Join(", ", recovered.Select(Path.GetFileName)));
            Assert.False(File.Exists(log.StagingPath));
            Assert.Contains(recovered, file => File.ReadAllText(file).Contains("gggggggggg", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void AppendPropagatesWriteFailure()
    {
        var fileSystem = new FakeLogFileSystem { AppendFailure = new IOException("disk full") };
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);

        Assert.Throws<IOException>(() => log.Append("a"));
    }

    [Fact]
    public void ConstructorRejectsInvalidArguments()
    {
        var fileSystem = new FakeLogFileSystem();

        Assert.Throws<ArgumentNullException>(() => new RotatingLogFile(null!, NullLogLock.Instance, LogPath, 10, 3));
        Assert.Throws<ArgumentException>(() => new RotatingLogFile(fileSystem, NullLogLock.Instance, " ", 10, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, 10, 1));
    }

    [Fact]
    public void PhysicalFileSystemRotatesRealFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "app.log");
            var log = new RotatingLogFile(new PhysicalLogFileSystem(), NullLogLock.Instance, path, maxBytes: 10, maxFiles: 3);

            log.Append("aaaaaaaaaa");
            log.Append("bbbbbbbbbb");
            log.Append("cccccccccc");
            log.Append("dddddddddd");

            Assert.Equal("dddddddddd", File.ReadAllText(path));
            Assert.Equal("cccccccccc", File.ReadAllText(log.GetGenerationPath(1)));
            Assert.Equal("bbbbbbbbbb", File.ReadAllText(log.GetGenerationPath(2)));
            Assert.Equal(3, Directory.GetFiles(directory).Length);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(2, 4, 100)]
    [InlineData(2, 8, 150)]
    public async Task ConcurrentWritersFromIndependentInstancesLoseNoLinesAndStayWithinLimit(int instanceCount, int writerCount, int writesPerWriter)
    {
        const int maxBytes = 4096;
        const int maxFiles = 3;
        var directory = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        var mutexName = $@"Local\PhotoGeoExplorerTests.{Guid.NewGuid():N}";
        var path = Path.Combine(directory, "app.log");
        var locks = Enumerable.Range(0, instanceCount).Select(_ => new NamedMutexLogLock(mutexName, TimeSpan.FromSeconds(30))).ToArray();
        try
        {
            // 別プロセス相当: インスタンスごとに独立した RotatingLogFile と Mutex ハンドルを持つ（プロセス内の lock は共有しない）。
            var logs = locks.Select(l => new RotatingLogFile(new PhysicalLogFileSystem(), l, path, maxBytes, maxFiles)).ToArray();
            var padding = new string('x', 120);

            var writers = Enumerable.Range(0, writerCount).Select(writer => Task.Run(() =>
            {
                for (var n = 0; n < writesPerWriter; n++)
                {
                    logs[writer % instanceCount].Append($"{writer}-{n} {padding}\n");
                }
            })).ToArray();
            await Task.WhenAll(writers).ConfigureAwait(true);

            var files = Directory.GetFiles(directory);
            Assert.True(files.Length <= maxFiles, $"ファイル数が上限を超えた: {string.Join(", ", files.Select(Path.GetFileName))}");
            Assert.All(files, file => Assert.True(new FileInfo(file).Length <= maxBytes + 200));

            // 保持できる範囲の行は、欠損・破損なく（各行が完全な形で）残る。
            var lines = files.SelectMany(File.ReadAllLines).ToArray();
            Assert.All(lines, line => Assert.EndsWith(padding, line, StringComparison.Ordinal));
            Assert.Equal(lines.Length, lines.Distinct().Count());
            Assert.True(lines.Length >= (maxFiles - 1) * (maxBytes / 130));
        }
        finally
        {
            foreach (var l in locks)
            {
                l.Dispose();
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeLogFileSystem : ILogFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        // 移動元・移動先のパスに応じて例外を注入する（null なら成功）。
        public Func<string, string, Exception?>? MoveFailure { get; set; }

        public Exception? AppendFailure { get; set; }

        public int MoveAttempts { get; set; }

        public long GetLength(string path)
        {
            return Files.TryGetValue(path, out var content) ? content.Length : 0;
        }

        public void CreateDirectory(string path)
        {
        }

        public void AppendAllText(string path, string contents)
        {
            if (AppendFailure is not null)
            {
                throw AppendFailure;
            }

            Files[path] = Files.GetValueOrDefault(path, string.Empty) + contents;
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveAttempts++;
            var failure = MoveFailure?.Invoke(sourcePath, destinationPath);
            if (failure is not null)
            {
                throw failure;
            }

            if (!Files.TryGetValue(sourcePath, out var content))
            {
                throw new FileNotFoundException(sourcePath);
            }

            if (!overwrite && Files.ContainsKey(destinationPath))
            {
                throw new IOException($"already exists: {destinationPath}");
            }

            Files.Remove(sourcePath);
            Files[destinationPath] = content;
        }

        public void Delete(string path)
        {
            Files.Remove(path);
        }
    }
}
