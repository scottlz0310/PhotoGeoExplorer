# タスクリスト（Standalone C++/Win32 Implementation）

フェーズ 0 — 設計と計画
- [x] `ARCHITECTURE.md` の更新（C++/Win32 + WebView2 構造）
- [x] `TECHSTACK.md` の更新（C++ 技術スタック）
- [x] `ImplementationPlan.md` の更新（C++ 実装計画）

フェーズ 1 — プロジェクトセットアップ
1. Visual Studio プロジェクト作成
   - [ ] `PhotoGeoPreview` ソリューション作成
   - [ ] ATL DLL プロジェクト作成
   - [ ] NuGet パッケージ追加 (`Microsoft.Web.WebView2`, `Microsoft.Windows.ImplementationLibrary`)
   - [ ] ビルド構成設定 (Release/x64)

フェーズ 2 — COM ハンドラ実装（C++）
1. COM インフラストラクチャ
   - [ ] `PhotoGeoPreview.idl` 定義
   - [ ] `dllmain.cpp` 実装
   - [ ] `PhotoGeoPreviewHandler` クラス作成
2. インターフェイス実装
   - [ ] `IPreviewHandler` 実装
   - [ ] `IInitializeWithFile` 実装
   - [ ] `IObjectWithSite` 実装
   - [ ] `IPreviewHandlerVisuals` 実装
3. WebView2 統合
   - [ ] `CreateCoreWebView2EnvironmentWithOptions` 呼び出し
   - [ ] `ICoreWebView2Controller` 設定 (親ウィンドウ設定)
   - [ ] `ICoreWebView2` 取得

フェーズ 3 — UI & ロジック実装
1. UI 実装（HTML/CSS/JS）
   - [ ] `template.html` 作成：
     - 画像ペイン（`<img>`）
     - スプリッター（`<div>`）
     - 地図ペイン（Leaflet）
   - [ ] `styles.css` 作成（Flexbox レイアウト）
   - [ ] スプリッターのドラッグ処理（JavaScript）
2. ロジック実装（C++）
   - [ ] WIC で EXIF GPS 抽出
   - [ ] HTML テンプレート生成（プレースホルダー置換）
   - [ ] WebView2 に `NavigateToString` またはファイル経由で表示

フェーズ 4 — ビルド & 検証
1. ビルド
   - [ ] Release / x64 ビルド成功確認
2. 登録 & テスト
   - [ ] `regsvr32` による登録バッチ作成
   - [ ] レジストリ登録確認
   - [ ] Explorer での動作確認（JPEG / PNG / HEIC）
   - [ ] スプリッターのドラッグ動作確認

フェーズ 5 — 配布準備
- [ ] 配布用パッケージ作成（DLL + リソース + 登録スクリプト）
- [ ] README 更新（ビルド・インストール手順）

備考:
- スタンドアロン C++ DLL として実装
- WebView2 + HTML で UI 完結
- WIC による EXIF 抽出
