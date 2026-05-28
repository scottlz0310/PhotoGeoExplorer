using System;

namespace PhotoGeoExplorer.Services;

internal interface IFolderWatcherService : IDisposable
{
    event EventHandler? FolderChanged;

    void Watch(string folderPath);

    void Stop();
}
