using System;
using System.Collections.Generic;
using System.IO;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class CrashReportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CrashReportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PhotoGeoExplorerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(@"C:\Users\testuser\Pictures\photo.jpg", "<path:masked>")]
    [InlineData(@"C:\Users\testuser\AppData\Local\app", "<path:masked>")]
    [InlineData(@"\\server\share\file.txt", "<unc:masked>")]
    [InlineData("No path here, just text", "No path here, just text")]
    [InlineData("", "")]
    public void MaskSensitiveData_MasksPathsCorrectly(string input, string expected)
    {
        var result = CrashReportService.MaskSensitiveData(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MaskSensitiveData_PreservesStackTraceSymbols()
    {
        var input = "   at PhotoGeoExplorer.Services.ExifEditorService.EditExifAsync()";

        var result = CrashReportService.MaskSensitiveData(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void MaskSensitiveData_MasksPathInExceptionMessage()
    {
        var input = @"Could not find file 'C:\Users\testuser\photo.jpg'.";

        var result = CrashReportService.MaskSensitiveData(input);

        Assert.DoesNotContain(@"C:\Users\testuser\photo.jpg", result, StringComparison.Ordinal);
        Assert.Contains("<path:masked>", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    public void MaskSensitiveData_HandlesNull(string? input)
    {
        var result = CrashReportService.MaskSensitiveData(input!);

        Assert.Equal(input, result);
    }

    [Fact]
    public void RecordStartup_SetsAbnormalTerminationFalse_WhenNoLockFile()
    {
        var service = new CrashReportService(_tempDir);

        service.RecordStartup();

        Assert.False(service.PreviouslyTerminatedAbnormally);
    }

    [Fact]
    public void RecordStartup_SetsAbnormalTerminationTrue_WhenLockFileExists()
    {
        var service = new CrashReportService(_tempDir);
        var lockPath = Path.Combine(_tempDir, "running.lock");
        File.WriteAllText(lockPath, "stale");

        service.RecordStartup();

        Assert.True(service.PreviouslyTerminatedAbnormally);
    }

    [Theory]
    [InlineData(false, false, false, false)] // 何も残っていない（正常な前回終了）
    [InlineData(true, false, true, false)]   // running.lock のみ（強制終了・電源断等、報告可能なログはない）
    [InlineData(false, true, true, true)]    // crash.marker のみ（WriteCrashLog 後に marker だけ残存するケース）
    [InlineData(true, true, true, true)]     // 両方存在（典型的なクラッシュ直後の再起動）
    public void RecordStartup_DeterminesReportability_BasedOnCrashMarkerPresence(
        bool lockFileExists,
        bool crashMarkerExists,
        bool expectedPreviouslyTerminatedAbnormally,
        bool expectedHasReportableCrash)
    {
        if (lockFileExists)
        {
            File.WriteAllText(Path.Combine(_tempDir, "running.lock"), "stale");
        }

        if (crashMarkerExists)
        {
            File.WriteAllText(Path.Combine(_tempDir, "crash.marker"), "marker");
        }

        var service = new CrashReportService(_tempDir);
        service.RecordStartup();

        Assert.Equal(expectedPreviouslyTerminatedAbnormally, service.PreviouslyTerminatedAbnormally);
        Assert.Equal(expectedHasReportableCrash, service.HasReportableCrash);
    }

    [Fact]
    public void RecordNormalExit_DeletesLockFile()
    {
        var service = new CrashReportService(_tempDir);
        service.RecordStartup();
        var lockPath = Path.Combine(_tempDir, "running.lock");
        Assert.True(File.Exists(lockPath));

        service.RecordNormalExit();

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void WriteCrashLog_CreatesFileInCrashReportsDirectory()
    {
        var service = new CrashReportService(_tempDir);
        var dir = service.CrashReportsDirectoryPath;

        service.WriteCrashLog(new InvalidOperationException("test crash"));

        var files = Directory.GetFiles(dir, "crash_*.log");
        Assert.Single(files);
    }

    [Fact]
    public void WriteCrashLog_MasksSensitiveDataInLog()
    {
        var service = new CrashReportService(_tempDir);
        var dir = service.CrashReportsDirectoryPath;

        service.WriteCrashLog(new FileNotFoundException(@"File not found: C:\Users\testuser\photo.jpg"));

        var files = Directory.GetFiles(dir, "crash_*.log");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.DoesNotContain(@"C:\Users\testuser\photo.jpg", content, StringComparison.Ordinal);
        Assert.Contains("<path:masked>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCrashLog_DoesNotThrow_WhenExceptionIsNull()
    {
        var service = new CrashReportService(_tempDir);

        var ex = Record.Exception(() => service.WriteCrashLog(null));

        Assert.Null(ex);
    }

    [Fact]
    public void WriteCrashLog_PrunesOldLogs_WhenExceedingMaxCount()
    {
        var service = new CrashReportService(_tempDir);
        var dir = service.CrashReportsDirectoryPath;
        Directory.CreateDirectory(dir);

        for (var i = 0; i < 22; i++)
        {
            var fileName = $"crash_20240101{i:D6}.log";
            File.WriteAllText(Path.Combine(dir, fileName), "dummy");
        }

        service.WriteCrashLog(new InvalidOperationException("trigger prune"));

        var files = Directory.GetFiles(dir, "crash_*.log");
        Assert.True(files.Length <= 20, $"Expected <=20 files, got {files.Length}");
    }

    [Fact]
    public void GetLatestCrashLogContent_ReturnsNull_WhenCrashReportsDirDoesNotExist()
    {
        var service = new CrashReportService(_tempDir);

        var result = service.GetLatestCrashLogContent();

        Assert.Null(result);
    }

    [Fact]
    public void GetLatestCrashLogContent_ReturnsNull_WhenNoLogFilesExist()
    {
        var service = new CrashReportService(_tempDir);
        Directory.CreateDirectory(service.CrashReportsDirectoryPath);

        var result = service.GetLatestCrashLogContent();

        Assert.Null(result);
    }

    [Fact]
    public void GetLatestCrashLogContent_ReturnsLatestLog_WhenMultipleFilesExist()
    {
        var service = new CrashReportService(_tempDir);
        var dir = service.CrashReportsDirectoryPath;
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "crash_20260101000000.log"), "older log");
        File.WriteAllText(Path.Combine(dir, "crash_20260201000000.log"), "newer log");

        var result = service.GetLatestCrashLogContent();

        Assert.Equal("newer log", result);
    }

    [Fact]
    public void RecordNormalExit_AfterWriteCrashLog_SameInstance_PreservesLockForNextStartup()
    {
        // 同一インスタンスで WriteCrashLog → RecordNormalExit → 次回起動のライフサイクルを保証する
        var service = new CrashReportService(_tempDir);
        service.RecordStartup();
        var lockPath = Path.Combine(_tempDir, "running.lock");
        Assert.True(File.Exists(lockPath));

        service.WriteCrashLog(new InvalidOperationException("unexpected error"));
        service.RecordNormalExit();

        Assert.True(File.Exists(lockPath));

        var nextService = new CrashReportService(_tempDir);
        nextService.RecordStartup();
        Assert.True(nextService.PreviouslyTerminatedAbnormally);
    }

    [Fact]
    public void RecordNormalExit_WhenSeparateInstanceWroteCrashLog_PreservesLockForNextStartup()
    {
        // MapPaneService が生成する別インスタンスで WriteCrashLog を呼び、
        // App インスタンスの RecordNormalExit で running.lock が残ることを保証する
        var appInstance = new CrashReportService(_tempDir);
        appInstance.RecordStartup();
        var lockPath = Path.Combine(_tempDir, "running.lock");
        Assert.True(File.Exists(lockPath));

        var serviceLayerInstance = new CrashReportService(_tempDir);
        serviceLayerInstance.WriteCrashLog(new InvalidOperationException("service layer crash"));

        appInstance.RecordNormalExit();

        Assert.True(File.Exists(lockPath));

        var nextInstance = new CrashReportService(_tempDir);
        nextInstance.RecordStartup();
        Assert.True(nextInstance.PreviouslyTerminatedAbnormally);
    }

    [Fact]
    public void RecordNormalExit_WithoutWriteCrashLog_DeletesLock()
    {
        // クラッシュログがない場合は従来どおりロックを削除する
        var service = new CrashReportService(_tempDir);
        service.RecordStartup();
        var lockPath = Path.Combine(_tempDir, "running.lock");
        Assert.True(File.Exists(lockPath));

        service.RecordNormalExit();

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void GetLatestCrashLogContent_ReturnsNull_WhenFileIsLocked()
    {
        var service = new CrashReportService(_tempDir);
        var dir = service.CrashReportsDirectoryPath;
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "crash_20260101000000.log");
        File.WriteAllText(logPath, "locked content");

        using var fs = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = service.GetLatestCrashLogContent();

        Assert.Null(result);
    }

    public static IEnumerable<object[]> MultiInstanceLifecycleScenarios()
    {
        yield return new object[]
        {
            "多重起動→両方正常終了（前回異常終了として検出されない）",
            (Action<string>)(dir =>
            {
                var processA = new CrashReportService(dir);
                processA.RecordStartup();
                var processB = new CrashReportService(dir);
                processB.RecordStartup();
                processA.RecordNormalExit();
                processB.RecordNormalExit();
            }),
            false,
            false
        };

        yield return new object[]
        {
            "多重起動→片方が正常終了、片方がクラッシュ（ログあり）",
            (Action<string>)(dir =>
            {
                var processA = new CrashReportService(dir);
                processA.RecordStartup();
                var processB = new CrashReportService(dir);
                processB.RecordStartup();
                processA.RecordNormalExit();
                processB.WriteCrashLog(new InvalidOperationException("crash in process B"));
                processB.RecordNormalExit();
            }),
            true,
            true
        };

        yield return new object[]
        {
            "単一起動→クラッシュログを書いてから正常終了",
            (Action<string>)(dir =>
            {
                var process = new CrashReportService(dir);
                process.RecordStartup();
                process.WriteCrashLog(new InvalidOperationException("crash"));
                process.RecordNormalExit();
            }),
            true,
            true
        };

        yield return new object[]
        {
            "単一起動→強制終了（RecordNormalExit も WriteCrashLog も呼ばれない）",
            (Action<string>)(dir =>
            {
                var process = new CrashReportService(dir);
                process.RecordStartup();
                // 強制終了・電源断を模す: 終了処理は一切呼ばれない
            }),
            true,
            false
        };

        yield return new object[]
        {
            "順次起動→終了を繰り返した後も状態が正しく引き継がれる",
            (Action<string>)(dir =>
            {
                var first = new CrashReportService(dir);
                first.RecordStartup();
                first.RecordNormalExit();

                var second = new CrashReportService(dir);
                second.RecordStartup();
                Assert.False(second.PreviouslyTerminatedAbnormally);
                second.RecordNormalExit();
            }),
            false,
            false
        };
    }

    [Theory]
    [MemberData(nameof(MultiInstanceLifecycleScenarios))]
    public void RecordStartup_MultiInstanceLifecycle_NextStartupReflectsExpectedState(
        string scenario,
        Action<string> setupScenario,
        bool expectedPreviouslyTerminatedAbnormally,
        bool expectedHasReportableCrash)
    {
        ArgumentNullException.ThrowIfNull(setupScenario);

        setupScenario(_tempDir);

        var nextInstance = new CrashReportService(_tempDir);
        nextInstance.RecordStartup();

        Assert.True(
            expectedPreviouslyTerminatedAbnormally == nextInstance.PreviouslyTerminatedAbnormally,
            $"[{scenario}] PreviouslyTerminatedAbnormally: expected {expectedPreviouslyTerminatedAbnormally}, actual {nextInstance.PreviouslyTerminatedAbnormally}");
        Assert.True(
            expectedHasReportableCrash == nextInstance.HasReportableCrash,
            $"[{scenario}] HasReportableCrash: expected {expectedHasReportableCrash}, actual {nextInstance.HasReportableCrash}");
    }
}
