# PhotoGeoPreview

PowerToys をフォークして実装する、C++/WinRT + WebView2 ベースの画像ジオタグ表示用 Preview Handler です。

## 概要

このプロジェクトは **PowerToys のフォーク** として、C++ で実装されます。
- **技術**: C++/WinRT + WebView2 + HTML/CSS/JS
- **UI**: 単一の WebView2 内で画像と地図を表示
- **スプリッター**: HTML/CSS/JS による可変レイアウト

## 特徴
- ✅ PowerToys の標準構造（C++）に完全準拠
- ✅ WebView2 単一で UI 完結
- ✅ HTML/CSS/JS による柔軟な UI
- ✅ WIC による高速 EXIF 抽出

## 動作環境
- Windows 10 / 11 (x64 / ARM64)
- PowerToys (フォーク版)
- WebView2 Runtime
- **HEIC サポート**: Windows HEIF Image Extensions（Microsoft Store から入手）

## 現在の状態

このリポジトリには以下が含まれています：
- 📋 **ドキュメント**: 完全な実装計画とアーキテクチャドキュメント
- 📝 **コードテンプレート**: `templates/` ディレクトリにすぐに使えるC++ソースコードテンプレート
- 🚀 **セットアップガイド**: PowerToysフォークでの実装手順の詳細説明

**注意**: 実際の実装は別のPowerToysフォークリポジトリで行います。このリポジトリはドキュメントとコードテンプレートのリファレンスとして機能します。

## はじめに

### ステップ 1: セットアップガイドを読む

👉 **[SETUP_GUIDE.md](SETUP_GUIDE.md)** - PowerToys をフォークして PhotoGeoPreview を実装するための完全ガイド

### ステップ 2: ドキュメントを確認

- [ARCHITECTURE.md](ARCHITECTURE.md) - システムアーキテクチャとコンポーネント設計
- [ImplementationPlan.md](ImplementationPlan.md) - 詳細な実装計画
- [TASKS.md](TASKS.md) - タスク分解とチェックリスト
- [TECHSTACK.md](TECHSTACK.md) - 技術スタック詳細

### ステップ 3: コードテンプレートを使用

`templates/` ディレクトリにすぐに使えるコードが含まれています：
- `PhotoGeoPreviewHandler.h` - メインハンドラーヘッダー
- `PhotoGeoPreviewHandler.cpp` - メインハンドラー実装
- `Resources/template.html` - Leaflet 地図を含む HTML テンプレート
- `module.def` - COM エクスポート定義
- `pch.h` / `pch.cpp` - プリコンパイル済みヘッダー
- `preview_handler_registration.json` - 登録設定

## PowerToys フォークのクイックスタート

```bash
# 1. PowerToys をフォーク & クローン
git clone https://github.com/YOUR_USERNAME/PowerToys.git
cd PowerToys

# 2. PhotoGeoPreview ディレクトリを作成
mkdir src/modules/previewpane/PhotoGeoPreview
mkdir src/modules/previewpane/PhotoGeoPreview/Resources

# 3. このリポジトリからテンプレートをコピー
# (templates/ ディレクトリのファイルを PowerToys フォークにコピー)

# 4. PowerToys をビルド
.\build\build.cmd -Configuration Debug -Platform x64
```

詳細な手順は [SETUP_GUIDE.md](SETUP_GUIDE.md) を参照してください。

## 技術詳細

### UI 構造（HTML）
```html
<div class="split-container">
  <div class="image-pane">
    <img src="{IMAGE_PATH}">
  </div>
  <div class="splitter"></div>
  <div class="map-pane"></div>
</div>
```

### EXIF 抽出（C++ + WIC）
```cpp
IWICImagingFactory* factory;
IWICBitmapDecoder* decoder;
IWICMetadataQueryReader* reader;
// GPS 座標を取得
```

## 参考実装
- `src/modules/previewpane/MarkdownPreviewHandler/` (C++)
- `src/modules/previewpane/SvgPreviewHandler/` (C++)

## ライセンス

PowerToys のライセンス（MIT License）に準拠します。

## お問い合わせ

[GitHub Issues](https://github.com/scottlz0310/PhotoGeoPreviewPane/issues)
