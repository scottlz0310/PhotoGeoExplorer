using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PhotoGeoPreviewHandler.Utils;

namespace PhotoGeoPreviewHandler.Controls;

/// <summary>
/// WPF UserControl for displaying image preview with geolocation map.
/// </summary>
public partial class PreviewHandlerControl : UserControl, IDisposable
{
    private bool _isDisposed;

    public PreviewHandlerControl()
    {
        InitializeComponent();
        InitializeWebView();
    }

    /// <summary>
    /// Initializes the WebView2 control.
    /// </summary>
    private async void InitializeWebView()
    {
        try
        {
            MapLoadingText.Visibility = Visibility.Visible;
            await MapWebView.EnsureCoreWebView2Async();
            MapLoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to initialize map: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the preview from a stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream containing the image data</param>
    public async Task LoadPreviewAsync(Stream stream)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(PreviewHandlerControl));
        }

        try
        {
            // Show loading indicator
            LoadingText.Visibility = Visibility.Visible;
            ImagePreview.Source = null;
            ErrorText.Visibility = Visibility.Collapsed;

            // Copy stream to memory (required for COM stream handling)
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Load image
            await LoadImageAsync(memoryStream);

            // Extract EXIF GPS data
            memoryStream.Position = 0;
            var gpsCoordinates = await ExtractGpsDataAsync(memoryStream);

            // Load map with coordinates
            await LoadMapAsync(gpsCoordinates);

            // Hide loading indicator
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load preview: {ex.Message}");
            LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Loads the image into the Image control.
    /// </summary>
    private Task LoadImageAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze(); // Allow cross-thread access

            // Update UI on dispatcher thread
            Dispatcher.Invoke(() =>
            {
                ImagePreview.Source = bitmap;
            });
        });
    }

    /// <summary>
    /// Extracts GPS coordinates from EXIF data.
    /// </summary>
    private Task<GpsCoordinate?> ExtractGpsDataAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            try
            {
                return ExifDataExtractor.ExtractGpsCoordinates(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GPS extraction failed: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Loads the map with the given GPS coordinates.
    /// </summary>
    private async Task LoadMapAsync(GpsCoordinate? coordinates)
    {
        if (MapWebView.CoreWebView2 == null)
        {
            return;
        }

        // TODO: Generate HTML with Leaflet map
        // This will be implemented in the next phase
        var html = GenerateMapHtml(coordinates);

        await Dispatcher.InvokeAsync(() =>
        {
            MapWebView.NavigateToString(html);
        });
    }

    /// <summary>
    /// Generates HTML for the map display.
    /// </summary>
    private string GenerateMapHtml(GpsCoordinate? coordinates)
    {
        return MapHtmlGenerator.GenerateMapHtml(coordinates);
    }

    /// <summary>
    /// Displays an error message in the preview area.
    /// </summary>
    public void ShowError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            LoadingText.Visibility = Visibility.Collapsed;
            ImagePreview.Source = null;
        });
    }

    /// <summary>
    /// Disposes resources used by the control.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            MapWebView?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
