using System;
using System.Text.RegularExpressions;

namespace PhotoGeoExplorer.Tests;

public sealed class AppLogTests
{
    [Fact]
    public void SessionIdIsNotNullOrEmpty()
    {
        var sessionId = AppLog.CurrentSessionId;

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public void SessionIdIsConsistent()
    {
        var sessionId1 = AppLog.CurrentSessionId;
        var sessionId2 = AppLog.CurrentSessionId;

        Assert.Equal(sessionId1, sessionId2);
    }

    [Fact]
    public void SessionIdHasExpectedLength()
    {
        var sessionId = AppLog.CurrentSessionId;

        // SessionId should be 8 characters (first 8 chars of GUID without hyphens)
        Assert.Equal(8, sessionId.Length);
    }

    [Fact]
    public void SessionIdIsHexadecimal()
    {
        var sessionId = AppLog.CurrentSessionId;

        // Should contain only hexadecimal characters (0-9, a-f)
        Assert.Matches("^[0-9a-f]{8}$", sessionId);
    }

    [Fact]
    public void LogFilePathIsNotNullOrEmpty()
    {
        var logPath = AppLog.LogFilePath;

        Assert.False(string.IsNullOrWhiteSpace(logPath));
    }

    [Fact]
    public void LogFilePathContainsExpectedDirectory()
    {
        var logPath = AppLog.LogFilePath;

        Assert.Contains("PhotoGeoExplorer", logPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Logs", logPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogFilePathEndsWithAppLog()
    {
        var logPath = AppLog.LogFilePath;

        Assert.EndsWith("app.log", logPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfoLogsMessage()
    {
        // This test just verifies that Info doesn't throw
        var testMessage = $"Test info message {Guid.NewGuid()}";
        var exception = Record.Exception(() => AppLog.Info(testMessage));

        Assert.Null(exception);
    }

    [Fact]
    public void ErrorLogsMessageWithException()
    {
        // This test just verifies that Error doesn't throw
        var testMessage = $"Test error message {Guid.NewGuid()}";
        var testException = new InvalidOperationException("Test exception");
        var exception = Record.Exception(() => AppLog.Error(testMessage, testException));

        Assert.Null(exception);
    }

    [Fact]
    public void ErrorLogsMessageWithoutException()
    {
        // This test just verifies that Error doesn't throw when exception is null
        var testMessage = $"Test error message {Guid.NewGuid()}";
        var exception = Record.Exception(() => AppLog.Error(testMessage, null));

        Assert.Null(exception);
    }
}
