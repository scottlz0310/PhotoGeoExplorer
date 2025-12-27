#include "pch.h"
#include "PhotoGeoPreviewHandler.h"

// Window class name
static const wchar_t* const PREVIEW_WINDOW_CLASS = L"PhotoGeoPreviewWindow";
static bool g_classRegistered = false;

// Register window class
static void EnsureWindowClassRegistered(HINSTANCE hInstance)
{
    if (g_classRegistered) return;

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = CPhotoGeoPreviewHandler::WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = PREVIEW_WINDOW_CLASS;
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.style = CS_HREDRAW | CS_VREDRAW;

    if (RegisterClassExW(&wc))
    {
        g_classRegistered = true;
    }
}

// Static window procedure
LRESULT CALLBACK CPhotoGeoPreviewHandler::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    CPhotoGeoPreviewHandler* pThis = nullptr;

    if (msg == WM_CREATE)
    {
        CREATESTRUCT* pCreate = reinterpret_cast<CREATESTRUCT*>(lParam);
        pThis = reinterpret_cast<CPhotoGeoPreviewHandler*>(pCreate->lpCreateParams);
        SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(pThis));
    }
    else
    {
        pThis = reinterpret_cast<CPhotoGeoPreviewHandler*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
    }

    switch (msg)
    {
    case WM_PAINT:
        if (pThis)
        {
            PAINTSTRUCT ps;
            HDC hdc = BeginPaint(hwnd, &ps);
            pThis->Paint(hdc);
            EndPaint(hwnd, &ps);
            return 0;
        }
        break;

    case WM_ERASEBKGND:
        return 1; // We handle background in WM_PAINT
    }

    return DefWindowProc(hwnd, msg, wParam, lParam);
}

void CPhotoGeoPreviewHandler::FinalRelease()
{
    if (m_hBitmap)
    {
        DeleteObject(m_hBitmap);
        m_hBitmap = nullptr;
    }
}

