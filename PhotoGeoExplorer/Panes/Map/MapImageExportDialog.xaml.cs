using System;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoGeoExplorer.Services;
using Windows.Globalization.NumberFormatting;
using Windows.Storage.Streams;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 地図画像の保存ダイアログ。条件の管理とプレビューの描画は <see cref="MapImageExportViewModel"/> が行う。
/// </summary>
internal sealed partial class MapImageExportDialog : ContentDialog
{
    // 表示は縮小するため、この幅に抑えて復号する
    private const int PreviewDecodeWidth = 960;

    private readonly MapImageExportViewModel _viewModel;

    internal MapImageExportDialog(MapImageExportViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        WidthBox.NumberFormatter = CreateFormatter(fractionDigits: 0);
        HeightBox.NumberFormatter = CreateFormatter(fractionDigits: 0);
        ZoomBox.NumberFormatter = CreateFormatter(fractionDigits: 2);
        ZoomBox.Minimum = MapImageExportLimits.MinLogicalZoom;
        ZoomBox.Maximum = MapImageExportLimits.MaxLogicalZoom;

        DataContext = viewModel;

        // 範囲を先に決めてから値を結び付ける（既定の範囲で値が丸められないようにする）
        ZoomBox.SetBinding(NumberBox.ValueProperty, new Binding
        {
            Path = new PropertyPath(nameof(MapImageExportViewModel.Zoom)),
            Mode = BindingMode.TwoWay
        });

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private static DecimalFormatter CreateFormatter(int fractionDigits)
        => new()
        {
            IntegerDigits = 1,
            FractionDigits = fractionDigits,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder { Increment = Math.Pow(10, -fractionDigits) }
        };

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowPreview();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapImageExportViewModel.PreviewPng))
        {
            ShowPreview();
        }
    }

    private async void ShowPreview()
    {
        var png = _viewModel.PreviewPng;
        if (png is null)
        {
            PreviewImage.Source = null;
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = PreviewDecodeWidth };
            await bitmap.SetSourceAsync(stream);

            // 復号の間に次のプレビューへ変わっていたら、反映しない
            if (ReferenceEquals(_viewModel.PreviewPng, png))
            {
                PreviewImage.Source = bitmap;
            }
        }
        // async void のため、失敗はここで終端する
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            AppLog.Error("Failed to show the map image preview.", ex);
        }
    }
}
