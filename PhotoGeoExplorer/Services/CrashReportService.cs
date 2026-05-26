using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoGeoExplorer.Services;

internal sealed class CrashReportService : ICrashReportService
{
    private const int MaxCrashLogCount = 20;

    private static readonly string DefaultAppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer");

    private readonly string _appDataDirectory;
    private readonly string _lockFilePath;
    private readonly string _crashReportsDir;

    public CrashReportService()
        : this(DefaultAppDataDirectory)
    {
    }

    internal CrashReportService(string appDataDirectory)
    {
        _appDataDirectory = appDataDirectory ?? throw new ArgumentNullException(nameof(appDataDirectory));
        _lockFilePath = Path.Combine(_appDataDirectory, "running.lock");
        _crashReportsDir = Path.Combine(_appDataDirectory, "CrashReports");
    }

    public bool PreviouslyTerminatedAbnormally { get; private set; }

    public string CrashReportsDirectoryPath => _crashReportsDir;

    public void RecordStartup()
    {
        PreviouslyTerminatedAbnormally = File.Exists(_lockFilePath);
        TryCreateLockFile();
    }

    public void RecordNormalExit()
    {
        TryDeleteFile(_lockFilePath);
    }

    public void WriteCrashLog(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_crashReportsDir);
            var timestamp = DateTimeOffset.Now;
            var fileName = $"crash_{timestamp:yyyyMMddHHmmss}.log";
            var filePath = Path.Combine(_crashReportsDir, fileName);

            var version = typeof(CrashReportService).Assembly.GetName().Version?.ToString() ?? "unknown";
            var osVersion = Environment.OSVersion.ToString();

            var builder = new StringBuilder();
            builder.AppendLine("PhotoGeoExplorer Crash Report");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"App Version: {version}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {osVersion}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {exception?.GetType().FullName ?? "Unknown"}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Exception Message: {MaskSensitiveData(exception?.Message ?? "Unknown")}");
            builder.AppendLine("Stack Trace:");
            builder.Append(MaskSensitiveData(exception?.ToString() ?? "No stack trace"));

            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
            PruneOldCrashLogs();
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (System.Security.SecurityException) { }
    }

    internal static string MaskSensitiveData(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Windows パス（ドライブレター付き）をマスク
        text = Regex.Replace(
            text,
            @"[A-Za-z]:\\[^\s,'""\r\n]+",
            "<path:masked>",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        // UNC パス
        text = Regex.Replace(
            text,
            @"\\\\[^\s,'""\r\n]+",
            "<unc:masked>",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return text;
    }

    private void PruneOldCrashLogs()
    {
        try
        {
            var files = Directory.GetFiles(_crashReportsDir, "crash_*.log")
                .OrderByDescending(f => f)
                .Skip(MaxCrashLogCount)
                .ToList();

            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
    }

    private void TryCreateLockFile()
    {
        try
        {
            Directory.CreateDirectory(_appDataDirectory);
            File.WriteAllText(_lockFilePath, DateTimeOffset.Now.ToString("O"));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
    }
}
