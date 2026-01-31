using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Panes;

/// <summary>
/// Pane ViewModel の型に基づいて適切な DataTemplate を選択するセレクター
/// VM→View の自動解決を実現
/// </summary>
internal sealed class PaneViewModelDataTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// 将来的にPaneが増えた際の DataTemplate マッピング用
    /// 現在は MainWindow.xaml の Resources で定義された DataTemplate を使用
    /// </summary>
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        if (item == null)
        {
            return null;
        }

        // 将来的に特定の Pane ViewModel 用の DataTemplate を返す
        // 現時点では MainWindow.xaml の Resources で定義された DataTemplate が使用される
        return base.SelectTemplateCore(item, container);
    }
}
