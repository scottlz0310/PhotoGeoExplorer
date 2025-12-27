#include "pch.h"
#include "PhotoGeoPreviewHandler.h"
#include <sstream>
#include <fstream>
#include <filesystem>
#include <winrt/base.h>

namespace fs = std::filesystem;

PhotoGeoPreviewHandler::PhotoGeoPreviewHandler()
{
}

PhotoGeoPreviewHandler::~PhotoGeoPreviewHandler()
{
    DestroyWebView();
}

// IPreviewHandler::SetWindow
IFACEMETHODIMP PhotoGeoPreviewHandler::SetWindow(HWND hwnd, const RECT* prc)
{
    if (!hwnd || !prc)
    {
        return E_INVALIDARG;
    }

    m_hwndParent = hwnd;
    m_rcParent = *prc;

    if (m_hwndPreview)
    {
        // ウィンドウが既に存在する場合はリサイズ
        SetWindowPos(
            m_hwndPreview,
            nullptr,
            m_rcParent.left,
            m_rcParent.top,
            m_rcParent.right - m_rcParent.left,
            m_rcParent.bottom - m_rcParent.top,
            SWP_NOZORDER | SWP_NOACTIVATE);

        ResizeWebView();
    }
    else
    {
        // プレビューウィンドウを作成
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
IFACEMETHODIMP PhotoGeoPreviewHandler::SetRect(const RECT* prc)
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

        ResizeWebView();
    }

    return S_OK;
}

// IPreviewHandler::DoPreview
IFACEMETHODIMP PhotoGeoPreviewHandler::DoPreview()
{
    if (m_filePath.empty())
    {
        return E_FAIL;
    }

    // WebView2 を作成（まだ作成されていない場合）
    if (!m_webview)
    {
        RETURN_IF_FAILED(CreateWebView());
    }

    // GPS データを抽出
    double latitude = 0.0;
    double longitude = 0.0;
    bool hasGPS = false;

    HRESULT hr = ExtractGPSData(m_filePath.c_str(), latitude, longitude, hasGPS);

    // GPS データの抽出に失敗しても続行（GPS なし画像として表示）
    if (FAILED(hr))
    {
        hasGPS = false;
    }

    // HTML を生成
    std::wstring html = GenerateHTML(m_filePath, latitude, longitude, hasGPS);

    // 一時ファイルに保存して表示
    std::wstring tempHtmlPath = CreateTempHTMLFile(html);

    if (!tempHtmlPath.empty() && m_webview)
    {
        std::wstring uri = L"file:///" + tempHtmlPath;
        // パス区切りを / に変換
        std::replace(uri.begin(), uri.end(), L'\\', L'/');

        RETURN_IF_FAILED(m_webview->Navigate(uri.c_str()));
    }

    return S_OK;
}

// IPreviewHandler::Unload
IFACEMETHODIMP PhotoGeoPreviewHandler::Unload()
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
IFACEMETHODIMP PhotoGeoPreviewHandler::SetFocus()
{
    if (m_hwndPreview)
    {
        ::SetFocus(m_hwndPreview);
        return S_OK;
    }
    return E_FAIL;
}

// IPreviewHandler::QueryFocus
IFACEMETHODIMP PhotoGeoPreviewHandler::QueryFocus(HWND* phwnd)
{
    if (!phwnd)
    {
        return E_INVALIDARG;
    }

    *phwnd = GetFocus();
    return S_OK;
}

// IPreviewHandler::TranslateAccelerator
IFACEMETHODIMP PhotoGeoPreviewHandler::TranslateAccelerator(MSG* pmsg)
{
    return S_FALSE;
}

// IPreviewHandlerVisuals::SetBackgroundColor
IFACEMETHODIMP PhotoGeoPreviewHandler::SetBackgroundColor(COLORREF color)
{
    m_backgroundColor = color;
    return S_OK;
}

// IPreviewHandlerVisuals::SetFont
IFACEMETHODIMP PhotoGeoPreviewHandler::SetFont(const LOGFONTW* plf)
{
    return S_OK;
}

// IPreviewHandlerVisuals::SetTextColor
IFACEMETHODIMP PhotoGeoPreviewHandler::SetTextColor(COLORREF color)
{
    return S_OK;
}

// IOleWindow::GetWindow
IFACEMETHODIMP PhotoGeoPreviewHandler::GetWindow(HWND* phwnd)
{
    if (!phwnd)
    {
        return E_INVALIDARG;
    }

    *phwnd = m_hwndPreview;
    return S_OK;
}

// IOleWindow::ContextSensitiveHelp
IFACEMETHODIMP PhotoGeoPreviewHandler::ContextSensitiveHelp(BOOL fEnterMode)
{
    return E_NOTIMPL;
}

