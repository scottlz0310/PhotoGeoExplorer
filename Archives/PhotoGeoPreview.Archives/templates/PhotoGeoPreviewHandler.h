#pragma once

#include <Windows.h>
#include <wrl/module.h>
#include <wil/com.h>
#include <wil/resource.h>
#include <Shlwapi.h>
#include <Shlobj.h>
#include <string>

// WebView2
#include <WebView2.h>
#include <wrl.h>

// WIC (Windows Imaging Component) for EXIF
#include <wincodec.h>
#include <propvarutil.h>

// Preview Handler インターフェイス
#include <Shobjidl.h>

using namespace Microsoft::WRL;

// PhotoGeoPreview Preview Handler
// GPS メタデータを含む画像ファイルをプレビューするためのハンドラー
// WebView2 を使用して画像と Leaflet 地図を表示
class PhotoGeoPreviewHandler :
    public RuntimeClass<
        RuntimeClassFlags<ClassicCom>,
        IPreviewHandler,
        IPreviewHandlerVisuals,
        IOleWindow,
        IInitializeWithFile,
        IObjectWithSite>
{
public:
    PhotoGeoPreviewHandler();
    virtual ~PhotoGeoPreviewHandler();

    // IPreviewHandler メソッド
    IFACEMETHODIMP SetWindow(HWND hwnd, const RECT* prc) override;
    IFACEMETHODIMP SetRect(const RECT* prc) override;
    IFACEMETHODIMP DoPreview() override;
    IFACEMETHODIMP Unload() override;
    IFACEMETHODIMP SetFocus() override;
    IFACEMETHODIMP QueryFocus(HWND* phwnd) override;
    IFACEMETHODIMP TranslateAccelerator(MSG* pmsg) override;

    // IPreviewHandlerVisuals メソッド
    IFACEMETHODIMP SetBackgroundColor(COLORREF color) override;
    IFACEMETHODIMP SetFont(const LOGFONTW* plf) override;
    IFACEMETHODIMP SetTextColor(COLORREF color) override;

    // IOleWindow メソッド
    IFACEMETHODIMP GetWindow(HWND* phwnd) override;
    IFACEMETHODIMP ContextSensitiveHelp(BOOL fEnterMode) override;

    // IInitializeWithFile メソッド
    IFACEMETHODIMP Initialize(LPCWSTR pszFilePath, DWORD grfMode) override;

    // IObjectWithSite メソッド
    IFACEMETHODIMP SetSite(IUnknown* punkSite) override;
    IFACEMETHODIMP GetSite(REFIID riid, void** ppvSite) override;

private:
    // WebView2 の初期化
    HRESULT CreateWebView();

    // WebView2 の破棄
    void DestroyWebView();

    // EXIF から GPS 情報を抽出
    HRESULT ExtractGPSData(
        LPCWSTR filePath,
        double& latitude,
        double& longitude,
        bool& hasGPS);

    // GPS の度分秒形式を10進数に変換
    double ConvertGPSCoordinate(const PROPVARIANT& propVar);

    // HTML の生成
    std::wstring GenerateHTML(
        const std::wstring& imagePath,
        double lat,
        double lon,
        bool hasGPS);

    // HTML テンプレートの読み込み
    std::wstring LoadHTMLTemplate();

    // ファイルパスの URI エスケープ
    std::wstring UriEscape(const std::wstring& path);

    // プレースホルダーの置換
    void ReplaceAll(
        std::wstring& str,
        const std::wstring& from,
        const std::wstring& to);

    // 一時 HTML ファイルの作成
    std::wstring CreateTempHTMLFile(const std::wstring& htmlContent);

    // テキストファイルの読み込み
    std::wstring ReadTextFile(const std::wstring& filePath);

    // リソースファイルのパスを取得
    std::wstring GetResourcePath(const std::wstring& resourceName);

    // ウィンドウのリサイズ
    void ResizeWebView();

    // メンバー変数
    HWND m_hwndParent = nullptr;        // 親ウィンドウ
    HWND m_hwndPreview = nullptr;       // プレビューウィンドウ
    RECT m_rcParent{};                  // 親ウィンドウの矩形
    std::wstring m_filePath;            // プレビュー対象のファイルパス
    COLORREF m_backgroundColor = RGB(255, 255, 255);

    // WebView2 関連
    wil::com_ptr<ICoreWebView2Controller> m_webviewController;
    wil::com_ptr<ICoreWebView2> m_webview;
    wil::com_ptr<ICoreWebView2Environment> m_webviewEnvironment;

    // COM サイト
    wil::com_ptr<IUnknown> m_site;

    // 初期化フラグ
    bool m_webviewReady = false;
};

// COM クラスファクトリー
CoCreatableClass(PhotoGeoPreviewHandler);
