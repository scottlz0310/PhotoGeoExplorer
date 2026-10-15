using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading;

namespace PhotoGeoExplorer;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoGeoExplorer",
        "Logs");

    private static readonly string LogPath = Path.Combine(LogDirectory, "app.log");
    private static readonly string FallbackLogDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Logs");

    private static readonly string FallbackLogPath = Path.Combine(FallbackLogDirectory, "app.log");
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

    // 1 ファイル 1 MB × 5 ファイル（app.log + app.1.log〜app.4.log）で合計約 5 MB を上限とする。
    // 実測では 1 セッションは数十 KB 程度のため、異常終了した前回を含む複数セッション分が残る。
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxFiles = 5;

    private static readonly PhysicalLogFileSystem FileSystem = new();
    private static readonly ILogLock CrossProcessLock = CreateCrossProcessLock();
    private static readonly RotatingLogFile PrimaryLog = new(FileSystem, CrossProcessLock, LogPath, MaxFileBytes, MaxFiles);
    private static readonly RotatingLogFile FallbackLog = new(FileSystem, CrossProcessLock, FallbackLogPath, MaxFileBytes, MaxFiles);

    internal static void Info(string message)
    {
        Write("INFO", message, null);
    }

    internal static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    internal static string LogFilePath => LogPath;

    internal static string CurrentSessionId => SessionId;

    // 単一インスタンスの転送元プロセスと本体は同じ app.log に書き込むため、プロセス間で直列化する。
    // 作成できない環境ではログ機構自身を失敗させず、プロセス内の排他（Gate）だけで続行する。
    private static ILogLock CreateCrossProcessLock()
    {
        try
        {
            return new NamedMutexLogLock(@"Local\PhotoGeoExplorer.AppLog", TimeSpan.FromSeconds(2));
        }
        catch (UnauthorizedAccessException)
        {
            return NullLogLock.Instance;
        }
        catch (IOException)
        {
            return NullLogLock.Instance;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return NullLogLock.Instance;
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var threadId = Environment.CurrentManagedThreadId;
        var builder = new StringBuilder();
        builder.Append(timestamp)
            .Append(" [").Append(SessionId).Append(']')
            .Append(" [T").Append(threadId).Append(']')
            .Append(' ').Append(level)
            .Append(' ').Append(message);

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append("Exception Type: ").AppendLine(exception.GetType().FullName);
            builder.Append("Exception Message: ").AppendLine(exception.Message);
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                builder.Append("Stack Trace:").AppendLine();
                builder.AppendLine(exception.StackTrace);
            }
            if (exception.InnerException is not null)
            {
                builder.AppendLine("Inner Exception:");
                builder.Append(exception.InnerException);
            }
        }

        builder.AppendLine();

        var payload = builder.ToString();

        if (TryWrite(PrimaryLog, payload))
        {
            return;
        }

        TryWrite(FallbackLog, payload);
    }

    private static bool TryWrite(RotatingLogFile log, string payload)
    {
        try
        {
            lock (Gate)
            {
                log.Append(payload);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
