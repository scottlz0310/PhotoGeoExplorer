using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
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
    private PreviewPaneViewModel? _viewModel;
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _dragStartHorizontalOffset;
    private double _dragStartVerticalOffset;
    private XamlRoot? _subscribedXamlRoot;
    private double _lastRasterizationScale = 1.0;
    private CancellationTokenSource? _xamlRootWaitCts;

    /// <summary>
    /// 最大化状態が変更されたときに発生するイベント
    /// </summary>
    public event EventHandler<bool>? MaximizeChanged;

    public PreviewPaneViewControl()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PreviewPaneViewModel);
        await SubscribeToXamlRootChangedAsync().ConfigureAwait(true);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as PreviewPaneViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeFromXamlRootChanged();

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.FitRequested -= OnViewModelFitRequested;
        _viewModel = null;
    }

    private void OnViewModelFitRequested(object? sender, EventArgs e)
    {
        ApplyFitIfNeeded(resetOffsets: true);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreviewPaneViewModel.ZoomFactor))
        {
            return;
        }

        var viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            PreviewScrollViewer.ChangeView(null, null, viewModel.ZoomFactor, disableAnimation: true);
        });
    }

    private void AttachViewModel(PreviewPaneViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.FitRequested -= OnViewModelFitRequested;
        }

        _viewModel = viewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.InitializeDispatcherQueue(DispatcherQueue);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.FitRequested += OnViewModelFitRequested;

        if (_subscribedXamlRoot is not null)
        {
            viewModel.OnRasterizationScaleChanged(_lastRasterizationScale);
        }
    }

    private async Task SubscribeToXamlRootChangedAsync()
    {
        if (_subscribedXamlRoot is not null)
        {
            return;
        }

        _xamlRootWaitCts?.Cancel();
        _xamlRootWaitCts?.Dispose();
        _xamlRootWaitCts = new CancellationTokenSource();
        var token = _xamlRootWaitCts.Token;

        var xamlRoot = await EnsureXamlRootAsync(token).ConfigureAwait(true);
        if (token.IsCancellationRequested || xamlRoot is null)
        {
            return;
        }

        _subscribedXamlRoot = xamlRoot;
        _lastRasterizationScale = xamlRoot.RasterizationScale;
        xamlRoot.Changed += OnXamlRootChanged;
        AppLog.Info($"PreviewPaneViewControl subscribed to XamlRoot.Changed. Initial RasterizationScale: {_lastRasterizationScale}");

        _viewModel?.OnRasterizationScaleChanged(_lastRasterizationScale);
    }

    private void UnsubscribeFromXamlRootChanged()
    {
        _xamlRootWaitCts?.Cancel();
        _xamlRootWaitCts?.Dispose();
        _xamlRootWaitCts = null;

        if (_subscribedXamlRoot is null)
        {
            return;
        }

        _subscribedXamlRoot.Changed -= OnXamlRootChanged;
        _subscribedXamlRoot = null;
    }

    private async Task<XamlRoot?> EnsureXamlRootAsync(CancellationToken token)
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (XamlRoot is not null)
        {
            return XamlRoot;
        }

        AppLog.Info("PreviewPaneViewControl: XamlRoot is null, waiting for it to become available...");

        var elapsed = 0;
        while (XamlRoot is null && elapsed < maxWaitMs)
        {
            try
            {
                await Task.Delay(intervalMs, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            elapsed += intervalMs;
        }

        if (XamlRoot is not null)
        {
            AppLog.Info($"PreviewPaneViewControl: XamlRoot became available after {elapsed}ms.");
            return XamlRoot;
        }

        AppLog.Info($"PreviewPaneViewControl: XamlRoot still null after {elapsed}ms, giving up.");
        return null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var newScale = sender.RasterizationScale;
        if (Math.Abs(newScale - _lastRasterizationScale) < 0.0001)
        {
            return;
        }

        AppLog.Info($"PreviewPaneViewControl: RasterizationScale changed: {_lastRasterizationScale} -> {newScale}");
        _viewModel?.OnRasterizationScaleChanged(newScale);
        _lastRasterizationScale = newScale;
    }

    private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (DataContext is not PreviewPaneViewModel viewModel)
        {
            return;
        }

        viewModel.OnViewportSizeChanged(
            scrollViewer.ViewportWidth,
            scrollViewer.ViewportHeight,
            PreviewImage.ActualWidth,
            PreviewImage.ActualHeight);
    }

    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        ApplyFitIfNeeded(resetOffsets: true);
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyFitIfNeeded(resetOffsets: true);
    }

    private void ApplyFitIfNeeded(bool resetOffsets)
    {
        var viewModel = _viewModel;
        if (viewModel is null || !viewModel.FitToWindow)
        {
            return;
        }

        if (PreviewImage.Source is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            viewModel.OnViewportSizeChanged(
                PreviewScrollViewer.ViewportWidth,
                PreviewScrollViewer.ViewportHeight,
                PreviewImage.ActualWidth,
                PreviewImage.ActualHeight);
            if (resetOffsets)
            {
                PreviewScrollViewer.ChangeView(0, 0, viewModel.ZoomFactor, disableAnimation: true);
            }
            else
            {
                PreviewScrollViewer.ChangeView(null, null, viewModel.ZoomFactor, disableAnimation: true);
            }
        });

    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (DataContext is not PreviewPaneViewModel viewModel)
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

    private void OnMaximizeChecked(object sender, RoutedEventArgs e)
    {
        MaximizeChanged?.Invoke(this, true);
    }

    private void OnMaximizeUnchecked(object sender, RoutedEventArgs e)
    {
        MaximizeChanged?.Invoke(this, false);
    }
}
