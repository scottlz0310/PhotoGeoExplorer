# 技術スタック計画書 (C++/Win32 + WebView2)

## 基本プラットフォーム
- **Base**: Windows Native (Win32/COM)
- **言語**: C++ (C++17/20)
- **Framework**: ATL (Active Template Library) / WRL (Windows Runtime Library)
- **Target**: Windows 10 / 11 (x64 / ARM64)
- **Host**: Windows Explorer Preview Pane

## UI テクノロジー
- **WebView2 (Microsoft Edge based)**:
  - 単一の WebView2 内で全 UI を実装
  - HTML/CSS/JS による柔軟なレイアウト
- **HTML/CSS/JavaScript**:
  - **Layout**: Flexbox で画像と地図を上下配置
  - **Splitter**: CSS + JS でリサイズ可能なスプリッター実装
  - **Map**: Leaflet.js (CDN)

## ライブラリ & ツール
- **WIC (Windows Imaging Component)**:
  - ネイティブ C++ による高速 EXIF 解析
  - GPS メタデータ抽出
- **WIL (Windows Implementation Libraries)**:
  - COM ポインタ管理、エラーハンドリング、リソース管理
- **Leaflet.js**:
  - 軽量なオープンソース JavaScript マップライブラリ
- **OpenStreetMap**:
  - 地図タイルプロバイダ

## インフラ & 登録
- **COM Preview Handler**: C++/Win32 ATL による実装
- **Registration**: `regsvr32` による標準的な COM 登録
- **Settings**: なし（シンプルさを重視）

## 開発環境
- **IDE**: Visual Studio 2022
- **Workload**: Desktop development with C++
- **Build Tools**: MSBuild
- **Version Control**: Git

## ビルド & デプロイ
- **ビルド方法**:
  - Visual Studio ソリューション (`.sln`)
  - Release / x64 ビルド
- **配布方法**:
  - DLL 単体配布 (+ 登録バッチ)
  - または MSI インストーラー
- **登録方法**:
  - `regsvr32 PhotoGeoPreviewHandler.dll`

## 技術選定の背景

### C++/Win32 (vs C#/.NET)
- **安定性**: Explorer はネイティブアプリであり、ネイティブ DLL が最も安定して動作する
- **トラブル回避**: .NET ランタイムのバージョン不整合や読み込みエラーを回避
- **パフォーマンス**: ネイティブコードによる高速処理

### WebView2 + HTML (vs WPF/XAML)
- **モダン UI**: Web 技術を使ってリッチな UI を簡単に構築可能
- **単一ホスト**: WebView2 一つで UI 完結
- **柔軟性**: HTML/CSS/JS による自由な UI 設計
- **Web 標準**: Leaflet.js などの Web ライブラリを直接利用可能

### WIC (vs MetadataExtractor)
- **ネイティブ API**: Windows 標準の画像処理 API
- **高速**: C++ による直接アクセス
- **依存関係なし**: 外部ライブラリ不要
