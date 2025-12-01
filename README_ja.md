# PhotoGeoPreview

C++/Win32 + WebView2 ベースのスタンドアロン画像ジオタグ表示用 Preview Handler です。
Windows Explorer のプレビューウィンドウに、写真とその撮影場所（地図）を表示します。

## 概要

このプロジェクトは **PowerToys などの外部ツールに依存しない**、単独で動作する軽量な Preview Handler です。
- **技術**: C++/Win32 (ATL/WRL) + WebView2 + HTML/CSS/JS
- **UI**: 単一の WebView2 内で画像と地図を表示
- **スプリッター**: HTML/CSS/JS による可変レイアウト
- **依存性**: 最小限（WebView2 Runtime のみ）

## 特徴
- ✅ **スタンドアロン動作**: PowerToys や他の巨大なフレームワークは不要
- ✅ **軽量**: ネイティブ C++ DLL による高速動作
- ✅ **WebView2**: 最新の Web 技術で UI を構築
- ✅ **WIC**: Windows 標準機能による高速な EXIF 解析

## 動作環境
- Windows 10 / 11 (x64 / ARM64)
- WebView2 Runtime (Windows 11 には標準搭載)
- **HEIC サポート**: Windows HEIF Image Extensions（Microsoft Store から入手）

## 現在の状態

このリポジトリには以下が含まれています：
- 📋 **ドキュメント**: 実装計画とアーキテクチャ設計
- 📝 **ソースコード**: C++ による Preview Handler の実装（進行中）
- 🚀 **ビルドガイド**: Visual Studio を使用したビルド手順

## はじめに

### ステップ 1: 必須コンポーネントの確認

- Visual Studio 2022 (C++ デスクトップ開発ワークロード)
- Windows SDK

### ステップ 2: ビルド

1. `PhotoGeoPreview.sln` を Visual Studio で開く
2. ソリューション構成を `Release` / `x64` に設定
3. ソリューションのビルドを実行

### ステップ 3: インストール（登録）

管理者権限でコマンドプロンプトを開き、ビルドされた DLL を登録します。

```cmd
regsvr32.exe PhotoGeoPreviewHandler.dll
```

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

## ライセンス

PowerToys のライセンス（MIT License）に準拠します。

## お問い合わせ

[GitHub Issues](https://github.com/scottlz0310/PhotoGeoPreviewPane/issues)
