using System;
using System.Threading.Tasks;

namespace PhotoGeoExplorer.Services;

internal interface IHelpService : IDisposable
{
    Task ShowGettingStartedAsync();

    Task ShowBasicsAsync();

    Task ShowHelpHtmlWindowAsync();

    Task ShowAboutAsync();

    Task ShowQuickStartIfNeededAsync();

    void CloseHelpWindow();
}
