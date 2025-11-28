using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using PhotoGeoPreviewHandler.Controls;

namespace PhotoGeoPreviewHandler.Services;

/// <summary>
/// COM Preview Handler for displaying images with geolocation data.
/// Implements IPreviewHandler, IInitializeWithStream, IOleWindow, and IObjectWithSite.
/// </summary>
[ComVisible(true)]
[Guid("E7C3A7D9-4B5A-4F2E-8C1D-9B6E5F3A2D8C")] // Generate a unique GUID for your handler
[ClassInterface(ClassInterfaceType.None)]
[ProgId("PhotoGeoPreviewHandler.PreviewHandler")]
public class PreviewHandler : IPreviewHandler, IInitializeWithStream, IOleWindow, IObjectWithSite
{
    private IntPtr _parentWindow;
    private RECT _bounds;
    private IStream? _stream;
    private PreviewHandlerControl? _previewControl;
    private HwndSource? _hwndSource;
    private object? _site;

    #region IInitializeWithStream Implementation

    /// <summary>
    /// Initializes the preview handler with a stream containing the file data.
    /// </summary>
    public void Initialize(IStream stream, uint grfMode)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    #endregion

    #region IPreviewHandler Implementation

    /// <summary>
    /// Sets the parent window and initial bounds for the preview.
    /// </summary>
    public void SetWindow(IntPtr hwnd, ref RECT rect)
    {
        _parentWindow = hwnd;
        _bounds = rect;
    }

    /// <summary>
    /// Updates the bounds of the preview area.
    /// </summary>
    public void SetRect(ref RECT rect)
    {
        _bounds = rect;
        UpdateBounds();
    }

    /// <summary>
    /// Creates and displays the preview content.
    /// </summary>
    public void DoPreview()
    {
        if (_stream == null)
        {
            throw new InvalidOperationException("Stream not initialized. Call Initialize first.");
        }

        // Create the WPF preview control
        _previewControl = new PreviewHandlerControl();

        // Create HwndSource to host WPF content in the preview pane
        var parameters = new HwndSourceParameters("PhotoGeoPreview")
        {
            ParentWindow = _parentWindow,
            PositionX = _bounds.Left,
            PositionY = _bounds.Top,
            Width = _bounds.Width,
            Height = _bounds.Height,
            WindowStyle = (int)(WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPCHILDREN)
        };

        _hwndSource = new HwndSource(parameters)
        {
            RootVisual = _previewControl
        };

        // Load the image stream asynchronously
        _ = LoadPreviewAsync(_stream);
    }

    /// <summary>
    /// Cleans up resources and unloads the preview.
    /// </summary>
    public void Unload()
    {
        _previewControl?.Dispose();
        _previewControl = null;

        _hwndSource?.Dispose();
        _hwndSource = null;

        if (_stream != null)
        {
            Marshal.ReleaseComObject(_stream);
            _stream = null;
        }
    }

    /// <summary>
    /// Sets focus to the preview window.
    /// </summary>
    public void SetFocus()
    {
        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            NativeMethods.SetFocus(_hwndSource.Handle);
        }
    }

    /// <summary>
    /// Queries which window currently has focus.
    /// </summary>
    public void QueryFocus(out IntPtr phwnd)
    {
        phwnd = NativeMethods.GetFocus();
    }

    /// <summary>
    /// Handles keyboard accelerator messages.
    /// </summary>
    public int TranslateAccelerator(ref MSG pmsg)
    {
        // Return S_FALSE to allow default handling
        return 1; // S_FALSE
    }

    #endregion

    #region IOleWindow Implementation

    /// <summary>
    /// Returns the window handle of the preview.
    /// </summary>
    public void GetWindow(out IntPtr phwnd)
    {
        phwnd = _hwndSource?.Handle ?? IntPtr.Zero;
    }

    /// <summary>
    /// Enables or disables context-sensitive help mode.
    /// </summary>
    public void ContextSensitiveHelp(bool fEnterMode)
    {
        // Not implemented for preview handlers
    }

    #endregion

    #region IObjectWithSite Implementation

    /// <summary>
    /// Sets the site object for this preview handler.
    /// </summary>
    public void SetSite(object pUnkSite)
    {
        _site = pUnkSite;
    }

    /// <summary>
    /// Gets the site object.
    /// </summary>
    public void GetSite(ref Guid riid, out IntPtr ppvSite)
    {
        if (_site != null)
        {
            IntPtr pUnknown = Marshal.GetIUnknownForObject(_site);
            Marshal.QueryInterface(pUnknown, ref riid, out ppvSite);
            Marshal.Release(pUnknown);
        }
        else
        {
            ppvSite = IntPtr.Zero;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads the preview content asynchronously from the stream.
    /// </summary>
    private async Task LoadPreviewAsync(IStream stream)
    {
        try
        {
            // Convert COM IStream to .NET Stream
            using var managedStream = new ComStreamWrapper(stream);

            // Load the preview in the control
            await _previewControl!.LoadPreviewAsync(managedStream);
        }
        catch (Exception ex)
        {
            // Log error and display error message in preview
            System.Diagnostics.Trace.WriteLine($"Preview load error: {ex.Message}");
            _previewControl?.ShowError($"Failed to load preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the window bounds when resized.
    /// </summary>
    private void UpdateBounds()
    {
        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwndSource.Handle,
                IntPtr.Zero,
                _bounds.Left,
                _bounds.Top,
                _bounds.Width,
                _bounds.Height,
                SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
        }
    }

    #endregion
}

#region Native Methods and Constants

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);
}

[Flags]
internal enum SetWindowPosFlags : uint
{
    SWP_NOSIZE = 0x0001,
    SWP_NOMOVE = 0x0002,
    SWP_NOZORDER = 0x0004,
    SWP_NOACTIVATE = 0x0010,
}

[Flags]
internal enum WindowStyles : uint
{
    WS_CHILD = 0x40000000,
    WS_VISIBLE = 0x10000000,
    WS_CLIPCHILDREN = 0x02000000,
}

#endregion
