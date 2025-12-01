#include "pch.h"
#include "PhotoGeoPreviewHandler.h"
#include <wininet.h>
#include <sstream>

#pragma comment(lib, "wininet.lib")

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

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
        return 1;
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
    if (m_hMapBitmap)
    {
        DeleteObject(m_hMapBitmap);
        m_hMapBitmap = nullptr;
    }
}

// IPreviewHandler::SetWindow
STDMETHODIMP CPhotoGeoPreviewHandler::SetWindow(HWND hwnd, const RECT* prc)
{
    if (!hwnd || !prc) return E_INVALIDARG;

    m_hwndParent = hwnd;
    m_rcParent = *prc;

    if (m_hwndPreview)
    {
        SetWindowPos(m_hwndPreview, nullptr,
            m_rcParent.left, m_rcParent.top,
            m_rcParent.right - m_rcParent.left,
            m_rcParent.bottom - m_rcParent.top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    return S_OK;
}

// IPreviewHandler::SetRect
STDMETHODIMP CPhotoGeoPreviewHandler::SetRect(const RECT* prc)
{
    if (!prc) return E_INVALIDARG;

    m_rcParent = *prc;

    if (m_hwndPreview)
    {
        SetWindowPos(m_hwndPreview, nullptr,
            m_rcParent.left, m_rcParent.top,
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
    if (m_filePath.empty()) return E_FAIL;

    // Load the photo
    HRESULT hr = LoadImage();
    if (FAILED(hr)) return hr;

    // Extract GPS data
    ExtractGpsData(&m_latitude, &m_longitude, &m_hasGps);

    // Download map tiles if GPS is available
    if (m_hasGps)
    {
        DownloadMapTiles();
    }

    // Ensure window class is registered
    EnsureWindowClassRegistered(_AtlBaseModule.GetModuleInstance());

    // Create preview window
    m_hwndPreview = CreateWindowExW(
        0, PREVIEW_WINDOW_CLASS, nullptr,
        WS_CHILD | WS_VISIBLE,
        m_rcParent.left, m_rcParent.top,
        m_rcParent.right - m_rcParent.left,
        m_rcParent.bottom - m_rcParent.top,
        m_hwndParent, nullptr,
        _AtlBaseModule.GetModuleInstance(), this);

    if (!m_hwndPreview) return HRESULT_FROM_WIN32(GetLastError());

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

    if (m_hMapBitmap)
    {
        DeleteObject(m_hMapBitmap);
        m_hMapBitmap = nullptr;
    }

    m_filePath.clear();
    m_hasGps = false;
    return S_OK;
}

STDMETHODIMP CPhotoGeoPreviewHandler::SetFocus()
{
    if (m_hwndPreview) { ::SetFocus(m_hwndPreview); return S_OK; }
    return S_FALSE;
}

STDMETHODIMP CPhotoGeoPreviewHandler::QueryFocus(HWND* phwnd)
{
    if (!phwnd) return E_INVALIDARG;
    *phwnd = ::GetFocus();
    return S_OK;
}

STDMETHODIMP CPhotoGeoPreviewHandler::TranslateAccelerator(MSG* pmsg) { return S_FALSE; }

STDMETHODIMP CPhotoGeoPreviewHandler::Initialize(LPCWSTR pszFilePath, DWORD grfMode)
{
    if (!pszFilePath) return E_INVALIDARG;
    m_filePath = pszFilePath;
    return S_OK;
}

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
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromFilename(m_filePath.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return hr;

    UINT width, height;
    hr = frame->GetSize(&width, &height);
    if (FAILED(hr)) return hr;

    m_imageWidth = static_cast<int>(width);
    m_imageHeight = static_cast<int>(height);

    wil::com_ptr<IWICFormatConverter> converter;
    hr = factory->CreateFormatConverter(&converter);
    if (FAILED(hr)) return hr;

    hr = converter->Initialize(frame.get(), GUID_WICPixelFormat32bppBGRA, WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) return hr;

    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = m_imageWidth;
    bmi.bmiHeader.biHeight = -m_imageHeight;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* pvBits = nullptr;
    HDC hdc = GetDC(nullptr);
    m_hBitmap = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &pvBits, nullptr, 0);
    ReleaseDC(nullptr, hdc);

    if (!m_hBitmap || !pvBits) return E_FAIL;

    UINT stride = m_imageWidth * 4;
    UINT bufferSize = stride * m_imageHeight;
    hr = converter->CopyPixels(nullptr, stride, bufferSize, static_cast<BYTE*>(pvBits));

    return hr;
}

// Extract GPS data from EXIF
HRESULT CPhotoGeoPreviewHandler::ExtractGpsData(double* lat, double* lon, bool* hasGps)
{
    *hasGps = false;
    *lat = 0;
    *lon = 0;

    wil::com_ptr<IWICImagingFactory> factory;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromFilename(m_filePath.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return hr;

    wil::com_ptr<IWICMetadataQueryReader> reader;
    hr = frame->GetMetadataQueryReader(&reader);
    if (FAILED(hr)) return hr;

    PROPVARIANT var;
    PropVariantInit(&var);

    // Latitude
    hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=2}", &var);
    if (SUCCEEDED(hr))
    {
        *lat = ConvertGpsCoordinate(var);
        PropVariantClear(&var);

        hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=1}", &var);
        if (SUCCEEDED(hr) && var.vt == VT_LPSTR && var.pszVal[0] == 'S')
            *lat = -(*lat);
        PropVariantClear(&var);

        hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=4}", &var);
        if (SUCCEEDED(hr))
        {
            *lon = ConvertGpsCoordinate(var);
            PropVariantClear(&var);

            hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=3}", &var);
            if (SUCCEEDED(hr) && var.vt == VT_LPSTR && var.pszVal[0] == 'W')
                *lon = -(*lon);
            PropVariantClear(&var);

            *hasGps = true;
        }
    }
    PropVariantClear(&var);

    return S_OK;
}

