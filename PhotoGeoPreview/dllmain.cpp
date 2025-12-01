#include "pch.h"
#include "resource.h"
#include "PhotoGeoPreviewHandler.h"
#include "PhotoGeoPreview_i.h"
#include "PhotoGeoPreview_i.c"

class CPhotoGeoPreviewModule : public ATL::CAtlDllModuleT< CPhotoGeoPreviewModule >
{
public :
    DECLARE_LIBID(LIBID_PhotoGeoPreviewLib)
    DECLARE_REGISTRY_APPID_RESOURCEID(IDR_PHOTOGEOPREVIEW, "{D8196810-5366-4F63-9CB6-6A3F3E2C8F1A}")
};

CPhotoGeoPreviewModule _AtlModule;

extern "C" BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, LPVOID lpReserved)
{
    return _AtlModule.DllMain(dwReason, lpReserved);
}

_Use_decl_annotations_
STDAPI DllCanUnloadNow(void)
{
    return _AtlModule.DllCanUnloadNow();
}

_Use_decl_annotations_
STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv)
{
    return _AtlModule.DllGetClassObject(rclsid, riid, ppv);
}

_Use_decl_annotations_
STDAPI DllRegisterServer(void)
{
    return _AtlModule.DllRegisterServer();
}

_Use_decl_annotations_
STDAPI DllUnregisterServer(void)
{
    return _AtlModule.DllUnregisterServer();
}

STDAPI DllInstall(BOOL bInstall, _In_opt_ LPCWSTR pszCmdLine)
{
    HRESULT hr = E_FAIL;
    static const wchar_t szUserSwitch[] = L"user";

    if (pszCmdLine != nullptr)
    {
        if (_wcsnicmp(pszCmdLine, szUserSwitch, _countof(szUserSwitch)) == 0)
        {
            ATL::AtlSetPerUserRegistration(true);
        }
    }

    if (bInstall)
    {
        hr = DllRegisterServer();
        if (FAILED(hr))
        {
            DllUnregisterServer();
        }
    }
    else
    {
        hr = DllUnregisterServer();
    }

    return hr;
}
