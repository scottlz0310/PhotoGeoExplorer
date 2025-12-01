#pragma once
#include "resource.h"
#include <atlbase.h>
#include <atlcom.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wincodec.h>
#include <wininet.h>
#include <wil/com.h>
#include <string>
#include <vector>
#include <cmath>

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
    CPhotoGeoPreviewHandler() {}

DECLARE_REGISTRY_RESOURCEID(IDR_PHOTOGEOPREVIEWHANDLER)

BEGIN_COM_MAP(CPhotoGeoPreviewHandler)
    COM_INTERFACE_ENTRY(IPreviewHandler)
    COM_INTERFACE_ENTRY(IInitializeWithFile)
    COM_INTERFACE_ENTRY(IObjectWithSite)
    COM_INTERFACE_ENTRY(IPreviewHandlerVisuals)
END_COM_MAP()

    DECLARE_PROTECT_FINAL_CONSTRUCT()

    HRESULT FinalConstruct() { return S_OK; }
    void FinalRelease();

    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

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
    HRESULT LoadImage();
    HRESULT ExtractGpsData(double* lat, double* lon, bool* hasGps);
    double ConvertGpsCoordinate(const PROPVARIANT& propVar);
    HRESULT DownloadMapTiles();
    HRESULT DownloadTile(int zoom, int x, int y, std::vector<BYTE>& data);
    HBITMAP CreateMapBitmap();
    void LatLonToTile(double lat, double lon, int zoom, int& x, int& y, double& fracX, double& fracY);
    void Paint(HDC hdc);

    HWND m_hwndParent = nullptr;
    RECT m_rcParent = {};
    wil::com_ptr<IUnknown> m_site;
    std::wstring m_filePath;
    HWND m_hwndPreview = nullptr;

    // Photo
    HBITMAP m_hBitmap = nullptr;
    int m_imageWidth = 0;
    int m_imageHeight = 0;

    // GPS & Map
    bool m_hasGps = false;
    double m_latitude = 0;
    double m_longitude = 0;
    HBITMAP m_hMapBitmap = nullptr;
    int m_mapWidth = 0;
    int m_mapHeight = 0;

    COLORREF m_bgColor = RGB(30, 30, 30);

    static const int TILE_SIZE = 256;
    static const int MAP_ZOOM = 15;
};

OBJECT_ENTRY_AUTO(__uuidof(PhotoGeoPreviewHandler), CPhotoGeoPreviewHandler)
