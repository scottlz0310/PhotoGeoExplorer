# 実装計画書（Standalone C++/Win32 Implementation）

## 目的
**C++/Win32 (ATL) + WebView2 + HTML** で `PhotoGeoPreview` Preview Handler を実装する。
PowerToys などの外部ツールに依存せず、単独の DLL として動作させる。

## 技術スタック
- **言語**: C++ (C++17/20)
- **Framework**: ATL (Active Template Library)
- **UI**: WebView2 + HTML/CSS/JavaScript
- **EXIF**: WIC (Windows Imaging Component)
- **地図**: Leaflet.js (CDN)
- **プロジェクト**: Visual Studio C++ DLL プロジェクト

## アプローチ
1. **ATL DLL プロジェクト** を作成し、COM サーバーとして構成
2. **IPreviewHandler** インターフェースを実装
3. **WebView2** をホストし、HTML を表示
4. **WIC** で画像から GPS 情報を抽出
5. **regsvr32** で Explorer に登録

## スコープ（Phase 1 - MVP）

### 1. プロジェクトセットアップ
- [ ] Visual Studio で ATL プロジェクトを作成 (`PhotoGeoPreview`)
- [ ] NuGet パッケージ追加:
  - `Microsoft.Web.WebView2`
  - `Microsoft.Windows.ImplementationLibrary` (WIL)

### 2. COM Preview Handler 実装 (C++)
- [ ] `PhotoGeoPreviewHandler` クラス作成
- [ ] インターフェース実装:
  - `IPreviewHandler`
  - `IInitializeWithFile`
  - `IObjectWithSite`
  - `IPreviewHandlerVisuals`
- [ ] `SetWindow` で WebView2 コントローラを作成

### 3. UI 実装（HTML/CSS/JS）
**HTML テンプレート** (`template.html`):
```html
<div class="container">
  <div class="image-pane" id="imagePane">
    <img id="photo" src="{IMAGE_PATH}" />
  </div>
  <div class="splitter" id="splitter"></div>
  <div class="map-pane" id="mapPane"></div>
</div>
<script src="https://unpkg.com/leaflet/dist/leaflet.js"></script>
<script>
  const map = L.map('mapPane').setView([{LAT}, {LON}], 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(map);
  L.marker([{LAT}, {LON}]).addTo(map);
</script>
```

**CSS**: Flexbox + resizable splitter
**JS**: マウスドラッグで上下比率調整

### 4. ロジック実装（C++）
- [ ] **WIC で EXIF GPS 抽出**:
  ```cpp
  IWICImagingFactory* factory;
  IWICBitmapDecoder* decoder;
  IWICMetadataQueryReader* reader;
  // GPS 座標を取得
  ```
- [ ] **HTML 生成**:
  - テンプレートに画像パス、緯度経度を埋め込み
  - WebView2 に `NavigateToString()` または一時ファイル経由で渡す

### 5. ビルド & 登録
- [ ] Release / x64 ビルド
- [ ] `regsvr32 PhotoGeoPreviewHandler.dll` で登録
- [ ] レジストリ設定（拡張子との関連付け）
  - `.jpg`, `.jpeg`, `.png` などの `PreviewHandler` 値を設定

## 非スコープ
- PowerToys 統合
- C#/.NET 相互運用
- 32bit (x86) サポート（Explorer は 64bit が主流のため）

## 成功基準
- `regsvr32` で正常に登録できる
- Explorer で画像選択時、HTML 内に画像と地図が表示される
- スライダーで上下比率を調整可能
- メモリリークやクラッシュがない

## 技術的利点
- ✅ **完全なスタンドアロン**: 依存関係トラブルからの解放
- ✅ **WebView2**: 最新の Web 技術を利用可能
- ✅ **ネイティブパフォーマンス**: C++ による高速動作

// ✅ 推奨（PowerToys の標準パターン）
std::wstring tempPath = GetTempHtmlPath();
WriteFile(tempPath, htmlContent);
webview->Navigate(L"file:///" + tempPath);
```

**理由**:
- `NavigateToString()` は大きな HTML で遅い
- Leaflet のような外部ライブラリと CSP の相性が悪い
- ローカルファイルの方がパフォーマンスと安定性が高い

#### 2. Leaflet の CDN とオフライン対応
**推奨**: CDN + ローカル fallback の 2 段構成
```html
<script src="https://unpkg.com/leaflet/dist/leaflet.js"
        onerror="this.onerror=null; this.src='resources/leaflet.js'">
</script>
```

**理由**:
- オフライン環境や社内ネットワークでも動作
- PowerToys ユーザーの多様な環境に対応

#### 3. ファイルパスの URI エスケープ
**必須**: 日本語・空白を含むパスは URI エンコード
```cpp
// ❌ 危険（日本語/空白でエラー）
html.replace("{IMAGE_PATH}", L"C:\\Users\\太郎\\Pictures\\旅行 2024\\IMG.jpg");

// ✅ 安全（PowerToys のユーティリティ関数を使用）
std::wstring escapedPath = UriEscape(imagePath);
html.replace("{IMAGE_PATH}", escapedPath);
```

### 🟦 中程度の重要度

#### 4. JavaScript の分離
**推奨**: HTML 内に直書きせず、外部ファイル化
```
PhotoGeoPreview/
├── Resources/
│   ├── template.html
│   ├── styles.css
│   ├── map.js          # Leaflet 初期化
│   └── splitter.js     # スプリッター処理
```

**理由**: PowerToys の既存 Add-on もこの方式でメンテナンス性が高い

#### 5. EXIF なし画像のフォールバック
**推奨**: GPS データがない場合の UI 対応
```cpp
if (!hasGPS) {
    // オプション1: 地図を非表示
    html.replace("{MAP_DISPLAY}", "display:none");

    // オプション2: 「位置情報なし」メッセージ
    html.replace("{MAP_CONTENT}", "<p>位置情報がありません</p>");
}
```

### 🟩 軽微な改善点

#### 6. HEIC サポートの前提条件
**メモ**: HEIC は Windows の HEIF 拡張が必要
- PowerToys の既存 Image Preview と同じ前提
- README に記載推奨
