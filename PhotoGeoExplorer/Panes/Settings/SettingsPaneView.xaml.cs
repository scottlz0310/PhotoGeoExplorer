using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Panes.Settings;

/// <summary>
/// 設定Paneの View（サンプル実装）
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class SettingsPaneView : UserControl
{
    public SettingsPaneView()
    {
        InitializeComponent();
    }
}
