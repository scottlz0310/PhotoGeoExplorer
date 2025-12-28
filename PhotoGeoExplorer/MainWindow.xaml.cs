using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace PhotoGeoExplorer;

[SuppressMessage("Design", "CA1515:Consider making public types internal")]
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _mapReady;
    private bool _mapInitialized;
    private WebView2? _mapWebView;
    private bool _windowSized;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new FileSystemService());
        RootGrid.DataContext = _viewModel;
        AppLog.Info("MainWindow constructed.");
        Activated += OnActivated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_mapInitialized)
        {
            return;
        }

        EnsureWindowSize();
        _mapInitialized = true;
        AppLog.Info("MainWindow activated.");
        await InitializeMapAsync().ConfigureAwait(true);
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        await UpdateMapFromSelectionAsync().ConfigureAwait(true);
    }

    private void EnsureWindowSize()
    {
        if (_windowSized)
        {
            return;
        }

        _windowSized = true;

        try
        {
            AppWindow.Resize(new SizeInt32(1200, 800));
        }
        catch (ArgumentException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Failed to set initial window size.", ex);
        }
    }

    private async Task InitializeMapAsync()
    {
        if (_mapWebView is not null)
        {
            return;
        }

        try
        {
            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                AppLog.Error($"Map page not found: {indexPath}");
                ShowMapStatus("Map page missing. See log.");
                return;
            }

            AppLog.Info("Initializing WebView2.");
            var webView = new WebView2();
            MapHost.Children.Clear();
            MapHost.Children.Add(webView);
            _mapWebView = webView;
            await webView.EnsureCoreWebView2Async().AsTask().ConfigureAwait(true);
            webView.Source = new Uri(indexPath);
            MapStatusText.Visibility = Visibility.Collapsed;
            AppLog.Info("WebView2 initialized.");
        }
        catch (TypeLoadException ex)
        {
            AppLog.Error("Map WebView2 type load failed.", ex);
            ShowMapStatus("WebView2 unavailable. See log.");
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (IOException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
        catch (UriFormatException ex)
        {
            AppLog.Error("Map WebView2 init failed.", ex);
            ShowMapStatus("WebView2 init failed. See log.");
        }
    }

    private void ShowMapStatus(string message)
    {
        MapStatusText.Text = message;
        MapStatusText.Visibility = Visibility.Visible;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedMetadata) or nameof(MainViewModel.SelectedItem))
        {
            await UpdateMapFromSelectionAsync().ConfigureAwait(true);
        }
    }

    private async Task UpdateMapFromSelectionAsync()
    {
        if (_mapWebView is null)
        {
            return;
        }

        var selectedItem = _viewModel.SelectedItem;
        if (selectedItem is null)
        {
            await SetMapMarkersAsync("[]").ConfigureAwait(true);
            ShowMapStatus("Select a photo to show location.");
            return;
        }

        var metadata = _viewModel.SelectedMetadata;
        if (metadata?.HasLocation != true)
        {
            await SetMapMarkersAsync("[]").ConfigureAwait(true);
            ShowMapStatus("Location data not found.");
            return;
        }

        var markers = new[]
        {
            new Dictionary<string, object?>
            {
                ["lat"] = metadata.Latitude,
                ["lon"] = metadata.Longitude,
                ["label"] = selectedItem.FileName
            }
        };
        var json = JsonSerializer.Serialize(markers);
        await SetMapMarkersAsync(json).ConfigureAwait(true);
        MapStatusText.Visibility = Visibility.Collapsed;
    }

    private async Task SetMapMarkersAsync(string markersJson)
    {
        if (_mapWebView is null)
        {
            return;
        }

        if (!await EnsureMapReadyAsync().ConfigureAwait(true))
        {
            return;
        }

        var script = $"window.PhotoGeoExplorer?.setMarkers({markersJson});";
        await _mapWebView.ExecuteScriptAsync(script).AsTask().ConfigureAwait(true);
    }

    private async Task<bool> EnsureMapReadyAsync()
    {
        if (_mapWebView is null)
        {
            return false;
        }

        if (_mapReady)
        {
            return true;
        }

        const int maxAttempts = 15;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var result = await _mapWebView
                    .ExecuteScriptAsync("typeof window.PhotoGeoExplorer !== 'undefined'")
                    .AsTask()
                    .ConfigureAwait(true);

                if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    _mapReady = true;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            await Task.Delay(100).ConfigureAwait(true);
        }

        return false;
    }

    private async void OnNavigateHomeClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.OpenHomeAsync().ConfigureAwait(true);
    }

    private async void OnNavigateUpClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.NavigateUpAsync().ConfigureAwait(true);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnApplyFiltersClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnFiltersChanged(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFolder? folder;
        try
        {
            folder = await picker.PickSingleFolderAsync().AsTask().ConfigureAwait(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
            return;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Error("Folder picker failed.", ex);
            return;
        }

        if (folder is null)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(folder.Path).ConfigureAwait(true);
    }

    private async void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not BreadcrumbSegment segment)
        {
            return;
        }

        await _viewModel.LoadFolderAsync(segment.FullPath).ConfigureAwait(true);
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }
}
