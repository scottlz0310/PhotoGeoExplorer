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

    PROPVARIANT varLat, varLatRef, varLon, varLonRef;
    PropVariantInit(&varLat);
    PropVariantInit(&varLatRef);
    PropVariantInit(&varLon);
    PropVariantInit(&varLonRef);

    // Try multiple metadata paths for GPS coordinates
    // JPEG: /app1/ifd/gps/...
    // Some cameras: /ifd/gps/...
    const wchar_t* latPaths[] = { L"/app1/ifd/gps/{ushort=2}", L"/ifd/gps/{ushort=2}", L"/app1/ifd/gps/subifd:{ushort=2}" };
    const wchar_t* latRefPaths[] = { L"/app1/ifd/gps/{ushort=1}", L"/ifd/gps/{ushort=1}", L"/app1/ifd/gps/subifd:{ushort=1}" };
    const wchar_t* lonPaths[] = { L"/app1/ifd/gps/{ushort=4}", L"/ifd/gps/{ushort=4}", L"/app1/ifd/gps/subifd:{ushort=4}" };
    const wchar_t* lonRefPaths[] = { L"/app1/ifd/gps/{ushort=3}", L"/ifd/gps/{ushort=3}", L"/app1/ifd/gps/subifd:{ushort=3}" };

    bool foundLat = false, foundLon = false;

    for (int i = 0; i < 3 && !foundLat; i++)
    {
        hr = reader->GetMetadataByName(latPaths[i], &varLat);
        if (SUCCEEDED(hr))
        {
            *lat = ConvertGpsCoordinate(varLat);
            foundLat = true;

            hr = reader->GetMetadataByName(latRefPaths[i], &varLatRef);
            if (SUCCEEDED(hr))
            {
                if ((varLatRef.vt == VT_LPSTR && varLatRef.pszVal && varLatRef.pszVal[0] == 'S') ||
                    (varLatRef.vt == VT_LPWSTR && varLatRef.pwszVal && varLatRef.pwszVal[0] == L'S'))
                {
                    *lat = -(*lat);
                }
            }
        }
    }

    for (int i = 0; i < 3 && !foundLon; i++)
    {
        hr = reader->GetMetadataByName(lonPaths[i], &varLon);
        if (SUCCEEDED(hr))
        {
            *lon = ConvertGpsCoordinate(varLon);
            foundLon = true;

            hr = reader->GetMetadataByName(lonRefPaths[i], &varLonRef);
            if (SUCCEEDED(hr))
            {
                if ((varLonRef.vt == VT_LPSTR && varLonRef.pszVal && varLonRef.pszVal[0] == 'W') ||
                    (varLonRef.vt == VT_LPWSTR && varLonRef.pwszVal && varLonRef.pwszVal[0] == L'W'))
                {
                    *lon = -(*lon);
                }
            }
        }
    }

    PropVariantClear(&varLat);
    PropVariantClear(&varLatRef);
    PropVariantClear(&varLon);
    PropVariantClear(&varLonRef);

    *hasGps = foundLat && foundLon && (*lat != 0 || *lon != 0);

    return S_OK;
}