double CPhotoGeoPreviewHandler::ConvertGpsCoordinate(const PROPVARIANT& propVar)
{
    double degrees = 0.0, minutes = 0.0, seconds = 0.0;

    if (propVar.vt == (VT_UI4 | VT_VECTOR) && propVar.caui.cElems >= 6)
    {
        double dNum = (double)propVar.caui.pElems[0];
        double dDen = (double)propVar.caui.pElems[1];
        double mNum = (double)propVar.caui.pElems[2];
        double mDen = (double)propVar.caui.pElems[3];
        double sNum = (double)propVar.caui.pElems[4];
        double sDen = (double)propVar.caui.pElems[5];

        degrees = (dDen != 0) ? dNum / dDen : 0;
        minutes = (mDen != 0) ? mNum / mDen : 0;
        seconds = (sDen != 0) ? sNum / sDen : 0;
    }
    else if (propVar.vt == (VT_R8 | VT_VECTOR) && propVar.cadbl.cElems >= 3)
    {
        degrees = propVar.cadbl.pElems[0];
        minutes = propVar.cadbl.pElems[1];
        seconds = propVar.cadbl.pElems[2];
    }

    return degrees + (minutes / 60.0) + (seconds / 3600.0);
}

// Convert lat/lon to tile coordinates
void CPhotoGeoPreviewHandler::LatLonToTile(double lat, double lon, int zoom, int& x, int& y, double& fracX, double& fracY)
{
    double n = pow(2.0, zoom);
    double latRad = lat * M_PI / 180.0;

    double xTile = n * ((lon + 180.0) / 360.0);
    double yTile = n * (1.0 - (log(tan(latRad) + 1.0 / cos(latRad)) / M_PI)) / 2.0;

    x = static_cast<int>(floor(xTile));
    y = static_cast<int>(floor(yTile));
    fracX = xTile - x;
    fracY = yTile - y;
}

// Download a single tile
HRESULT CPhotoGeoPreviewHandler::DownloadTile(int zoom, int x, int y, std::vector<BYTE>& data)
{
    data.clear();

    // Build URL: https://tile.openstreetmap.org/{zoom}/{x}/{y}.png
    std::wstringstream url;
    url << L"https://tile.openstreetmap.org/" << zoom << L"/" << x << L"/" << y << L".png";

    HINTERNET hInternet = InternetOpenW(L"PhotoGeoPreview/1.0", INTERNET_OPEN_TYPE_PRECONFIG, nullptr, nullptr, 0);
    if (!hInternet) return E_FAIL;

    HINTERNET hUrl = InternetOpenUrlW(hInternet, url.str().c_str(), nullptr, 0,
        INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE | INTERNET_FLAG_SECURE, 0);
    if (!hUrl)
    {
        InternetCloseHandle(hInternet);
        return E_FAIL;
    }

    BYTE buffer[4096];
    DWORD bytesRead;
    while (InternetReadFile(hUrl, buffer, sizeof(buffer), &bytesRead) && bytesRead > 0)
    {
        data.insert(data.end(), buffer, buffer + bytesRead);
    }

    InternetCloseHandle(hUrl);
    InternetCloseHandle(hInternet);

    return data.empty() ? E_FAIL : S_OK;
}

