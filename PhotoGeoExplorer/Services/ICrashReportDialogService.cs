using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal interface ICrashReportDialogService
{
    Task ShowCrashReportDialogAsync();

    Task OpenLogFolderAsync();
}
