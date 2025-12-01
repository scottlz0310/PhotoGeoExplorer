#include "pch.h"
#include "PhotoGeoPreviewHandler.h"
#include <sstream>
#include <fstream>
#include <filesystem>
#include <algorithm>
#include <wrl/event.h>

namespace fs = std::filesystem;
using namespace Microsoft::WRL;

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

        if (m_webViewController)
        {
            RECT bounds;
            GetClientRect(m_hwndPreview, &bounds);
            m_webViewController->put_Bounds(bounds);
        }
    }
    else
    {
        m_hwndPreview = CreateWindowEx(
            0,
            L"Static",
            nullptr,
            WS_CHILD | WS_VISIBLE,
            m_rcParent.left,
            m_rcParent.top,
            m_rcParent.right - m_rcParent.left,
            m_rcParent.bottom - m_rcParent.top,
            m_hwndParent,
            nullptr,
            nullptr,
            nullptr);

        if (!m_hwndPreview)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
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

        if (m_webViewController)
        {
            RECT bounds;
            GetClientRect(m_hwndPreview, &bounds);
            m_webViewController->put_Bounds(bounds);
        }
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

    if (!m_hwndPreview)
    {
        // SetWindow should be called before DoPreview
        return E_FAIL;
    }

    return CreateWebView();
}

// IPreviewHandler::Unload
STDMETHODIMP CPhotoGeoPreviewHandler::Unload()
{
    DestroyWebView();

    if (m_hwndPreview)
    {
        DestroyWindow(m_hwndPreview);
        m_hwndPreview = nullptr;
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
        if (m_webViewController)
        {
            m_webViewController->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
        }
        return S_OK;
    }
    return S_FALSE;
}

// IPreviewHandler::QueryFocus
STDMETHODIMP CPhotoGeoPreviewHandler::QueryFocus(HWND* phwnd)
{
    if (!phwnd)
    {
        return E_INVALIDARG;
    }

    *phwnd = ::GetFocus();
    return S_OK;
}