// IPreviewHandler::SetWindow
STDMETHODIMP CPhotoGeoPreviewHandler::SetWindow(HWND hwnd, const RECT* prc)
{
    if (!hwnd || !prc)
    {
        return E_INVALIDARG;
    }

    m_hwndParent = hwnd;
    m_rcParent = *prc;

    if (m_hwndPreview)
    {
        SetWindowPos(
            m_hwndPreview,
            nullptr,
            m_rcParent.left,
            m_rcParent.top,
            m_rcParent.right - m_rcParent.left,
            m_rcParent.bottom - m_rcParent.top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    return S_OK;
}

// IPreviewHandler::SetRect
STDMETHODIMP CPhotoGeoPreviewHandler::SetRect(const RECT* prc)
{
    if (!prc)
    {
        return E_INVALIDARG;
    }

    m_rcParent = *prc;

    if (m_hwndPreview)
    {
        SetWindowPos(
            m_hwndPreview,
            nullptr,
            m_rcParent.left,
            m_rcParent.top,
            m_rcParent.right - m_rcParent.left,
            m_rcParent.bottom - m_rcParent.top,
            SWP_NOZORDER | SWP_NOACTIVATE);
        InvalidateRect(m_hwndPreview, nullptr, TRUE);
    }

    return S_OK;
}

// IPreviewHandler::DoPreview
STDMETHODIMP CPhotoGeoPreviewHandler::DoPreview()
{
    if (m_filePath.empty())
    {
        return E_FAIL;
    }

    // Load the image first
    HRESULT hr = LoadImage();
    if (FAILED(hr))
    {
        return hr;
    }

    // Ensure window class is registered
    EnsureWindowClassRegistered(_AtlBaseModule.GetModuleInstance());

    // Create preview window
    m_hwndPreview = CreateWindowExW(
        0,
        PREVIEW_WINDOW_CLASS,
        nullptr,
        WS_CHILD | WS_VISIBLE,
        m_rcParent.left,
        m_rcParent.top,
        m_rcParent.right - m_rcParent.left,
        m_rcParent.bottom - m_rcParent.top,
        m_hwndParent,
        nullptr,
        _AtlBaseModule.GetModuleInstance(),
        this);

    if (!m_hwndPreview)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    return S_OK;
}

// IPreviewHandler::Unload
STDMETHODIMP CPhotoGeoPreviewHandler::Unload()
{
    if (m_hwndPreview)
    {
        DestroyWindow(m_hwndPreview);
        m_hwndPreview = nullptr;
    }

    if (m_hBitmap)
    {
        DeleteObject(m_hBitmap);
        m_hBitmap = nullptr;
    }

    m_filePath.clear();
    return S_OK;
}

// IPreviewHandler::SetFocus
STDMETHODIMP CPhotoGeoPreviewHandler::SetFocus()
{
    if (m_hwndPreview)
    {
        ::SetFocus(m_hwndPreview);
        return S_OK;
    }
    return S_FALSE;
}

// IPreviewHandler::QueryFocus
STDMETHODIMP CPhotoGeoPreviewHandler::QueryFocus(HWND* phwnd)
{
    if (!phwnd) return E_INVALIDARG;
    *phwnd = ::GetFocus();
    return S_OK;
}

// IPreviewHandler::TranslateAccelerator
STDMETHODIMP CPhotoGeoPreviewHandler::TranslateAccelerator(MSG* pmsg)
{
    return S_FALSE;
}

// IInitializeWithFile::Initialize
STDMETHODIMP CPhotoGeoPreviewHandler::Initialize(LPCWSTR pszFilePath, DWORD grfMode)
{
    if (!pszFilePath)
    {
        return E_INVALIDARG;
    }
    m_filePath = pszFilePath;
    return S_OK;
}

// IObjectWithSite
STDMETHODIMP CPhotoGeoPreviewHandler::SetSite(IUnknown* pUnkSite)
{
    m_site = pUnkSite;
    return S_OK;
}

STDMETHODIMP CPhotoGeoPreviewHandler::GetSite(REFIID riid, void** ppvSite)
{
    if (!ppvSite) return E_POINTER;
    if (m_site) return m_site->QueryInterface(riid, ppvSite);
    return E_FAIL;
}

// IPreviewHandlerVisuals
STDMETHODIMP CPhotoGeoPreviewHandler::SetBackgroundColor(COLORREF color)
{
    m_bgColor = color;
    if (m_hwndPreview) InvalidateRect(m_hwndPreview, nullptr, TRUE);
    return S_OK;
}

STDMETHODIMP CPhotoGeoPreviewHandler::SetFont(const LOGFONTW* plf) { return S_OK; }
STDMETHODIMP CPhotoGeoPreviewHandler::SetTextColor(COLORREF color) { return S_OK; }

// Load image using WIC
HRESULT CPhotoGeoPreviewHandler::LoadImage()
{
    wil::com_ptr<IWICImagingFactory> factory;
    HRESULT hr = CoCreateInstance(
        CLSID_WICImagingFactory,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromFilename(
        m_filePath.c_str(),
        nullptr,
        GENERIC_READ,
        WICDecodeMetadataCacheOnDemand,
        &decoder);
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return hr;

    UINT width, height;
    hr = frame->GetSize(&width, &height);
    if (FAILED(hr)) return hr;

    m_imageWidth = static_cast<int>(width);
    m_imageHeight = static_cast<int>(height);

    // Convert to 32bpp BGRA
    wil::com_ptr<IWICFormatConverter> converter;
    hr = factory->CreateFormatConverter(&converter);
    if (FAILED(hr)) return hr;

    hr = converter->Initialize(
        frame.get(),
        GUID_WICPixelFormat32bppBGRA,
        WICBitmapDitherTypeNone,
        nullptr,
        0.0,
        WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) return hr;

    // Create DIB section
    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = m_imageWidth;
    bmi.bmiHeader.biHeight = -m_imageHeight; // Top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* pvBits = nullptr;
    HDC hdc = GetDC(nullptr);
    m_hBitmap = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &pvBits, nullptr, 0);
    ReleaseDC(nullptr, hdc);

    if (!m_hBitmap || !pvBits) return E_FAIL;

    // Copy pixels
    UINT stride = m_imageWidth * 4;
    UINT bufferSize = stride * m_imageHeight;
    hr = converter->CopyPixels(nullptr, stride, bufferSize, static_cast<BYTE*>(pvBits));

    return hr;
}

// Paint the preview
void CPhotoGeoPreviewHandler::Paint(HDC hdc)
{
    RECT rc;
    GetClientRect(m_hwndPreview, &rc);

    // Fill background
    HBRUSH hBrush = CreateSolidBrush(m_bgColor);
    FillRect(hdc, &rc, hBrush);
    DeleteObject(hBrush);

    if (!m_hBitmap) return;

    // Calculate scaled size maintaining aspect ratio
    int clientWidth = rc.right - rc.left;
    int clientHeight = rc.bottom - rc.top;

    float scaleX = static_cast<float>(clientWidth) / m_imageWidth;
    float scaleY = static_cast<float>(clientHeight) / m_imageHeight;
    float scale = (scaleX < scaleY) ? scaleX : scaleY;

    int drawWidth = static_cast<int>(m_imageWidth * scale);
    int drawHeight = static_cast<int>(m_imageHeight * scale);

    int x = (clientWidth - drawWidth) / 2;
    int y = (clientHeight - drawHeight) / 2;

    // Draw image
    HDC hdcMem = CreateCompatibleDC(hdc);
    HBITMAP hOldBitmap = static_cast<HBITMAP>(SelectObject(hdcMem, m_hBitmap));

    SetStretchBltMode(hdc, HALFTONE);
    SetBrushOrgEx(hdc, 0, 0, nullptr);
    StretchBlt(hdc, x, y, drawWidth, drawHeight,
               hdcMem, 0, 0, m_imageWidth, m_imageHeight, SRCCOPY);

    SelectObject(hdcMem, hOldBitmap);
    DeleteDC(hdcMem);
}
