using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// PreviewPaneViewControl のコードビハインド
/// ScrollViewer のイベントを ViewModel に橋渡しする
/// </summary>
internal sealed partial class PreviewPaneViewControl : UserControl
{
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _dragStartHorizontalOffset;
    private double _dragStartVerticalOffset;

    public PreviewPaneViewControl()
    {
        InitializeComponent();
    }

    private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (scrollViewer.DataContext is not PreviewPaneViewModel viewModel)
        {
            return;
        }

        viewModel.OnViewportSizeChanged(scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
    }

    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Image image)
        {
            return;
        }

        var scrollViewer = FindParent<ScrollViewer>(image);
        if (scrollViewer?.DataContext is not PreviewPaneViewModel viewModel)
        {
            return;
        }

        viewModel.OnImageOpened(scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);

        // ViewModel の ZoomFactor を ScrollViewer に反映
        scrollViewer.ChangeView(0, 0, viewModel.ZoomFactor, disableAnimation: true);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (scrollViewer.DataContext is not PreviewPaneViewModel viewModel)
        {
            return;
        }

        var point = e.GetCurrentPoint(scrollViewer);
        if (point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        // ViewModel にズーム操作を通知
        viewModel.ZoomAtPoint(point.Properties.MouseWheelDelta, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);

        // カーソル位置を基準にズーム
        var current = scrollViewer.ZoomFactor;
        var target = viewModel.ZoomFactor;

        if (System.Math.Abs(target - current) < 0.0001f)
        {
            return;
        }

        var cursor = point.Position;
        var contentX = (scrollViewer.HorizontalOffset + cursor.X) / current;
        var contentY = (scrollViewer.VerticalOffset + cursor.Y) / current;
        var targetOffsetX = contentX * target - cursor.X;
        var targetOffsetY = contentY * target - cursor.Y;

        scrollViewer.ChangeView(targetOffsetX, targetOffsetY, target, disableAnimation: true);
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var point = e.GetCurrentPoint(scrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        _dragStart = point.Position;
        _dragStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _dragStartVerticalOffset = scrollViewer.VerticalOffset;
        scrollViewer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var point = e.GetCurrentPoint(scrollViewer).Position;
        var deltaX = point.X - _dragStart.X;
        var deltaY = point.Y - _dragStart.Y;
        scrollViewer.ChangeView(
            _dragStartHorizontalOffset - deltaX,
            _dragStartVerticalOffset - deltaY,
            null,
            disableAnimation: true);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        _isDragging = false;
        scrollViewer.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T typedParent)
            {
                return typedParent;
            }

            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}
