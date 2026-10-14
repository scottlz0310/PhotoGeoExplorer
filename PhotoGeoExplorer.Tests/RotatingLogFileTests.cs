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
        fileSystem.MoveFailure = path => path == LogPath ? (Exception)Activator.CreateInstance(exceptionType)! : null;

        var exception = Record.Exception(() => log.Append("bbbbbbbbbb"));

        Assert.Null(exception);
        Assert.Equal("aaaaaaaaaabbbbbbbbbb", fileSystem.Files[LogPath]);
        Assert.Single(fileSystem.Files);

        fileSystem.MoveFailure = null;
        log.Append("cccccccccc");

        Assert.Equal("cccccccccc", fileSystem.Files[LogPath]);
        Assert.Equal("aaaaaaaaaabbbbbbbbbb", fileSystem.Files[log.GetGenerationPath(1)]);
    }

    [Fact]
    public void AppendKeepsCurrentLogWhenShiftingOlderGenerationFails()
    {
        var fileSystem = new FakeLogFileSystem();
        var log = new RotatingLogFile(fileSystem, NullLogLock.Instance, LogPath, maxBytes: 10, maxFiles: 3);
        log.Append("aaaaaaaaaa");
        log.Append("bbbbbbbbbb");
        fileSystem.MoveFailure = path => path == log.GetGenerationPath(1) ? new IOException("locked") : null;

        var exception = Record.Exception(() => log.Append("cccccccccc"));

        Assert.Null(exception);
        Assert.Equal("cccccccccc", fileSystem.Files[LogPath]);
        Assert.Contains("bbbbbbbbbb", fileSystem.Files.Values);
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

        // 移動元のパスごとに例外を注入する（null なら成功）。
        public Func<string, Exception?>? MoveFailure { get; set; }

        public Exception? AppendFailure { get; set; }

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
            var failure = MoveFailure?.Invoke(sourcePath);
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
