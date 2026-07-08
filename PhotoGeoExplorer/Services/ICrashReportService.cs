using System;

namespace PhotoGeoExplorer.Services;

internal interface ICrashReportService
{
    bool PreviouslyTerminatedAbnormally { get; }

    bool HasReportableCrash { get; }

    string CrashReportsDirectoryPath { get; }

    void RecordStartup();

    void RecordNormalExit();

    void WriteCrashLog(Exception? exception);

    string? GetLatestCrashLogContent();
}
