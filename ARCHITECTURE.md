# アーキテクチャ設計書 (C++/Win32 + WebView2)

## 概要
**C++/Win32 (ATL) + WebView2 + HTML** で `PhotoGeoPreview` を実装する。
Windows Explorer の Preview Handler として単独で動作し、単一の WebView2 内で全 UI を完結させる。

## プロジェクト構造

```
PhotoGeoPreview/
├── PhotoGeoPreview.sln          # Visual Studio ソリューション
├── PhotoGeoPreview/             # メインプロジェクト
│   ├── PhotoGeoPreview.vcxproj
│   ├── PhotoGeoPreviewHandler.h # IPreviewHandler 実装
│   ├── PhotoGeoPreviewHandler.cpp
│   ├── dllmain.cpp              # DLL エントリポイント
│   ├── PhotoGeoPreview.idl      # COM インターフェース定義
│   ├── Resource.h
│   ├── PhotoGeoPreview.rc
│   └── Resources/
│       ├── template.html        # HTML テンプレート
│       ├── styles.css
│       ├── map.js               # Leaflet 初期化
│       └── leaflet/             # オフライン fallback
│           ├── leaflet.js
│           └── leaflet.css
└── installer/                   # (Optional) インストーラー
    └── register.bat             # 登録用スクリプト
```

## コンポーネント構成

### 1. COM Preview Handler (C++/Win32 ATL)

**PhotoGeoPreviewHandler.h/.cpp**:
```cpp
class ATL_NO_VTABLE CPhotoGeoPreviewHandler :
    public CComObjectRootEx<CComMultiThreadModel>,
    public CComCoClass<CPhotoGeoPreviewHandler, &CLSID_PhotoGeoPreviewHandler>,
    public IPreviewHandler,
    public IInitializeWithFile,
    public IObjectWithSite,
    public IPreviewHandlerVisuals
{
private:
    HWND m_hwndParent;
    HWND m_hwndPreview;
    wil::com_ptr<ICoreWebView2Controller> m_webviewController;
    wil::com_ptr<ICoreWebView2> m_webview;

public:
    // IPreviewHandler
    STDMETHOD(SetWindow)(HWND hwnd, const RECT* prc);
    STDMETHOD(DoPreview)();

    // IInitializeWithFile
    STDMETHOD(Initialize)(LPCWSTR pszFilePath, DWORD grfMode);

    // EXIF 抽出 + HTML 生成 + WebView2 表示
};
```

### 2. UI Layer (HTML/CSS/JS)

**単一の WebView2** 内で全 UI を実装：

#### HTML 構造 (`template.html`)
```html
<!DOCTYPE html>
<html>
<head>
  <link rel="stylesheet" href="styles.css">
  <link rel="stylesheet" href="https://unpkg.com/leaflet/dist/leaflet.css">
</head>
<body>
  <div class="split-container">
    <!-- 画像ペイン -->
    <div class="image-pane" id="imagePane">
      <img id="photo" src="{IMAGE_PATH}">
    </div>

    <!-- リサイズ可能なスプリッター -->
    <div class="splitter" id="splitter"></div>

    <!-- 地図ペイン -->
    <div class="map-pane" id="mapPane"></div>
  </div>

  <script src="https://unpkg.com/leaflet/dist/leaflet.js"></script>
  <script>
    // 地図初期化
    const map = L.map('mapPane').setView([{LAT}, {LON}], 13);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(map);
    L.marker([{LAT}, {LON}]).addTo(map);

    // スプリッターのドラッグ処理
    const splitter = document.getElementById('splitter');
    let isDragging = false;

    splitter.addEventListener('mousedown', () => isDragging = true);
    document.addEventListener('mouseup', () => isDragging = false);
    document.addEventListener('mousemove', (e) => {
      if (isDragging) {
        const container = document.querySelector('.split-container');
        const imagePane = document.getElementById('imagePane');
        const percentage = (e.clientY / container.clientHeight) * 100;
        imagePane.style.height = `${percentage}%`;
      }
    });
  </script>
</body>
</html>
```

#### CSS (`styles.css`)
```css
.split-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.image-pane {
  flex: 1;
  overflow: hidden;
  display: flex;
  justify-content: center;
  align-items: center;
}

.splitter {
  height: 5px;
  background: #ccc;
  cursor: ns-resize;
}

.map-pane {
  flex: 1;
}
```

### 3. Logic Layer (C++)

#### EXIF 抽出 (WIC)
```cpp
HRESULT ExtractGPSFromImage(LPCWSTR filePath, double& lat, double& lon)
{
    IWICImagingFactory* factory;
    CoCreateInstance(CLSID_WICImagingFactory, ...);

    IWICBitmapDecoder* decoder;
    factory->CreateDecoderFromFilename(filePath, ...);

    IWICMetadataQueryReader* reader;
    decoder->GetFrame(0)->GetMetadataQueryReader(&reader);

    // GPS 座標を読み取り
    PROPVARIANT value;
    reader->GetMetadataByName(L"/app1/ifd/gps/{ushort=2}", &value);
    // 緯度経度に変換
}
```

#### HTML 生成と一時ファイル
```cpp
std::wstring GenerateHTML(const std::wstring& imagePath, double lat, double lon)
{
    std::wstring html = LoadTemplate(L"Resources/template.html");

    // URI エスケープ（日本語・空白対応）
    std::wstring escapedPath = UriEscape(imagePath);

    ReplaceAll(html, L"{IMAGE_PATH}", escapedPath);
    ReplaceAll(html, L"{LAT}", std::to_wstring(lat));
    ReplaceAll(html, L"{LON}", std::to_wstring(lon));

    // 一時ファイルに書き出し（PowerToys 標準パターン）
    std::wstring tempPath = GetTempHtmlPath();
    WriteFile(tempPath, html);

    return tempPath;
}

// WebView2 で読み込み
std::wstring tempHtmlPath = GenerateHTML(imagePath, lat, lon);
m_webview->Navigate(L"file:///" + tempHtmlPath);
```

## データフロー
1. ユーザーが Explorer で画像を選択
2. PowerToys Runner が `PhotoGeoPreviewHandler` を起動
3. `IInitializeWithFile::Initialize()` でファイルパスを受け取る
4. `DoPreview()` で：
   - WIC で EXIF GPS を抽出
   - GPS データの有無を確認
   - HTML テンプレートに画像パス（URI エスケープ済み）・座標を埋め込み
   - 一時ファイルに HTML を書き出し
   - WebView2 を初期化し `Navigate(file:///temp.html)` で表示
5. HTML 内の JS で：
   - Leaflet 地図を初期化（CDN → ローカル fallback）
   - スプリッターを有効化

## 技術的利点
- ✅ PowerToys の標準構造（C++/WinRT）に完全準拠
- ✅ WebView2 単一で UI 完結（XAML 不要）
- ✅ HTML/CSS/JS による柔軟な UI 実装
- ✅ WIC による高速 EXIF 抽出
- ✅ 既存 Preview Handler と同じ管理方法
