using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Panes.Places;

/// <summary>
/// 字地名のツリーの表示。処理は <see cref="PlacesPaneViewModel"/> に委譲する
/// </summary>
internal sealed partial class PlacesPaneView : UserControl
{
    public PlacesPaneView()
    {
        InitializeComponent();
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (DataContext is PlacesPaneViewModel viewModel && args.InvokedItem is PlacesNodeViewModel node)
        {
            viewModel.SelectNode(node);
        }
    }
}
