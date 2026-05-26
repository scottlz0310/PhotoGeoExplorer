using System;
using System.IO;
using PhotoGeoExplorer.Services;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class CrashReportServiceTests
{
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
        var service = new CrashReportService();
        EnsureNoLockFile();

        service.RecordStartup();

        Assert.False(service.PreviouslyTerminatedAbnormally);
        CleanupLockFile();
    }

    [Fact]
    public void RecordNormalExit_DeletesLockFile()
    {
        var service = new CrashReportService();
        service.RecordStartup();
        var lockPath = GetLockFilePath();
        Assert.True(File.Exists(lockPath));

        service.RecordNormalExit();

        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void WriteCrashLog_CreatesFileInCrashReportsDirectory()
    {
        var service = new CrashReportService();
        var dir = service.CrashReportsDirectoryPath;
        CleanupCrashReports(dir);

        service.WriteCrashLog(new InvalidOperationException("test crash"));

        var files = Directory.GetFiles(dir, "crash_*.log");
        Assert.Single(files);
        CleanupCrashReports(dir);
    }

    [Fact]
    public void WriteCrashLog_MasksSensitiveDataInLog()
    {
        var service = new CrashReportService();
        var dir = service.CrashReportsDirectoryPath;
        CleanupCrashReports(dir);

        service.WriteCrashLog(new FileNotFoundException(@"File not found: C:\Users\testuser\photo.jpg"));

        var files = Directory.GetFiles(dir, "crash_*.log");
        Assert.Single(files);
        var content = File.ReadAllText(files[0]);
        Assert.DoesNotContain(@"C:\Users\testuser\photo.jpg", content, StringComparison.Ordinal);
        Assert.Contains("<path:masked>", content, StringComparison.Ordinal);
        CleanupCrashReports(dir);
    }

    [Fact]
    public void WriteCrashLog_DoesNotThrow_WhenExceptionIsNull()
    {
        var service = new CrashReportService();

        var ex = Record.Exception(() => service.WriteCrashLog(null));

        Assert.Null(ex);
    }

    private static string GetLockFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoGeoExplorer",
            "running.lock");
    }

    private static void EnsureNoLockFile()
    {
        var path = GetLockFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CleanupLockFile()
    {
        var path = GetLockFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CleanupCrashReports(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "crash_*.log"))
        {
            File.Delete(file);
        }
    }
}