// IPreviewHandler::TranslateAccelerator
STDMETHODIMP CPhotoGeoPreviewHandler::TranslateAccelerator(MSG* pmsg)
{
    // Pass keys to WebView2 if needed, but for now just return S_FALSE
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

// IObjectWithSite::SetSite
STDMETHODIMP CPhotoGeoPreviewHandler::SetSite(IUnknown* pUnkSite)
{
    m_site = pUnkSite;
    return S_OK;
}

// IObjectWithSite::GetSite
STDMETHODIMP CPhotoGeoPreviewHandler::GetSite(REFIID riid, void** ppvSite)
{
    if (!ppvSite)
    {
        return E_POINTER;
    }

    if (m_site)
    {
        return m_site->QueryInterface(riid, ppvSite);
    }

    return E_FAIL;
}

// IPreviewHandlerVisuals
STDMETHODIMP CPhotoGeoPreviewHandler::SetBackgroundColor(COLORREF color) { return S_OK; }
STDMETHODIMP CPhotoGeoPreviewHandler::SetFont(const LOGFONTW* plf) { return S_OK; }
STDMETHODIMP CPhotoGeoPreviewHandler::SetTextColor(COLORREF color) { return S_OK; }

// WebView2 Creation
HRESULT CPhotoGeoPreviewHandler::CreateWebView()
{
    if (m_webViewController)
    {
        return NavigateToHtml();
    }

    // Create event for synchronization
    m_webViewReadyEvent = CreateEvent(nullptr, TRUE, FALSE, nullptr);
    if (!m_webViewReadyEvent) return E_FAIL;

    // Use temp folder for WebView2 user data
    wchar_t tempPath[MAX_PATH];
    GetTempPathW(MAX_PATH, tempPath);
    std::wstring userDataFolder = std::wstring(tempPath) + L"PhotoGeoPreview";

    HRESULT initResult = E_FAIL;

    auto callback = Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
        [this, &initResult](HRESULT result, ICoreWebView2Environment* env) -> HRESULT
        {
            if (FAILED(result))
            {
                initResult = result;
                SetEvent(m_webViewReadyEvent);
                return result;
            }

            m_webViewEnvironment = env;

            env->CreateCoreWebView2Controller(
                m_hwndPreview,
                Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                    [this, &initResult](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT
                    {
                        if (FAILED(result))
                        {
                            initResult = result;
                            SetEvent(m_webViewReadyEvent);
                            return result;
                        }

                        m_webViewController = controller;
                        m_webViewController->get_CoreWebView2(&m_webView);

                        RECT bounds;
                        GetClientRect(m_hwndPreview, &bounds);
                        m_webViewController->put_Bounds(bounds);

                        wil::com_ptr<ICoreWebView2Settings> settings;
                        m_webView->get_Settings(&settings);
                        if (settings)
                        {
                            settings->put_AreDefaultContextMenusEnabled(FALSE);
                            settings->put_AreDevToolsEnabled(FALSE);
                        }

                        initResult = NavigateToHtml();
                        SetEvent(m_webViewReadyEvent);
                        return initResult;
                    }).Get());

            return S_OK;
        });

    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(
        nullptr, userDataFolder.c_str(), nullptr, callback.Get());

    if (FAILED(hr))
    {
        CloseHandle(m_webViewReadyEvent);
        m_webViewReadyEvent = nullptr;
        return hr;
    }

    // Wait for WebView2 initialization with message pump
    MSG msg;
    while (WaitForSingleObject(m_webViewReadyEvent, 0) == WAIT_TIMEOUT)
    {
        if (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    CloseHandle(m_webViewReadyEvent);
    m_webViewReadyEvent = nullptr;

    return initResult;
}

void CPhotoGeoPreviewHandler::DestroyWebView()
{
    if (m_webViewController)
    {
        m_webViewController->Close();
        m_webViewController = nullptr;
    }
    m_webView = nullptr;
    m_webViewEnvironment = nullptr;
}

HRESULT CPhotoGeoPreviewHandler::NavigateToHtml()
{
    if (!m_webView) return E_FAIL;

    std::wstring htmlContent = GenerateHtmlContent();

    // Use NavigateToString for simplicity, or save to temp file if content is large
    return m_webView->NavigateToString(htmlContent.c_str());
}

std::wstring CPhotoGeoPreviewHandler::GenerateHtmlContent()
{
    std::wstring html = LoadHtmlTemplate();

    double lat = 0, lon = 0;
    bool hasGps = false;
    ExtractGpsData(&lat, &lon, &hasGps);

    // Escape path for JS string
    std::wstring escapedPath = m_filePath;
    std::replace(escapedPath.begin(), escapedPath.end(), L'\\', L'/');
    // Simple escape for '
    ReplaceAll(escapedPath, L"'", L"\\'");

    ReplaceAll(html, L"{IMAGE_PATH}", L"file:///" + escapedPath);

    if (hasGps)
    {
        ReplaceAll(html, L"{LAT}", std::to_wstring(lat));
        ReplaceAll(html, L"{LON}", std::to_wstring(lon));
        ReplaceAll(html, L"{HAS_GPS}", L"true");
        ReplaceAll(html, L"{SPLITTER_HIDDEN}", L"");
        ReplaceAll(html, L"{MAP_HIDDEN}", L"");
        ReplaceAll(html, L"{NOGPS_HIDDEN}", L"hidden");
    }
    else
    {
        ReplaceAll(html, L"{LAT}", L"0");
        ReplaceAll(html, L"{LON}", L"0");
        ReplaceAll(html, L"{HAS_GPS}", L"false");
        ReplaceAll(html, L"{SPLITTER_HIDDEN}", L"hidden");
        ReplaceAll(html, L"{MAP_HIDDEN}", L"hidden");
        ReplaceAll(html, L"{NOGPS_HIDDEN}", L"");
    }

    return html;
}

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

        // Ref (N/S)
        hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=1}", &var);
        if (SUCCEEDED(hr) && var.vt == VT_LPSTR && var.pszVal[0] == 'S')
        {
            *lat = -(*lat);
        }
        PropVariantClear(&var);

        // Longitude
        hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=4}", &var);
        if (SUCCEEDED(hr))
        {
            *lon = ConvertGpsCoordinate(var);
            PropVariantClear(&var);

            // Ref (E/W)
            hr = reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=3}", &var);
            if (SUCCEEDED(hr) && var.vt == VT_LPSTR && var.pszVal[0] == 'W')
            {
                *lon = -(*lon);
            }
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

    // WIC often returns VT_UI4 | VT_VECTOR (array of 6 ULONGs: num, den, num, den, num, den)
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
    // Sometimes VT_R8 | VT_VECTOR (array of 3 doubles)
    else if (propVar.vt == (VT_R8 | VT_VECTOR) && propVar.cadbl.cElems >= 3)
    {
        degrees = propVar.cadbl.pElems[0];
        minutes = propVar.cadbl.pElems[1];
        seconds = propVar.cadbl.pElems[2];
    }

    return degrees + (minutes / 60.0) + (seconds / 3600.0);
}

std::wstring CPhotoGeoPreviewHandler::LoadHtmlTemplate()
{
    // Get path to DLL
    wchar_t dllPath[MAX_PATH];
    GetModuleFileNameW(_AtlBaseModule.GetModuleInstance(), dllPath, MAX_PATH);

    fs::path path(dllPath);
    path = path.parent_path() / L"Resources" / L"template.html";

    std::wifstream file(path);
    if (!file.is_open()) return L"<html><body>Error loading template</body></html>";

    std::wstringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

void CPhotoGeoPreviewHandler::ReplaceAll(std::wstring& str, const std::wstring& from, const std::wstring& to)
{
    if (from.empty()) return;
    size_t start_pos = 0;
    while ((start_pos = str.find(from, start_pos)) != std::wstring::npos)
    {
        str.replace(start_pos, from.length(), to);
        start_pos += to.length();
    }
}