// IInitializeWithFile::Initialize
IFACEMETHODIMP PhotoGeoPreviewHandler::Initialize(LPCWSTR pszFilePath, DWORD grfMode)
{
    if (!pszFilePath)
    {
        return E_INVALIDARG;
    }

    m_filePath = pszFilePath;
    return S_OK;
}

// IObjectWithSite::SetSite
IFACEMETHODIMP PhotoGeoPreviewHandler::SetSite(IUnknown* punkSite)
{
    m_site = punkSite;
    return S_OK;
}

// IObjectWithSite::GetSite
IFACEMETHODIMP PhotoGeoPreviewHandler::GetSite(REFIID riid, void** ppvSite)
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

// WebView2 の作成
HRESULT PhotoGeoPreviewHandler::CreateWebView()
{
    if (!m_hwndPreview)
    {
        return E_FAIL;
    }

    // WebView2 環境を作成
    auto callback = Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
        [this](HRESULT result, ICoreWebView2Environment* env) -> HRESULT
        {
            if (FAILED(result))
            {
                return result;
            }

            m_webviewEnvironment = env;

            // WebView2 コントローラーを作成
            env->CreateCoreWebView2Controller(
                m_hwndPreview,
                Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                    [this](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT
                    {
                        if (FAILED(result))
                        {
                            return result;
                        }

                        m_webviewController = controller;
                        m_webviewController->get_CoreWebView2(&m_webview);

                        // WebView2 のサイズを設定
                        ResizeWebView();

                        m_webviewReady = true;

                        return S_OK;
                    }).Get());

            return S_OK;
        });

    return CreateCoreWebView2EnvironmentWithOptions(
        nullptr,
        nullptr,
        nullptr,
        callback.Get());
}

// WebView2 の破棄
void PhotoGeoPreviewHandler::DestroyWebView()
{
    if (m_webviewController)
    {
        m_webviewController->Close();
        m_webviewController = nullptr;
    }

    m_webview = nullptr;
    m_webviewEnvironment = nullptr;
    m_webviewReady = false;
}

// WebView2 のリサイズ
void PhotoGeoPreviewHandler::ResizeWebView()
{
    if (m_webviewController && m_hwndPreview)
    {
        RECT bounds;
        GetClientRect(m_hwndPreview, &bounds);
        m_webviewController->put_Bounds(bounds);
    }
}

// GPS データの抽出
HRESULT PhotoGeoPreviewHandler::ExtractGPSData(
    LPCWSTR filePath,
    double& latitude,
    double& longitude,
    bool& hasGPS)
{
    hasGPS = false;

    // WIC ファクトリーを作成
    wil::com_ptr<IWICImagingFactory> factory;
    RETURN_IF_FAILED(CoCreateInstance(
        CLSID_WICImagingFactory,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&factory)));

    // デコーダーを作成
    wil::com_ptr<IWICBitmapDecoder> decoder;
    RETURN_IF_FAILED(factory->CreateDecoderFromFilename(
        filePath,
        nullptr,
        GENERIC_READ,
        WICDecodeMetadataCacheOnDemand,
        &decoder));

    // フレームを取得
    wil::com_ptr<IWICBitmapFrameDecode> frame;
    RETURN_IF_FAILED(decoder->GetFrame(0, &frame));

    // メタデータリーダーを取得
    wil::com_ptr<IWICMetadataQueryReader> metadataReader;
    HRESULT hr = frame->GetMetadataQueryReader(&metadataReader);
    if (FAILED(hr))
    {
        return hr; // メタデータなし
    }

    // GPS 緯度を取得
    PROPVARIANT propLat;
    PropVariantInit(&propLat);
    hr = metadataReader->GetMetadataByName(L"/app1/ifd/gps/{ushort=2}", &propLat);
    if (SUCCEEDED(hr) && propLat.vt != VT_EMPTY)
    {
        latitude = ConvertGPSCoordinate(propLat);
        hasGPS = true;
    }
    PropVariantClear(&propLat);

    // 北緯/南緯の判定
    PROPVARIANT propLatRef;
    PropVariantInit(&propLatRef);
    hr = metadataReader->GetMetadataByName(L"/app1/ifd/gps/{ushort=1}", &propLatRef);
    if (SUCCEEDED(hr) && propLatRef.vt == VT_LPSTR)
    {
        if (propLatRef.pszVal[0] == 'S')
        {
            latitude = -latitude;
        }
    }
    PropVariantClear(&propLatRef);

    // GPS 経度を取得
    PROPVARIANT propLon;
    PropVariantInit(&propLon);
    hr = metadataReader->GetMetadataByName(L"/app1/ifd/gps/{ushort=4}", &propLon);
    if (SUCCEEDED(hr) && propLon.vt != VT_EMPTY)
    {
        longitude = ConvertGPSCoordinate(propLon);
    }
    PropVariantClear(&propLon);

    // 東経/西経の判定
    PROPVARIANT propLonRef;
    PropVariantInit(&propLonRef);
    hr = metadataReader->GetMetadataByName(L"/app1/ifd/gps/{ushort=3}", &propLonRef);
    if (SUCCEEDED(hr) && propLonRef.vt == VT_LPSTR)
    {
        if (propLonRef.pszVal[0] == 'W')
        {
            longitude = -longitude;
        }
    }
    PropVariantClear(&propLonRef);

    return hasGPS ? S_OK : E_FAIL;
}

