# PowerToys フォーク & PhotoGeoPreview 実装ガイド

このガイドでは、PowerToys を完全にフォークして PhotoGeoPreview を統合実装する手順を説明します。

## 前提条件

### 必要なツール
- **Git**: バージョン管理
- **Visual Studio 2022** (推奨は 17.8 以降):
  - C++ によるデスクトップ開発
  - Windows SDK (10.0.22621.0 以降)
  - C++/WinRT テンプレート
- **CMake**: 3.27 以降
- **.NET SDK**: 8.0 以降
- **WiX Toolset**: v3.14 以降 (インストーラー作成用)

### システム要件
- Windows 10 version 2004 (ビルド 19041) 以降
- メモリ: 最低 8GB (推奨 16GB)
- ディスク: 約 10GB の空き容量

---

## Phase 1: PowerToys のフォーク & セットアップ

### Step 1.1: GitHub でフォークを作成

1. ブラウザで [PowerToys 公式リポジトリ](https://github.com/microsoft/PowerToys) にアクセス
2. 右上の **Fork** ボタンをクリック
3. フォーク先のアカウントを選択
4. フォーク名は `PowerToys` のままでOK（または `PowerToys-PhotoGeoPreview`）
5. **Create fork** をクリック

### Step 1.2: ローカルにクローン

```bash
# フォークしたリポジトリをクローン（自分のユーザー名に置き換え）
git clone https://github.com/YOUR_USERNAME/PowerToys.git
cd PowerToys

# 上流（本家）リポジトリをリモートに追加
git remote add upstream https://github.com/microsoft/PowerToys.git

# 確認
git remote -v
```

### Step 1.3: 依存関係のインストール

PowerToys のルートディレクトリで以下を実行：

```bash
# NuGet パッケージの復元とビルドの準備
.\build\prerequisites.ps1
```

### Step 1.4: 初回ビルドの確認

フォークが正常に動作するか確認：

```bash
# デバッグビルド（x64）
.\build\build.cmd -Configuration Debug -Platform x64

# または Visual Studio で開く
.\PowerToys.sln
```

ビルドが成功したら、`src/x64/Debug/PowerToys.exe` が生成されます。

**重要**: 初回ビルドは 10〜30 分かかる場合があります。

---

## Phase 2: 既存 Preview Handler の調査

PhotoGeoPreview を実装する前に、既存の Preview Handler の構造を理解します。

### Step 2.1: 参考にする Preview Handler

PowerToys には以下の Preview Handler があります：

1. **MarkdownPreviewHandler** (C++/WinRT + WebView2)
   - パス: `src/modules/previewpane/MarkdownPreviewHandler/`
   - HTML ベースのプレビュー
   - WebView2 を使用

2. **SvgPreviewHandler** (C++/WinRT + WebView2)
   - パス: `src/modules/previewpane/SvgPreviewHandler/`
   - SVG を HTML で表示
   - WebView2 を使用

3. **MonacoPreviewHandler** (C++/WinRT + WebView2)
   - パス: `src/modules/previewpane/MonacoPreviewHandler/`
   - コードエディタ (Monaco Editor)
   - WebView2 + 外部ライブラリ

### Step 2.2: ディレクトリ構造の確認

```bash
# Preview Handler のディレクトリに移動
cd src/modules/previewpane/

# 構造を確認
dir

# MarkdownPreviewHandler の内容を確認
cd MarkdownPreviewHandler
dir
```

典型的な構造：
```
MarkdownPreviewHandler/
├── MarkdownPreviewHandler.vcxproj
├── MarkdownPreviewHandler.h
├── MarkdownPreviewHandler.cpp
├── module.def
├── Resources/
│   ├── index.html
│   ├── markdown.css
│   └── (その他のリソース)
└── (その他のヘルパーファイル)
```

### Step 2.3: 重要なファイルの確認

以下のファイルを読んで、実装パターンを理解します：

```cpp
// MarkdownPreviewHandler.h
class MarkdownPreviewHandler :
    public IPreviewHandler,
    public IInitializeWithFile,
    public IPreviewHandlerVisuals,
    public IOleWindow
{
    // WebView2 のホスト処理
    // ファイルの読み込み
    // HTML の生成と表示
};
```

**重要なメソッド**:
- `SetWindow()`: プレビューウィンドウの作成
- `DoPreview()`: プレビューの実行
- `Initialize()`: ファイルパスの取得

---

## Phase 3: PhotoGeoPreview の実装

### Step 3.1: ディレクトリとファイルの作成

```bash
# Preview Handler のディレクトリに移動
cd src/modules/previewpane/

# PhotoGeoPreview ディレクトリを作成
mkdir PhotoGeoPreview
cd PhotoGeoPreview

# 基本ディレクトリ構造を作成
mkdir Resources
mkdir Resources\leaflet
```

### Step 3.2: プロジェクトファイル (.vcxproj) の作成

`MarkdownPreviewHandler.vcxproj` をテンプレートとしてコピー：

```bash
# MarkdownPreviewHandler をテンプレートとしてコピー
cp ..\MarkdownPreviewHandler\MarkdownPreviewHandler.vcxproj PhotoGeoPreview.vcxproj
```

`PhotoGeoPreview.vcxproj` を編集して、以下を置換：
- `MarkdownPreviewHandler` → `PhotoGeoPreview`
- GUID を新しいものに変更（Visual Studio で自動生成可能）

### Step 3.3: COM エクスポート定義 (module.def) の作成

`module.def`:
```def
LIBRARY "PhotoGeoPreview"

EXPORTS
    DllCanUnloadNow     PRIVATE
    DllGetClassObject   PRIVATE
    DllRegisterServer   PRIVATE
    DllUnregisterServer PRIVATE
```

### Step 3.4: ハンドラーのヘッダーファイル (PhotoGeoPreviewHandler.h) の作成

`MarkdownPreviewHandler.h` をテンプレートとしてコピーし、以下を実装：

```cpp
#pragma once
#include <Windows.h>
#include <wrl/module.h>
#include <wil/com.h>
#include <Shlwapi.h>
#include <Shlobj.h>
#include <string>

// WebView2
#include <WebView2.h>

// Preview Handler インターフェイス
#include <Shobjidl.h>

class PhotoGeoPreviewHandler :
    public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        IPreviewHandler,
        IPreviewHandlerVisuals,
        IOleWindow,
        IInitializeWithFile,
        IObjectWithSite>
{
public:
    PhotoGeoPreviewHandler();
    virtual ~PhotoGeoPreviewHandler();

    // IPreviewHandler
    IFACEMETHODIMP SetWindow(HWND hwnd, const RECT* prc) override;
    IFACEMETHODIMP SetRect(const RECT* prc) override;
    IFACEMETHODIMP DoPreview() override;
    IFACEMETHODIMP Unload() override;
    IFACEMETHODIMP SetFocus() override;
    IFACEMETHODIMP QueryFocus(HWND* phwnd) override;
    IFACEMETHODIMP TranslateAccelerator(MSG* pmsg) override;

    // IPreviewHandlerVisuals
    IFACEMETHODIMP SetBackgroundColor(COLORREF color) override;
    IFACEMETHODIMP SetFont(const LOGFONTW* plf) override;
    IFACEMETHODIMP SetTextColor(COLORREF color) override;

    // IOleWindow
    IFACEMETHODIMP GetWindow(HWND* phwnd) override;
    IFACEMETHODIMP ContextSensitiveHelp(BOOL fEnterMode) override;

    // IInitializeWithFile
    IFACEMETHODIMP Initialize(LPCWSTR pszFilePath, DWORD grfMode) override;

    // IObjectWithSite
    IFACEMETHODIMP SetSite(IUnknown* punkSite) override;
    IFACEMETHODIMP GetSite(REFIID riid, void** ppvSite) override;

private:
    // WebView2 の初期化
    HRESULT CreateWebView();

    // EXIF からGPS情報を抽出
    HRESULT ExtractGPSData(LPCWSTR filePath, double& latitude, double& longitude);

    // HTML の生成
    std::wstring GenerateHTML(const std::wstring& imagePath, double lat, double lon);

    // 一時HTMLファイルの作成
    std::wstring CreateTempHTMLFile(const std::wstring& htmlContent);

    HWND m_hwndParent = nullptr;
    HWND m_hwndPreview = nullptr;
    RECT m_rcParent{};
    std::wstring m_filePath;

    wil::com_ptr<ICoreWebView2Controller> m_webviewController;
    wil::com_ptr<ICoreWebView2> m_webview;
    wil::com_ptr<IUnknown> m_site;
};
```

### Step 3.5: ハンドラーの実装 (PhotoGeoPreviewHandler.cpp) の作成

以下の主要メソッドを実装：

**1. EXIF GPS 抽出 (WIC 使用)**:

```cpp
#include <wincodec.h>
#include <propvarutil.h>

HRESULT PhotoGeoPreviewHandler::ExtractGPSData(
    LPCWSTR filePath,
    double& latitude,
    double& longitude)
{
    wil::com_ptr<IWICImagingFactory> factory;
    RETURN_IF_FAILED(CoCreateInstance(
        CLSID_WICImagingFactory,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&factory)));

    wil::com_ptr<IWICBitmapDecoder> decoder;
    RETURN_IF_FAILED(factory->CreateDecoderFromFilename(
        filePath,
        nullptr,
        GENERIC_READ,
        WICDecodeMetadataCacheOnDemand,
        &decoder));

    wil::com_ptr<IWICBitmapFrameDecode> frame;
    RETURN_IF_FAILED(decoder->GetFrame(0, &frame));

    wil::com_ptr<IWICMetadataQueryReader> metadataReader;
    RETURN_IF_FAILED(frame->GetMetadataQueryReader(&metadataReader));

    // GPS 緯度の取得
    PROPVARIANT propLat;
    PropVariantInit(&propLat);
    if (SUCCEEDED(metadataReader->GetMetadataByName(
        L"/app1/ifd/gps/{ushort=2}", &propLat)))
    {
        // GPS座標の解析（度分秒から10進数へ変換）
        // TODO: 実装
    }
    PropVariantClear(&propLat);

    // GPS 経度の取得
    PROPVARIANT propLon;
    PropVariantInit(&propLon);
    if (SUCCEEDED(metadataReader->GetMetadataByName(
        L"/app1/ifd/gps/{ushort=4}", &propLon)))
    {
        // TODO: 実装
    }
    PropVariantClear(&propLon);

    return S_OK;
}
```

**2. HTML 生成**:

```cpp
std::wstring PhotoGeoPreviewHandler::GenerateHTML(
    const std::wstring& imagePath,
    double lat,
    double lon)
{
    // Resources/template.html を読み込み
    std::wstring templatePath = L"Resources\\template.html";
    std::wstring htmlTemplate = ReadTextFile(templatePath);

    // プレースホルダーを置換
    std::wstring html = htmlTemplate;

    // 画像パスの URI エスケープ
    std::wstring escapedPath = UriEscape(imagePath);
    ReplaceAll(html, L"{IMAGE_PATH}", escapedPath);

    ReplaceAll(html, L"{LAT}", std::to_wstring(lat));
    ReplaceAll(html, L"{LON}", std::to_wstring(lon));

    return html;
}
```

**3. DoPreview() の実装**:

```cpp
IFACEMETHODIMP PhotoGeoPreviewHandler::DoPreview()
{
    if (!m_webview)
    {
        RETURN_IF_FAILED(CreateWebView());
    }

    double lat = 0.0, lon = 0.0;
    HRESULT hr = ExtractGPSData(m_filePath.c_str(), lat, lon);

    if (FAILED(hr))
    {
        // GPS データがない場合のフォールバック
        // TODO: 「位置情報なし」の表示
        return S_OK;
    }

    // HTML 生成
    std::wstring html = GenerateHTML(m_filePath, lat, lon);

    // 一時ファイルに保存
    std::wstring tempHtmlPath = CreateTempHTMLFile(html);

    // WebView2 で表示
    std::wstring uri = L"file:///" + tempHtmlPath;
    RETURN_IF_FAILED(m_webview->Navigate(uri.c_str()));

    return S_OK;
}
```

### Step 3.6: HTML テンプレートの作成

`Resources/template.html`:

```html
<!DOCTYPE html>
<html lang="ja">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Photo Geo Preview</title>
    <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"
          onerror="this.onerror=null; this.href='leaflet/leaflet.css';" />
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }

        body, html {
            width: 100%;
            height: 100%;
            overflow: hidden;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        }

        .split-container {
            display: flex;
            flex-direction: column;
            width: 100%;
            height: 100%;
        }

        .image-pane {
            flex: 1;
            overflow: hidden;
            display: flex;
            justify-content: center;
            align-items: center;
            background: #1e1e1e;
            position: relative;
        }

        .image-pane img {
            max-width: 100%;
            max-height: 100%;
            object-fit: contain;
        }

        .splitter {
            height: 4px;
            background: #007acc;
            cursor: ns-resize;
            position: relative;
            z-index: 10;
        }

        .splitter:hover {
            background: #1a9aff;
        }

        .map-pane {
            flex: 1;
            position: relative;
        }

        #map {
            width: 100%;
            height: 100%;
        }
    </style>
</head>
<body>
    <div class="split-container">
        <div class="image-pane" id="imagePane">
            <img id="photo" src="{IMAGE_PATH}" alt="Photo">
        </div>

        <div class="splitter" id="splitter"></div>

        <div class="map-pane">
            <div id="map"></div>
        </div>
    </div>

    <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"
            onerror="this.onerror=null; this.src='leaflet/leaflet.js';"></script>
    <script>
        // 地図の初期化
        const map = L.map('map').setView([{LAT}, {LON}], 13);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
            maxZoom: 19
        }).addTo(map);

        // マーカーの追加
        L.marker([{LAT}, {LON}]).addTo(map)
            .bindPopup('撮影場所')
            .openPopup();

        // スプリッターのドラッグ処理
        const splitter = document.getElementById('splitter');
        const imagePane = document.getElementById('imagePane');
        const container = document.querySelector('.split-container');
        let isDragging = false;

        splitter.addEventListener('mousedown', (e) => {
            isDragging = true;
            e.preventDefault();
        });

        document.addEventListener('mousemove', (e) => {
            if (!isDragging) return;

            const containerRect = container.getBoundingClientRect();
            const percentage = ((e.clientY - containerRect.top) / containerRect.height) * 100;

            // 最小/最大サイズの制限
            if (percentage > 10 && percentage < 90) {
                imagePane.style.flex = `0 0 ${percentage}%`;
            }
        });

        document.addEventListener('mouseup', () => {
            isDragging = false;
        });

        // マップのリサイズ対応
        window.addEventListener('resize', () => {
            map.invalidateSize();
        });
    </script>
</body>
</html>
```

---

## Phase 4: PowerToys への統合

### Step 4.1: プロジェクトをソリューションに追加

`src/modules/previewpane/previewpane.sln` を Visual Studio で開き：

1. ソリューションエクスプローラーで右クリック
2. **追加** → **既存のプロジェクト**
3. `PhotoGeoPreview/PhotoGeoPreview.vcxproj` を選択

### Step 4.2: preview_handlers.json に登録

`installer/PowerToysSetup/preview_handlers.json` に以下を追加：

```json
{
  "name": "PhotoGeoPreview",
  "clsid": "{あなたのGUID}",
  "extensions": [".jpg", ".jpeg", ".png", ".heic"],
  "description": "Photo with Geolocation Preview Handler"
}
```

**GUID の生成方法**:
- Visual Studio → **ツール** → **GUID の作成**
- または PowerShell: `[guid]::NewGuid()`

### Step 4.3: CMakeLists.txt の更新

`src/modules/previewpane/CMakeLists.txt` に PhotoGeoPreview を追加：

```cmake
add_subdirectory(PhotoGeoPreview)
```

---

## Phase 5: ビルドとテスト

### Step 5.1: ビルド

```bash
# PowerToys のルートディレクトリで
.\build\build.cmd -Configuration Debug -Platform x64
```

または Visual Studio で：
- ソリューション構成: Debug
- プラットフォーム: x64
- **ビルド** → **ソリューションのビルド**

### Step 5.2: 動作確認

1. ビルドが成功したら、PowerToys.exe を起動：
   ```bash
   .\src\x64\Debug\PowerToys.exe
   ```

2. PowerToys の設定画面で：
   - **ファイルエクスプローラーのアドオン** を開く
   - **PhotoGeoPreview** が表示されることを確認
   - 有効にする

3. Windows Explorer でテスト：
   - GPS データ付きの画像ファイルを選択
   - プレビューペインに画像と地図が表示されることを確認
   - スプリッターをドラッグして上下比率を調整

### Step 5.3: デバッグ

問題が発生した場合：

1. Visual Studio でデバッガーをアタッチ：
   - **デバッグ** → **プロセスにアタッチ**
   - `explorer.exe` を選択

2. ログの確認：
   - `%LOCALAPPDATA%\Microsoft\PowerToys\logs\`

---

## Phase 6: 配布準備

### Step 6.1: Release ビルド

```bash
.\build\build.cmd -Configuration Release -Platform x64
```

### Step 6.2: インストーラーの作成

```bash
.\build\installer\build_installer.cmd
```

生成されたインストーラー: `installer\PowerToysSetup\bin\Release\PowerToysSetup-<version>.exe`

---

## トラブルシューティング

### ビルドエラー: "WebView2 SDK が見つかりません"

```bash
# NuGet パッケージの再インストール
nuget restore PowerToys.sln
```

### Preview Handler が登録されない

1. PowerToys を管理者権限で実行
2. 設定で明示的に有効化
3. Explorer を再起動

### GPS データが取得できない

- WIC のメタデータクエリパスを確認：
  - JPEG: `/app1/ifd/gps/{ushort=2}` (緯度)
  - JPEG: `/app1/ifd/gps/{ushort=4}` (経度)

---

## 次のステップ

基本実装が完了したら：

1. **HEIC サポート**: Windows HEIF 拡張との連携
2. **オフライン地図**: Leaflet.js のローカルコピー
3. **パフォーマンス最適化**: 大きな画像の処理
4. **UI 改善**: ダークモード対応、ローディング表示

---

## 参考リソース

- [PowerToys 開発ガイド](https://github.com/microsoft/PowerToys/wiki/Developer-Guide)
- [WebView2 ドキュメント](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [Windows Imaging Component (WIC)](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-lh)
- [Leaflet.js ドキュメント](https://leafletjs.com/)