// Download map tiles and create bitmap
HRESULT CPhotoGeoPreviewHandler::DownloadMapTiles()
{
    int centerX, centerY;
    double fracX, fracY;
    LatLonToTile(m_latitude, m_longitude, MAP_ZOOM, centerX, centerY, fracX, fracY);

    // Download 3x3 tiles centered on the location
    const int GRID = 3;
    m_mapWidth = TILE_SIZE * GRID;
    m_mapHeight = TILE_SIZE * GRID;

    // Create map bitmap
    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = m_mapWidth;
    bmi.bmiHeader.biHeight = -m_mapHeight;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* pvBits = nullptr;
    HDC hdc = GetDC(nullptr);
    m_hMapBitmap = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &pvBits, nullptr, 0);

    if (!m_hMapBitmap)
    {
        ReleaseDC(nullptr, hdc);
        return E_FAIL;
    }

    // Fill with gray initially
    memset(pvBits, 0x40, m_mapWidth * m_mapHeight * 4);

    // Create WIC factory for decoding PNG
    wil::com_ptr<IWICImagingFactory> factory;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
    if (FAILED(hr))
    {
        ReleaseDC(nullptr, hdc);
        return hr;
    }

    HDC hdcMem = CreateCompatibleDC(hdc);
    HBITMAP hOldBitmap = static_cast<HBITMAP>(SelectObject(hdcMem, m_hMapBitmap));

    // Download and draw each tile
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            int tileX = centerX + dx;
            int tileY = centerY + dy;

            std::vector<BYTE> tileData;
            if (SUCCEEDED(DownloadTile(MAP_ZOOM, tileX, tileY, tileData)))
            {
                // Decode PNG using WIC
                wil::com_ptr<IWICStream> stream;
                if (SUCCEEDED(factory->CreateStream(&stream)) &&
                    SUCCEEDED(stream->InitializeFromMemory(tileData.data(), static_cast<DWORD>(tileData.size()))))
                {
                    wil::com_ptr<IWICBitmapDecoder> decoder;
                    if (SUCCEEDED(factory->CreateDecoderFromStream(stream.get(), nullptr, WICDecodeMetadataCacheOnDemand, &decoder)))
                    {
                        wil::com_ptr<IWICBitmapFrameDecode> frame;
                        if (SUCCEEDED(decoder->GetFrame(0, &frame)))
                        {
                            wil::com_ptr<IWICFormatConverter> converter;
                            if (SUCCEEDED(factory->CreateFormatConverter(&converter)) &&
                                SUCCEEDED(converter->Initialize(frame.get(), GUID_WICPixelFormat32bppBGRA,
                                    WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom)))
                            {
                                // Create tile bitmap
                                BITMAPINFO tileBmi = {};
                                tileBmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
                                tileBmi.bmiHeader.biWidth = TILE_SIZE;
                                tileBmi.bmiHeader.biHeight = -TILE_SIZE;
                                tileBmi.bmiHeader.biPlanes = 1;
                                tileBmi.bmiHeader.biBitCount = 32;
                                tileBmi.bmiHeader.biCompression = BI_RGB;

                                void* tileBits = nullptr;
                                HBITMAP hTileBitmap = CreateDIBSection(hdc, &tileBmi, DIB_RGB_COLORS, &tileBits, nullptr, 0);
                                if (hTileBitmap && tileBits)
                                {
                                    converter->CopyPixels(nullptr, TILE_SIZE * 4, TILE_SIZE * TILE_SIZE * 4, static_cast<BYTE*>(tileBits));

                                    HDC hdcTile = CreateCompatibleDC(hdc);
                                    HBITMAP hOldTile = static_cast<HBITMAP>(SelectObject(hdcTile, hTileBitmap));

                                    int destX = (dx + 1) * TILE_SIZE;
                                    int destY = (dy + 1) * TILE_SIZE;
                                    BitBlt(hdcMem, destX, destY, TILE_SIZE, TILE_SIZE, hdcTile, 0, 0, SRCCOPY);

                                    SelectObject(hdcTile, hOldTile);
                                    DeleteDC(hdcTile);
                                    DeleteObject(hTileBitmap);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Draw marker at center
    int markerX = TILE_SIZE + static_cast<int>(fracX * TILE_SIZE);
    int markerY = TILE_SIZE + static_cast<int>(fracY * TILE_SIZE);

    // Draw red circle marker
    HBRUSH hRedBrush = CreateSolidBrush(RGB(255, 0, 0));
    HBRUSH hWhiteBrush = CreateSolidBrush(RGB(255, 255, 255));
    HPEN hPen = CreatePen(PS_SOLID, 2, RGB(255, 255, 255));
    HPEN hOldPen = static_cast<HPEN>(SelectObject(hdcMem, hPen));
    HBRUSH hOldBrush = static_cast<HBRUSH>(SelectObject(hdcMem, hRedBrush));

    Ellipse(hdcMem, markerX - 8, markerY - 8, markerX + 8, markerY + 8);

    SelectObject(hdcMem, hWhiteBrush);
    Ellipse(hdcMem, markerX - 3, markerY - 3, markerX + 3, markerY + 3);

    SelectObject(hdcMem, hOldBrush);
    SelectObject(hdcMem, hOldPen);
    DeleteObject(hRedBrush);
    DeleteObject(hWhiteBrush);
    DeleteObject(hPen);

    SelectObject(hdcMem, hOldBitmap);
    DeleteDC(hdcMem);
    ReleaseDC(nullptr, hdc);

    return S_OK;
}

// Paint the preview
void CPhotoGeoPreviewHandler::Paint(HDC hdc)
{
    RECT rc;
    GetClientRect(m_hwndPreview, &rc);

    int clientWidth = rc.right - rc.left;
    int clientHeight = rc.bottom - rc.top;

    // Fill background
    HBRUSH hBrush = CreateSolidBrush(m_bgColor);
    FillRect(hdc, &rc, hBrush);
    DeleteObject(hBrush);

    if (!m_hBitmap) return;

    // Calculate layout: image on top, map on bottom (if GPS available)
    int imageAreaHeight = m_hasGps ? clientHeight / 2 : clientHeight;
    int mapAreaHeight = m_hasGps ? clientHeight - imageAreaHeight : 0;

    // Draw image (centered, scaled to fit)
    {
        float scaleX = static_cast<float>(clientWidth) / m_imageWidth;
        float scaleY = static_cast<float>(imageAreaHeight) / m_imageHeight;
        float scale = (scaleX < scaleY) ? scaleX : scaleY;

        int drawWidth = static_cast<int>(m_imageWidth * scale);
        int drawHeight = static_cast<int>(m_imageHeight * scale);
        int x = (clientWidth - drawWidth) / 2;
        int y = (imageAreaHeight - drawHeight) / 2;

        HDC hdcMem = CreateCompatibleDC(hdc);
        HBITMAP hOldBitmap = static_cast<HBITMAP>(SelectObject(hdcMem, m_hBitmap));

        SetStretchBltMode(hdc, HALFTONE);
        SetBrushOrgEx(hdc, 0, 0, nullptr);
        StretchBlt(hdc, x, y, drawWidth, drawHeight, hdcMem, 0, 0, m_imageWidth, m_imageHeight, SRCCOPY);

        SelectObject(hdcMem, hOldBitmap);
        DeleteDC(hdcMem);
    }

    // Draw map (if GPS available)
    if (m_hasGps && m_hMapBitmap)
    {
        // Draw separator line
        HPEN hSepPen = CreatePen(PS_SOLID, 2, RGB(0, 120, 215));
        HPEN hOldPen = static_cast<HPEN>(SelectObject(hdc, hSepPen));
        MoveToEx(hdc, 0, imageAreaHeight, nullptr);
        LineTo(hdc, clientWidth, imageAreaHeight);
        SelectObject(hdc, hOldPen);
        DeleteObject(hSepPen);

        // Calculate map drawing area
        float scaleX = static_cast<float>(clientWidth) / m_mapWidth;
        float scaleY = static_cast<float>(mapAreaHeight) / m_mapHeight;
        float scale = (scaleX < scaleY) ? scaleX : scaleY;

        int drawWidth = static_cast<int>(m_mapWidth * scale);
        int drawHeight = static_cast<int>(m_mapHeight * scale);
        int x = (clientWidth - drawWidth) / 2;
        int y = imageAreaHeight + (mapAreaHeight - drawHeight) / 2;

        HDC hdcMem = CreateCompatibleDC(hdc);
        HBITMAP hOldBitmap = static_cast<HBITMAP>(SelectObject(hdcMem, m_hMapBitmap));

        SetStretchBltMode(hdc, HALFTONE);
        StretchBlt(hdc, x, y, drawWidth, drawHeight, hdcMem, 0, 0, m_mapWidth, m_mapHeight, SRCCOPY);

        SelectObject(hdcMem, hOldBitmap);
        DeleteDC(hdcMem);
    }
    else if (!m_hasGps)
    {
        // Draw "No GPS" message at bottom
        RECT textRect = { 0, clientHeight - 30, clientWidth, clientHeight };
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, RGB(128, 128, 128));
        DrawTextW(hdc, L"GPS情報がありません", -1, &textRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
}