double CPhotoGeoPreviewHandler::ConvertGpsCoordinate(const PROPVARIANT& propVar)
{
    double degrees = 0.0, minutes = 0.0, seconds = 0.0;

    // VT_VECTOR | VT_UI8 - array of ULARGE_INTEGER (rational as num/den pairs)
    if ((propVar.vt == (VT_VECTOR | VT_UI8)) && propVar.cauh.cElems >= 3)
    {
        // Each element is a ULARGE_INTEGER representing num/den as LowPart/HighPart
        ULARGE_INTEGER* pElems = propVar.cauh.pElems;

        ULONG dNum = pElems[0].LowPart;
        ULONG dDen = pElems[0].HighPart;
        ULONG mNum = pElems[1].LowPart;
        ULONG mDen = pElems[1].HighPart;
        ULONG sNum = pElems[2].LowPart;
        ULONG sDen = pElems[2].HighPart;

        degrees = (dDen != 0) ? (double)dNum / dDen : 0;
        minutes = (mDen != 0) ? (double)mNum / mDen : 0;
        seconds = (sDen != 0) ? (double)sNum / sDen : 0;
    }
    // VT_VECTOR | VT_UI4 - array of ULONG (older format: 6 elements for 3 rationals)
    else if ((propVar.vt == (VT_VECTOR | VT_UI4)) && propVar.caul.cElems >= 6)
    {
        ULONG* pElems = propVar.caul.pElems;

        degrees = (pElems[1] != 0) ? (double)pElems[0] / pElems[1] : 0;
        minutes = (pElems[3] != 0) ? (double)pElems[2] / pElems[3] : 0;
        seconds = (pElems[5] != 0) ? (double)pElems[4] / pElems[5] : 0;
    }
    // VT_VECTOR | VT_R8 - array of doubles
    else if ((propVar.vt == (VT_VECTOR | VT_R8)) && propVar.cadbl.cElems >= 3)
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

    // Use direct connection, not proxy-dependent
    HINTERNET hInternet = InternetOpenW(L"Mozilla/5.0 PhotoGeoPreview/1.0", INTERNET_OPEN_TYPE_DIRECT, nullptr, nullptr, 0);
    if (!hInternet) return E_FAIL;

    // Set timeouts
    DWORD timeout = 10000; // 10 seconds
    InternetSetOptionW(hInternet, INTERNET_OPTION_CONNECT_TIMEOUT, &timeout, sizeof(timeout));
    InternetSetOptionW(hInternet, INTERNET_OPTION_RECEIVE_TIMEOUT, &timeout, sizeof(timeout));

    HINTERNET hUrl = InternetOpenUrlW(hInternet, url.str().c_str(), nullptr, 0,
        INTERNET_FLAG_RELOAD | INTERNET_FLAG_NO_CACHE_WRITE, 0);
    if (!hUrl)
    {
        InternetCloseHandle(hInternet);
        return E_FAIL;
    }

    // Check HTTP status code
    DWORD statusCode = 0;
    DWORD statusSize = sizeof(statusCode);
    if (HttpQueryInfoW(hUrl, HTTP_QUERY_STATUS_CODE | HTTP_QUERY_FLAG_NUMBER, &statusCode, &statusSize, nullptr))
    {
        if (statusCode != 200)
        {
            InternetCloseHandle(hUrl);
            InternetCloseHandle(hInternet);
            return E_FAIL;
        }
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
// Falls back to simple coordinate display if download fails
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

    HDC hdcMem = CreateCompatibleDC(hdc);
    HBITMAP hOldBitmap = static_cast<HBITMAP>(SelectObject(hdcMem, m_hMapBitmap));

    // Fill with light blue (water/ocean color)
    HBRUSH hBgBrush = CreateSolidBrush(RGB(170, 211, 223));
    RECT bgRect = { 0, 0, m_mapWidth, m_mapHeight };
    FillRect(hdcMem, &bgRect, hBgBrush);
    DeleteObject(hBgBrush);

    // Try to download tiles
    bool anyTileDownloaded = false;

    // Create WIC factory for decoding PNG
    wil::com_ptr<IWICImagingFactory> factory;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));

    if (SUCCEEDED(hr))
    {
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

                                        anyTileDownloaded = true;

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
    }

    // If no tiles were downloaded, draw a simple coordinate display
    if (!anyTileDownloaded)
    {
        // Draw decorative background with gradient-like effect
        for (int y = 0; y < m_mapHeight; y++)
        {
            int shade = 200 + (y * 40 / m_mapHeight);
            HPEN hLinePen = CreatePen(PS_SOLID, 1, RGB(shade, shade + 10, shade + 20));
            HPEN hOldLinePen = static_cast<HPEN>(SelectObject(hdcMem, hLinePen));
            MoveToEx(hdcMem, 0, y, nullptr);
            LineTo(hdcMem, m_mapWidth, y);
            SelectObject(hdcMem, hOldLinePen);
            DeleteObject(hLinePen);
        }

        // Draw grid lines
        HPEN hGridPen = CreatePen(PS_SOLID, 1, RGB(180, 200, 220));
        HPEN hOldPen = static_cast<HPEN>(SelectObject(hdcMem, hGridPen));

        // Draw latitude/longitude grid
        for (int i = 0; i <= 6; i++)
        {
            int pos = i * m_mapWidth / 6;
            MoveToEx(hdcMem, pos, 0, nullptr);
            LineTo(hdcMem, pos, m_mapHeight);
            MoveToEx(hdcMem, 0, pos, nullptr);
            LineTo(hdcMem, m_mapWidth, pos);
        }

        SelectObject(hdcMem, hOldPen);
        DeleteObject(hGridPen);

        SetBkMode(hdcMem, TRANSPARENT);

        // Draw location icon (compass rose style)
        int cx = m_mapWidth / 2;
        int cy = m_mapHeight / 2 - 30;

        HPEN hCompassPen = CreatePen(PS_SOLID, 2, RGB(70, 100, 140));
        SelectObject(hdcMem, hCompassPen);

        // Draw compass circle
        HBRUSH hOldBrush2 = static_cast<HBRUSH>(SelectObject(hdcMem, GetStockObject(NULL_BRUSH)));
        Ellipse(hdcMem, cx - 40, cy - 40, cx + 40, cy + 40);

        // Draw N-S-E-W lines
        MoveToEx(hdcMem, cx, cy - 35, nullptr);
        LineTo(hdcMem, cx, cy + 35);
        MoveToEx(hdcMem, cx - 35, cy, nullptr);
        LineTo(hdcMem, cx + 35, cy);

        SelectObject(hdcMem, hOldBrush2);
        DeleteObject(hCompassPen);

        // Draw "N" at top
        HFONT hCompassFont = CreateFontW(16, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
        HFONT hOldFont = static_cast<HFONT>(SelectObject(hdcMem, hCompassFont));
        ::SetTextColor(hdcMem, RGB(200, 60, 60));
        TextOutW(hdcMem, cx - 5, cy - 58, L"N", 1);
        DeleteObject(hCompassFont);

        // Draw coordinate text - larger and more prominent
        HFONT hFont = CreateFontW(22, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
        SelectObject(hdcMem, hFont);
        ::SetTextColor(hdcMem, RGB(40, 70, 110));

        // Format coordinates (English only to avoid font issues)
        wchar_t latStr[64], lonStr[64];
        swprintf_s(latStr, L"Lat: %.6f %s", abs(m_latitude), m_latitude >= 0 ? L"N" : L"S");
        swprintf_s(lonStr, L"Lon: %.6f %s", abs(m_longitude), m_longitude >= 0 ? L"E" : L"W");

        RECT textRect = { 10, m_mapHeight - 80, m_mapWidth - 10, m_mapHeight - 50 };
        DrawTextW(hdcMem, latStr, -1, &textRect, DT_CENTER | DT_SINGLELINE);

        textRect.top = m_mapHeight - 50;
        textRect.bottom = m_mapHeight - 20;
        DrawTextW(hdcMem, lonStr, -1, &textRect, DT_CENTER | DT_SINGLELINE);

        SelectObject(hdcMem, hOldFont);
        DeleteObject(hFont);

        // Draw title
        HFONT hTitleFont = CreateFontW(18, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
        SelectObject(hdcMem, hTitleFont);
        ::SetTextColor(hdcMem, RGB(70, 100, 140));

        RECT titleRect = { 10, 10, m_mapWidth - 10, 35 };
        DrawTextW(hdcMem, L"Photo Location", -1, &titleRect, DT_CENTER | DT_SINGLELINE);

        DeleteObject(hTitleFont);

        // Draw pin marker at compass center
        int markerX = cx;
        int markerY = cy;

        // Draw location pin (red with white border)
        HBRUSH hRedBrush = CreateSolidBrush(RGB(220, 50, 50));
        HBRUSH hWhiteBrush = CreateSolidBrush(RGB(255, 255, 255));
        HPEN hMarkerPen = CreatePen(PS_SOLID, 2, RGB(180, 40, 40));
        HPEN hOldMarkerPen = static_cast<HPEN>(SelectObject(hdcMem, hMarkerPen));
        HBRUSH hOldMarkerBrush = static_cast<HBRUSH>(SelectObject(hdcMem, hRedBrush));

        // Draw pin head
        Ellipse(hdcMem, markerX - 10, markerY - 10, markerX + 10, markerY + 10);

        // Draw white center
        SelectObject(hdcMem, hWhiteBrush);
        Ellipse(hdcMem, markerX - 4, markerY - 4, markerX + 4, markerY + 4);

        SelectObject(hdcMem, hOldMarkerBrush);
        SelectObject(hdcMem, hOldMarkerPen);
        DeleteObject(hRedBrush);
        DeleteObject(hWhiteBrush);
        DeleteObject(hMarkerPen);
    }
    else
    {
        // Tiles were downloaded - draw marker at the correct position
        int markerX = TILE_SIZE + static_cast<int>(fracX * TILE_SIZE);
        int markerY = TILE_SIZE + static_cast<int>(fracY * TILE_SIZE);

        // Draw location pin (red with white border)
        HBRUSH hRedBrush = CreateSolidBrush(RGB(220, 50, 50));
        HBRUSH hWhiteBrush = CreateSolidBrush(RGB(255, 255, 255));
        HPEN hMarkerPen = CreatePen(PS_SOLID, 2, RGB(180, 40, 40));
        HPEN hOldMarkerPen = static_cast<HPEN>(SelectObject(hdcMem, hMarkerPen));
        HBRUSH hOldMarkerBrush = static_cast<HBRUSH>(SelectObject(hdcMem, hRedBrush));

        // Draw pin head
        Ellipse(hdcMem, markerX - 10, markerY - 10, markerX + 10, markerY + 10);

        // Draw white center
        SelectObject(hdcMem, hWhiteBrush);
        Ellipse(hdcMem, markerX - 4, markerY - 4, markerX + 4, markerY + 4);

        SelectObject(hdcMem, hOldMarkerBrush);
        SelectObject(hdcMem, hOldMarkerPen);
        DeleteObject(hRedBrush);
        DeleteObject(hWhiteBrush);
        DeleteObject(hMarkerPen);
    }

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
        ::SetTextColor(hdc, RGB(128, 128, 128));
        DrawTextW(hdc, L"No GPS Data", -1, &textRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
}
