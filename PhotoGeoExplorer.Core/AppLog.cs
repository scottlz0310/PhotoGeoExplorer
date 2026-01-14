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

    internal static void Reset()
    {
        TryDelete(LogPath);
        TryDelete(FallbackLogPath);
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

        if (TryWrite(LogDirectory, LogPath, payload))
        {
            return;
        }

        TryWrite(FallbackLogDirectory, FallbackLogPath, payload);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
    }

    private static bool TryWrite(string directory, string path, string payload)
    {
        try
        {
            Directory.CreateDirectory(directory);
            lock (Gate)
            {
                File.AppendAllText(path, payload);
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
