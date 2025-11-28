// pch.h: プリコンパイル済みヘッダーファイル
// PhotoGeoPreview Preview Handler

#ifndef PCH_H
#define PCH_H

// Windows ヘッダーファイル
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <restrictederrorinfo.h>
#include <hstring.h>

// WRL (Windows Runtime Library)
#include <wrl.h>
#include <wrl/module.h>

// WIL (Windows Implementation Library)
#include <wil/com.h>
#include <wil/resource.h>
#include <wil/result.h>

// COM インターフェイス
#include <Shobjidl.h>
#include <Shlwapi.h>
#include <Shlobj.h>

// WebView2
#include <WebView2.h>
#include <WebView2EnvironmentOptions.h>

// WIC (Windows Imaging Component)
#include <wincodec.h>
#include <propvarutil.h>

// C++ 標準ライブラリ
#include <string>
#include <vector>
#include <memory>
#include <sstream>
#include <fstream>
#include <filesystem>
#include <algorithm>

// リンカー指定
#pragma comment(lib, "Shlwapi.lib")
#pragma comment(lib, "WindowsCodecs.lib")
#pragma comment(lib, "Propsys.lib")
#pragma comment(lib, "WebView2LoaderStatic.lib")

#endif // PCH_H
