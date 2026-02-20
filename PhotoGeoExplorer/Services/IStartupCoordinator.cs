using System;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal interface IStartupCoordinator : IDisposable
{
    string? StartupFilePath { get; }

    void SetStartupFilePath(string? filePath);

    Task ApplyStartupAsync();
}
