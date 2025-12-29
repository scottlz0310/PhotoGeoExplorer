using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Title = LocalizationService.GetString("SplashWindow.Title");
    }
}
