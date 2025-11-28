using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PhotoGeoPreviewHandler.Services;

/// <summary>
/// COM interface definitions for Windows Preview Handler
/// </summary>
internal static class ComInterfaces
{
    // Shell Preview Handler CLSID and interface IDs
    public const string IPreviewHandlerGuid = "8895b1c6-b41f-4c1c-a562-0d564250836f";
    public const string IInitializeWithStreamGuid = "b7d14566-0509-4cce-a71f-0a554233bd9b";
    public const string IOleWindowGuid = "00000114-0000-0000-C000-000000000046";
    public const string IObjectWithSiteGuid = "fc4801a3-2ba9-11cf-a229-00aa003d7352";
}

/// <summary>
/// Exposes methods for initializing a handler with a stream.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComInterfaces.IInitializeWithStreamGuid)]
internal interface IInitializeWithStream
{
    /// <summary>
    /// Initializes the handler with a stream.
    /// </summary>
    /// <param name="stream">The stream to initialize with</param>
    /// <param name="grfMode">File access mode flags</param>
    void Initialize(IStream stream, uint grfMode);
}

/// <summary>
/// Exposes methods for displaying a preview of a file.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComInterfaces.IPreviewHandlerGuid)]
internal interface IPreviewHandler
{
    /// <summary>
    /// Sets the parent window for the preview.
    /// </summary>
    void SetWindow(IntPtr hwnd, ref RECT rect);

    /// <summary>
    /// Sets the preview area rectangle.
    /// </summary>
    void SetRect(ref RECT rect);

    /// <summary>
    /// Directs the preview handler to load data from the source.
    /// </summary>
    void DoPreview();

    /// <summary>
    /// Directs the preview handler to stop rendering.
    /// </summary>
    void Unload();

    /// <summary>
    /// Sets the focus to the preview window.
    /// </summary>
    void SetFocus();

    /// <summary>
    /// Queries the preview handler for the optimal size of the preview.
    /// </summary>
    void QueryFocus(out IntPtr phwnd);

    /// <summary>
    /// Directs the preview handler to handle a keystroke.
    /// </summary>
    [PreserveSig]
    int TranslateAccelerator(ref MSG pmsg);
}

/// <summary>
/// Provides methods that allow a container to pass an object a pointer to the interface for its site.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComInterfaces.IObjectWithSiteGuid)]
internal interface IObjectWithSite
{
    /// <summary>
    /// Provides the site's IUnknown pointer to the object.
    /// </summary>
    void SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);

    /// <summary>
    /// Gets the last site set with SetSite.
    /// </summary>
    void GetSite(ref Guid riid, out IntPtr ppvSite);
}

/// <summary>
/// Provides methods that retrieve the window handle and control enabling of modeless dialog boxes.
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComInterfaces.IOleWindowGuid)]
internal interface IOleWindow
{
    /// <summary>
    /// Retrieves a handle to one of the windows participating in in-place activation.
    /// </summary>
    void GetWindow(out IntPtr phwnd);

    /// <summary>
    /// Determines whether context-sensitive help mode should be entered during an in-place activation session.
    /// </summary>
    void ContextSensitiveHelp(bool fEnterMode);
}

/// <summary>
/// Rectangle structure for COM interop.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Message structure for COM interop.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int pt_x;
    public int pt_y;
}
