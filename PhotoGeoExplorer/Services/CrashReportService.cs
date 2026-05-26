using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoGeoExplorer.Services;

internal sealed class CrashReportService : ICrashReportService
{
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer");

    private static readonly string LockFilePath = Path.Combine(AppDataDirectory, "running.lock");

    private static readonly string CrashReportsDir = Path.Combine(AppDataDirectory, "CrashReports");

    public bool PreviouslyTerminatedAbnormally { get; private set; }

    public string CrashReportsDirectoryPath => CrashReportsDir;

    public void RecordStartup()
    {
        PreviouslyTerminatedAbnormally = File.Exists(LockFilePath);
        TryCreateLockFile();
    }

    public void RecordNormalExit()
    {
        TryDeleteFile(LockFilePath);
    }

    public void WriteCrashLog(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(CrashReportsDir);
            var timestamp = DateTimeOffset.Now;
            var fileName = $"crash_{timestamp:yyyyMMddHHmmss}.log";
            var filePath = Path.Combine(CrashReportsDir, fileName);

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

    private static void TryCreateLockFile()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            File.WriteAllText(LockFilePath, DateTimeOffset.Now.ToString("O"));
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