// GPS 座標の変換（度分秒 → 10進数）
double PhotoGeoPreviewHandler::ConvertGPSCoordinate(const PROPVARIANT& propVar)
{
    // GPS座標は通常、3つの有理数（度、分、秒）の配列として保存されている
    // TODO: PROPVARIANT の配列から度分秒を取り出して10進数に変換
    // 現時点では簡易実装
    return 0.0;
}

// HTML の生成
std::wstring PhotoGeoPreviewHandler::GenerateHTML(
    const std::wstring& imagePath,
    double lat,
    double lon,
    bool hasGPS)
{
    std::wstring html = LoadHTMLTemplate();

    // 画像パスを URI エスケープ
    std::wstring escapedPath = UriEscape(imagePath);

    // プレースホルダーを置換
    ReplaceAll(html, L"{IMAGE_PATH}", escapedPath);

    if (hasGPS)
    {
        ReplaceAll(html, L"{LAT}", std::to_wstring(lat));
        ReplaceAll(html, L"{LON}", std::to_wstring(lon));
        ReplaceAll(html, L"{MAP_DISPLAY}", L"block");
        ReplaceAll(html, L"{NO_GPS_DISPLAY}", L"none");
    }
    else
    {
        ReplaceAll(html, L"{LAT}", L"0");
        ReplaceAll(html, L"{LON}", L"0");
        ReplaceAll(html, L"{MAP_DISPLAY}", L"none");
        ReplaceAll(html, L"{NO_GPS_DISPLAY}", L"block");
    }

    return html;
}

// HTML テンプレートの読み込み
std::wstring PhotoGeoPreviewHandler::LoadHTMLTemplate()
{
    std::wstring templatePath = GetResourcePath(L"template.html");
    return ReadTextFile(templatePath);
}

// ファイルパスの URI エスケープ
std::wstring PhotoGeoPreviewHandler::UriEscape(const std::wstring& path)
{
    // TODO: 適切な URI エスケープを実装
    // 現時点では単純なパス変換
    std::wstring escaped = path;
    std::replace(escaped.begin(), escaped.end(), L'\\', L'/');
    return escaped;
}

// プレースホルダーの置換
void PhotoGeoPreviewHandler::ReplaceAll(
    std::wstring& str,
    const std::wstring& from,
    const std::wstring& to)
{
    size_t pos = 0;
    while ((pos = str.find(from, pos)) != std::wstring::npos)
    {
        str.replace(pos, from.length(), to);
        pos += to.length();
    }
}

// 一時 HTML ファイルの作成
std::wstring PhotoGeoPreviewHandler::CreateTempHTMLFile(const std::wstring& htmlContent)
{
    wchar_t tempPath[MAX_PATH];
    wchar_t tempFile[MAX_PATH];

    GetTempPath(MAX_PATH, tempPath);
    GetTempFileName(tempPath, L"PGP", 0, tempFile);

    // 拡張子を .html に変更
    std::wstring tempHtmlPath = tempFile;
    tempHtmlPath = tempHtmlPath.substr(0, tempHtmlPath.find_last_of(L'.')) + L".html";

    std::wofstream file(tempHtmlPath);
    if (file.is_open())
    {
        file << htmlContent;
        file.close();
        return tempHtmlPath;
    }

    return L"";
}

// テキストファイルの読み込み
std::wstring PhotoGeoPreviewHandler::ReadTextFile(const std::wstring& filePath)
{
    std::wifstream file(filePath);
    if (!file.is_open())
    {
        return L"";
    }

    std::wstringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

// リソースファイルのパスを取得
std::wstring PhotoGeoPreviewHandler::GetResourcePath(const std::wstring& resourceName)
{
    // DLL のパスを取得
    wchar_t dllPath[MAX_PATH];
    GetModuleFileName(nullptr, dllPath, MAX_PATH);

    fs::path modulePath(dllPath);
    fs::path resourcePath = modulePath.parent_path() / L"Resources" / resourceName;

    return resourcePath.wstring();
}
