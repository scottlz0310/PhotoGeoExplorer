using System;
using System.ComponentModel;
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

    /// <summary>
    /// 最大化状態が変更されたときに発生するイベント
    /// </summary>
    public event EventHandler<bool>? MaximizeChanged;

    public PreviewPaneViewControl()
    {
        InitializeComponent();
    }

    internal void EnsureViewModelAttached()
    {
        AttachViewModel(DataContext as PreviewPaneViewModel);
    }

    internal void SetFitToWindow()
    {
        EnsureViewModelAttached();
        if (_viewModel is not null)
        {
            _viewModel.FitToWindow = true;
        }
    }

    internal void RequestRefitIfNeeded()
    {
        EnsureViewModelAttached();
        var viewModel = _viewModel;
        if (viewModel is null || !viewModel.FitToWindow)
        {
            return;
        }

        if (viewModel.FitCommand.CanExecute(null))
        {
            viewModel.FitCommand.Execute(null);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PreviewPaneViewModel);
        await SubscribeToXamlRootChangedAsync().ConfigureAwait(true);
        ApplyFitIfNeeded(resetOffsets: false, remainingRetries: 3);
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
        ApplyFitIfNeeded(resetOffsets: true, remainingRetries: 3);
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

        var xamlRoot = await EnsureXamlRootAsync().ConfigureAwait(true);
        if (!IsLoaded || xamlRoot is null)
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
        if (_subscribedXamlRoot is null)
        {
            return;
        }

        _subscribedXamlRoot.Changed -= OnXamlRootChanged;
        _subscribedXamlRoot = null;
    }

    private async Task<XamlRoot?> EnsureXamlRootAsync()
    {
        const int maxWaitMs = 3000;
        const int intervalMs = 50;

        if (XamlRoot is not null)
        {
            return XamlRoot;
        }

        AppLog.Info("PreviewPaneViewControl: XamlRoot is null, waiting for it to become available...");

        var elapsed = 0;
        while (XamlRoot is null && elapsed < maxWaitMs && IsLoaded)
        {
            await Task.Delay(intervalMs).ConfigureAwait(true);
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
        if (!HasMeaningfulRasterizationScaleChange(_lastRasterizationScale, newScale))
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
        ApplyFitIfNeeded(resetOffsets: true, remainingRetries: 3);
    }

    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyFitIfNeeded(resetOffsets: true, remainingRetries: 3);
    }

    private void ApplyFitIfNeeded(bool resetOffsets, int remainingRetries = 0)
    {
        EnsureViewModelAttached();
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
            if (!IsLoaded)
            {
                return;
            }

            var viewportWidth = PreviewScrollViewer.ViewportWidth;
            var viewportHeight = PreviewScrollViewer.ViewportHeight;
            if (ShouldRetryFit(viewportWidth, viewportHeight, remainingRetries))
            {
                _ = RetryApplyFitAsync(resetOffsets, remainingRetries - 1);
                return;
            }

            viewModel.OnViewportSizeChanged(
                viewportWidth,
                viewportHeight,
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

    private async Task RetryApplyFitAsync(bool resetOffsets, int remainingRetries)
    {
        await Task.Delay(50).ConfigureAwait(true);
        if (!IsLoaded)
        {
            return;
        }

        ApplyFitIfNeeded(resetOffsets, remainingRetries);
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

        if (!HasZoomFactorChanged(current, target))
        {
            return;
        }

        var cursor = point.Position;
        var targetOffsets = CalculateZoomOffsets(
            current,
            scrollViewer.HorizontalOffset,
            scrollViewer.VerticalOffset,
            cursor.X,
            cursor.Y,
            target);

        scrollViewer.ChangeView(targetOffsets.TargetOffsetX, targetOffsets.TargetOffsetY, target, disableAnimation: true);
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
        var targetOffsets = CalculateDragOffsets(_dragStartHorizontalOffset, _dragStartVerticalOffset, deltaX, deltaY);
        scrollViewer.ChangeView(
            targetOffsets.HorizontalOffset,
            targetOffsets.VerticalOffset,
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

    internal static bool HasMeaningfulRasterizationScaleChange(double previousScale, double nextScale)
    {
        return Math.Abs(nextScale - previousScale) >= 0.0001;
    }

    internal static bool ShouldRetryFit(double viewportWidth, double viewportHeight, int remainingRetries)
    {
        return (viewportWidth <= 0 || viewportHeight <= 0) && remainingRetries > 0;
    }

    internal static bool HasZoomFactorChanged(double currentZoom, double targetZoom)
    {
        return Math.Abs(targetZoom - currentZoom) >= 0.0001f;
    }

    internal static (double TargetOffsetX, double TargetOffsetY) CalculateZoomOffsets(
        double currentZoom,
        double horizontalOffset,
        double verticalOffset,
        double cursorX,
        double cursorY,
        double targetZoom)
    {
        var contentX = (horizontalOffset + cursorX) / currentZoom;
        var contentY = (verticalOffset + cursorY) / currentZoom;
        return (
            TargetOffsetX: contentX * targetZoom - cursorX,
            TargetOffsetY: contentY * targetZoom - cursorY);
    }

    internal static (double HorizontalOffset, double VerticalOffset) CalculateDragOffsets(
        double dragStartHorizontalOffset,
        double dragStartVerticalOffset,
        double deltaX,
        double deltaY)
    {
        return (
            HorizontalOffset: dragStartHorizontalOffset - deltaX,
            VerticalOffset: dragStartVerticalOffset - deltaY);
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
