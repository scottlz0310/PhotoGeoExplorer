using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Services.PanZoom;

namespace PhotoGeoExplorer.Panes.Preview;

/// <summary>
/// PreviewPaneViewControl のコードビハインド
/// 入力は <see cref="PanZoomInputAdapter"/>、状態は ViewModel の <see cref="PanZoomController"/> に任せ、変換の描画への反映だけを行う
/// </summary>
internal sealed partial class PreviewPaneViewControl : UserControl
{
    private PreviewPaneViewModel? _viewModel;
    private PanZoomInputAdapter? _inputAdapter;

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
        _viewModel?.Fit();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PreviewPaneViewModel);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as PreviewPaneViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
    }

    private void AttachViewModel(PreviewPaneViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        DetachViewModel();
        if (viewModel is null)
        {
            return;
        }

        _viewModel = viewModel;
        viewModel.PanZoom.Changed += OnPanZoomChanged;
        _inputAdapter = new PanZoomInputAdapter(
            PreviewViewport,
            new PanZoomControllerTarget(viewModel.PanZoom, PreviewPaneViewModel.WheelFactorPerNotch));
        ApplyTransform(viewModel.PanZoom.Transform);

        // 表示領域の大きさは ViewModel の接続より先に通知済みのことがあるため、いまの大きさを渡す。
        // 画像の寸法は ViewModel が保持している。ペインの付け替えで再接続されても、拡大状態は保つ
        viewModel.OnViewportSizeChanged(PreviewViewport.ActualWidth, PreviewViewport.ActualHeight);
    }

    private void DetachViewModel()
    {
        _inputAdapter?.Detach();
        _inputAdapter = null;

        if (_viewModel is not null)
        {
            _viewModel.PanZoom.Changed -= OnPanZoomChanged;
            _viewModel = null;
        }
    }

    private void OnPanZoomChanged(object? sender, PanZoomTransform transform)
    {
        ApplyTransform(transform);
    }

    private void ApplyTransform(PanZoomTransform transform)
    {
        PreviewImageTransform.ScaleX = transform.Scale;
        PreviewImageTransform.ScaleY = transform.Scale;
        PreviewImageTransform.TranslateX = transform.TranslateX;
        PreviewImageTransform.TranslateY = transform.TranslateY;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 拡大した画像が角丸の枠の外へはみ出さないよう、表示領域で切り取る
        PreviewViewport.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
        _viewModel?.OnViewportSizeChanged(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewImage.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 } bitmap)
        {
            _viewModel?.OnImageOpened(bitmap.PixelWidth, bitmap.PixelHeight);
        }
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
