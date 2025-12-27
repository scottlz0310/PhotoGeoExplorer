#pragma once
#include "resource.h"
#include <atlbase.h>
#include <atlcom.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <thumbcache.h>
#include <wincodec.h>
#include <WebView2.h>
#include <wil/com.h>
#include <wil/resource.h>
#include <string>
#include <vector>

#include "PhotoGeoPreview_i.h"

using namespace ATL;

class ATL_NO_VTABLE CPhotoGeoPreviewHandler :
    public CComObjectRootEx<CComMultiThreadModel>,
    public CComCoClass<CPhotoGeoPreviewHandler, &CLSID_PhotoGeoPreviewHandler>,
    public IPreviewHandler,
    public IInitializeWithFile,
    public IObjectWithSite,
    public IPreviewHandlerVisuals
{
public:
    CPhotoGeoPreviewHandler()
    {
    }

DECLARE_REGISTRY_RESOURCEID(IDR_PHOTOGEOPREVIEWHANDLER)

BEGIN_COM_MAP(CPhotoGeoPreviewHandler)
    COM_INTERFACE_ENTRY(IPreviewHandler)
    COM_INTERFACE_ENTRY(IInitializeWithFile)
    COM_INTERFACE_ENTRY(IObjectWithSite)
    COM_INTERFACE_ENTRY(IPreviewHandlerVisuals)
END_COM_MAP()

    DECLARE_PROTECT_FINAL_CONSTRUCT()

    HRESULT FinalConstruct()
    {
        return S_OK;
    }

    void FinalRelease()
    {
        DestroyWebView();
    }

public:
    // IPreviewHandler
    STDMETHOD(SetWindow)(HWND hwnd, const RECT* prc);
    STDMETHOD(SetRect)(const RECT* prc);
    STDMETHOD(DoPreview)();
    STDMETHOD(Unload)();
    STDMETHOD(SetFocus)();
    STDMETHOD(QueryFocus)(HWND* phwnd);
    STDMETHOD(TranslateAccelerator)(MSG* pmsg);

    // IInitializeWithFile
    STDMETHOD(Initialize)(LPCWSTR pszFilePath, DWORD grfMode);

    // IObjectWithSite
    STDMETHOD(SetSite)(IUnknown* pUnkSite);
    STDMETHOD(GetSite)(REFIID riid, void** ppvSite);

    // IPreviewHandlerVisuals
    STDMETHOD(SetBackgroundColor)(COLORREF color);
    STDMETHOD(SetFont)(const LOGFONTW* plf);
    STDMETHOD(SetTextColor)(COLORREF color);

private:
    HWND m_hwndParent = nullptr;
    RECT m_rcParent = {};
    wil::com_ptr<IUnknown> m_site;
    std::wstring m_filePath;

    // WebView2
    wil::com_ptr<ICoreWebView2Environment> m_webViewEnvironment;
    wil::com_ptr<ICoreWebView2Controller> m_webViewController;
    wil::com_ptr<ICoreWebView2> m_webView;
    HWND m_hwndPreview = nullptr;
    EventRegistrationToken m_navigationCompletedToken = {};
    HANDLE m_webViewReadyEvent = nullptr;

    HRESULT CreateWebView();
    void DestroyWebView();
    HRESULT NavigateToHtml();
    std::wstring GenerateHtmlContent();
    HRESULT ExtractGpsData(double* lat, double* lon, bool* hasGps);

    // Helper
    double ConvertGpsCoordinate(const PROPVARIANT& propVar);
    std::wstring LoadHtmlTemplate();
    void ReplaceAll(std::wstring& str, const std::wstring& from, const std::wstring& to);
};

OBJECT_ENTRY_AUTO(__uuidof(PhotoGeoPreviewHandler), CPhotoGeoPreviewHandler)
