# PhoroGeoExplorer for Windows ネイティブ版 設計計画ドキュメント

## 1. プロジェクト概要

**アプリケーション名**：PhoroGeoExplorer
**目的**：Windows 上で写真ファイルに埋め込まれた位置情報（GPS）を抽出・地図上にプロットし、撮影場所を視覚的に把握できるスタンドアロンアプリ。
**ターゲットプラットフォーム**：Windows 10 / Windows 11 のみ（クロスプラットフォームは要件外）
**ターゲットユーザー**：旅行写真の整理、撮影地確認をしたい一般ユーザー・写真愛好家
**バージョン**：1.1.1（リリース準備）

## 2. 技術スタック選定

| 項目                  | 選定技術                                      | 選定理由 |
|-----------------------|-----------------------------------------------|----------|
| 言語                  | C# (.NET 10)                                  | Windows ネイティブ最高のパフォーマンス・ツール統合 |
| UI フレームワーク     | WinUI 3 (Windows App SDK)                     | Windows 11 Fluent Design に完全準拠。BreadcrumbBar・CommandBar・ListView などの Explorer 準拠コントロールが標準搭載 |
| 地図表示              | Microsoft.Web.WebView2 (Chromium ベース)      | 既存の Leaflet.js コードをほぼそのまま流用可能。オフライン対応も容易 |
| 画像処理・サムネイル生成 | Six Labors.ImageSharp                         | 高性能・純マネージド。リサイズ・フォーマット変換・キャッシュ生成に最適 |
| EXIF / GPS メタデータ抽出 | MetadataExtractor                             | 信頼性が高く、JPEG/TIFF/RAW など広範なフォーマット対応。GPS・撮影日時取得が容易 |
| ファイルシステム操作  | Windows.Storage API                           | ネイティブで高速・安全。フォルダ監視・ファイル列挙に最適 |
| 設定管理              | Microsoft.Extensions.Configuration + JSON    | 軽量で十分。必要に応じて Windows Registry も検討 |
| 通知                  | Windows App SDK の AppNotification            | トースト通知・常駐監視と親和性が高い（既存経験あり） |
| パッケージング        | MSI (WiX) / MSIX (試験的)                       | 署名不要での配布と Store 公開の両方を視野に入れる |
| ビルドツール          | Visual Studio 2026                            | .NET 10 / WinUI 3 の公式サポート・デザイナー・デバッグ体験が最高 |

## 3. 開発環境整備

- **IDE**：Visual Studio 2026 (最新アップデート適用)
  - Microsoft Visual Studio Community
  - 2025年12月 機能更新プログラム
  - チャネル:  Stable
  - Version: 18.1.1
- **ワークロード**：
  - .NET Desktop Development
  - Windows App SDK
  - Universal Windows Platform development (オプション)
- **NuGet パッケージ**（プロジェクト作成後に追加）
  - Microsoft.WindowsAppSDK (最新 stable)
  - Microsoft.Web.WebView2
  - SixLabors.ImageSharp
  - MetadataExtractor
- **プロジェクトテンプレート**：Blank App, Packaged (WinUI 3 in Desktop)
- **ターゲットフレームワーク**：net10.0-windows10.0.19041.0（Windows 10/11 両対応）
- **アーキテクチャ**：x64 / ARM64 両ビルド（将来的な Surface 等対応）
- **バージョン管理**：Git（既存リポジトリに新ブランチ `winui-migration` 作成予定）

## 4. プロジェクト構造（予定）

```
PhoroGeoExplorer/
├── PhoroGeoExplorer.csproj
├── App.xaml / App.xaml.cs                // アプリケーション起動・ライフサイクル管理
├── MainWindow.xaml / MainWindow.xaml.cs  // メインウィンドウ（単一ウィンドウ設計）
├── Assets/                               // アイコン・スプラッシュ画面等
├── Services/                             // ビジネスロジック層
│   ├── FileSystemService.cs              // フォルダ監視・ファイル列挙
│   ├── ImageProcessingService.cs         // サムネイル生成・キャッシュ管理
│   ├── ExifService.cs                    // メタデータ抽出（GPS・撮影日時等）
│   └── MapService.cs                     // WebView2 との通信ブリッジ
├── ViewModels/                           // MVVM パターン用 ViewModel
│   ├── MainViewModel.cs
│   ├── FileBrowserViewModel.cs
│   └── MapViewModel.cs
├── Views/                                // 必要に応じてページ分割（今回は単一ウィンドウのため省略可）
├── Models/                               // データモデル（PhotoItem 等）
├── Resources/                            // 文字列・スタイル・ブラシ
├── wwwroot/                              // Leaflet 用静的ファイル（HTML/JS/CSS）
│   ├── index.html
│   ├── leaflet.css
│   └── leaflet.js
└── Properties/
    └── launchSettings.json
```

## 5. UI 設計概要

**全体レイアウト**：単一ウィンドウ・3分割構成（固定分割ではなく Resizable Panels 考慮）

1. **左ペイン（File Browser）** - 約40%幅
   - 上部：CommandBar（戻る/進む/上へ/更新/ホームボタン）
   - パンくずリスト（BreadcrumbBar）
   - 検索ボックス + フィルタボタン
   - 「画像のみ表示」チェックボックス
   - ファイルリスト（ListView - Details ビュー）
     - 列：アイコン / 名前 / 撮影日時 / 解像度 / サイズ
     - ソート：列ヘッダークリックで昇順・降順切り替え
     - アイコン：ImageSharp で生成したサムネイル

2. **右ペイン上部（Image Preview）** - 約60%幅・上半分
   - 選択画像のフルプレビュー
   - ズーム・パン機能（必要に応じて追加）

3. **右ペイン下部（Map）** - 約60%幅・下半分
   - WebView2 で Leaflet 表示
   - 選択画像の GPS 座標をマーカーでプロット
   - 複数選択時は全マーカー表示 + 地図自動調整

**その他 UI 要素**
- メニュー：ファイル / 表示 / 設定 / ヘルプ（MenuBar）
- ステータスバー：選択ファイルの詳細（ファイル名・カメラモデル・ISO・GPS 等）
- ダーク/ライトテーマ対応（システム準拠）

## 6. 主要機能要件（技術的ポイント）

- Explorer からの複数画像ドラッグ＆ドロップ対応
- アプリ内から Explorer への画像ドラッグアウト対応
- フォルダ選択・ナビゲーション（パンくず + ボタン）
- サムネイルキャッシュ（%LocalAppData%\PhoroGeoExplorer\Cache）
- EXIF 解析による撮影日時ソート・GPSプロット
- オフライン完全対応（地図タイルはローカルキャッシュ推奨）

## 7. 開発スケジュール（目安）

- Week 1：プロジェクト作成・基本レイアウト・File Browser 骨格
- Week 2：ファイルリスト実装・D&D・サムネイル生成
- Week 3：EXIF 抽出・Map（WebView2）連携
- Week 4：設定・通知・仕上げ・テスト・MSIX パッケージング

## 8. リスクと対策

| リスク                           | 対策 |
|----------------------------------|------|
| WebView2 の初期化遅延            | 事前初期化 or スプラッシュ画面で隠蔽 |
| 大量画像時のサムネイル生成負荷   | 非同期処理 + キャッシュ徹底 + 仮想化 ListView 使用 |
| EXIF 非対応フォーマットの扱い    | フォールバック（ファイル作成日時使用） |

以上が PhoroGeoExplorer Windows ネイティブ版の設計計画です。
WinUI 3 の強みを最大限活かし、Windows Explorer に近い自然な操作感を実現することを最優先とします。
