# 変更履歴

このプロジェクトの主な変更点をここに記録します。

## [Unreleased]

### 追加
- Shell + Pane アーキテクチャの導入（ISSUE #70）
  - `IPaneViewModel` インターフェースと `PaneViewModelBase` 基底クラス
  - ペイン間共有状態を管理する `WorkspaceState`
  - Pane 作成ガイドライン（`docs/Architecture/PaneSystem.md`）
  - サンプル実装：`SettingsPaneView` / `SettingsPaneViewModel`
  - PRテンプレートにアーキテクチャガードレールを追加
- 新規ディレクトリ構造
  - `/PhotoGeoExplorer/Panes` - 機能単位のUI + ViewModel
  - `/PhotoGeoExplorer/State` - 共有状態管理
- Map Pane の選択判定ロジックを `MapPaneSelectionHelper` として分離。
- Map Pane の選択判定に対するユニットテストを追加（`MapPaneSelectionHelperTests`）。
- `docs/Architecture/MainWindow-Orchestration-Review.md` を追加し、MainWindow の責務移管状況を整理。
- AI エージェント向けガイドラインの正本 `AgentGuidelineSource.md` を追加。
- ガイドライン同期スクリプト `scripts/Sync-AgentDocs.ps1` を追加（`-Check` 対応）。

### 変更
- README.md にアーキテクチャセクションを追加
- ファイルビュー詳細表示の更新日時・解像度・サイズ列に余白を追加し、視認性を改善
- PNG 画像など小さいプレビューでフィットが過剰に拡大される問題を改善（表示サイズに基づいてフィットを計算）
- MainWindow の地図 UI（Flyout/マップ状態表示/矩形選択イベント）を `MapPaneViewControl` に移管し、MainWindow をオーケストレーション中心へ整理。
- `MapPaneView`（ResourceDictionary/DataTemplate）構成を廃止し、`MapPaneViewControl`（UserControl）へ統一。
- Preview の DPI 変更監視（`XamlRoot.Changed`）を MainWindow から `PreviewPaneViewControl` へ移管。
- `App.xaml` の `MapPaneView` 参照を削除し、Map View の構成を一本化。
- `docs/Architecture/PaneSystem.md` を `MapPaneViewControl` ベースの構成に更新。
- `docs/MainWindow-Orchestration-Review.md` を `docs/Architecture/MainWindow-Orchestration-Review.md` に再配置。
- `AGENTS.md` / `CLAUDE.md` / `.github/copilot-instructions.md` を自動生成方式に統一し、固有補足ブロックのみ手編集可能に変更。
- `lefthook.yml` と CI（`.github/workflows/ci.yml`）にガイドライン同期チェックを追加。

### 修正
- Map 初期化時に `MapPaneViewModel.Map` の変更通知が発火せず、地図が表示されない場合がある問題を修正。
- CI 相当のローカル品質ゲート（analyzer/nullability）で発生するビルド失敗を解消。

### テスト
- Map 選択判定のテストを拡充（矩形境界判定、重複除外、閾値判定、引数異常系）。
- ローカル実行で `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes` を通過。

## [1.5.5] - 2026-01-23

### 変更
- MSI インストーラープロジェクト (`PhotoGeoExplorer.Installer`) を削除し、配布形式を Microsoft Store (MSIX) に一本化。
- 開発用スクリプトを `scripts/DevInstall.ps1` に統合・刷新。
  - `install.ps1`, `uninstall.ps1` を廃止。
  - `-Clean` オプションでアンインストールと証明書削除を一括実行可能に。
- WACK テストスクリプト (`scripts/RunWackTests.ps1`) の安定性を向上。
  - テスト環境の隔離を行い、TAEF ログエラーを回避。
  - テスト結果のサマリー表示機能 (`scripts/AnalyzeWackReport.ps1`) を復元・統合。
- ドキュメント構成を整理し、古いドキュメントを `docs/archive/` へ移動。

### 改善
- ファイル一覧のソート順を Windows Explorer 準拠の「自然順ソート」に変更。(#63)
  - 数値を含むファイル名が `1, 2, 3, 11` のように数値として正しく並ぶように改善。

### 修正
- マルチモニター環境で異なるDPIスケーリング（100%/150%等）のモニター間でウィンドウを移動した際に、画像プレビューが予期せず拡大される問題を修正。(#55)
  - `ApplyPreviewFit()` から不要な `RasterizationScale` 除算を削除。
  - `XamlRoot.Changed` イベントでDPI変更を検出し、プレビューを再フィット。

## [1.5.4] - 2026-01-22

### 修正
- Microsoft Store 版で日本語 UI が表示されない問題を修正。
  - `AppxDefaultResourceQualifiers` を追加し、AppxManifest.xml の Resources に ja-JP が出力されるように修正。

### 改善
- Store 提出用パッケージ (msixupload) からローカルテスト用の署名済みパッケージを生成するスクリプトを追加。
  - `wack/build-from-upload.ps1`: msixupload を展開し、自己署名を付与
  - `wack/install-from-upload.ps1`: 生成したパッケージをインストール
  - 旧スクリプト (`build-signed-test.ps1`, `install-signed-test.ps1`) は削除

## [1.5.3] - 2026-01-21

### 修正
- Microsoft Store 版で一部の環境（国内メーカーPC等）において起動に失敗する問題を修正。
  - Win32 API (`GetCurrentPackageFullName`) を使用した堅牢なパッケージ判定に変更。
  - MSIX パッケージ環境では Windows App SDK Bootstrap を呼び出さないように修正。
- ContentDialog 表示時に XamlRoot が未確定の場合にクラッシュする問題を修正。
  - ウィンドウ初期化完了まで待機してからダイアログを表示するように改善。
- マップタイル設定（Esri WorldImagery）が再起動後に反映されない問題を修正。(#58)
- Store 提出用ビルドで日本語リソース（JA-JP）が resources.pri に含まれない問題を修正。(#56)
  - `DefaultLanguage` プロパティを追加し、PRI 生成時にすべてのロケールが含まれるように修正。

## [1.5.2] - 2026-01-20

### 修正
- MSIX 版で日本語リソースが反映されない問題を修正。
- EXIF 編集で位置情報がない写真を地図から選択する際、暗転表示のままになる問題を修正。

## [1.5.1] - 2026-01-20

### 修正
- 言語設定を日本語に変更しても表示が英語のままになる問題を修正。
- 起動時のフォルダ復元処理がファイルパス指定起動より優先される問題を修正。
- File Browser の Move ボタンでフォルダ選択時にナビゲーション遷移するように修正。

### 改善
- 回帰テストを追加（#46/#47/#51）。

## [1.5.0] - 2026-01-17

### 追加
- **地図上での矩形選択機能**: Ctrl + ドラッグで地図上に矩形選択エリアを作成し、エリア内の写真を一括選択できるようになりました。
- EXIF 情報の編集（撮影日時/位置情報）に対応。
- 手動テストチェックリストを追加。
- 戻る/進むボタンのナビゲーション履歴を追加。
- ログフォルダーを開くメニューとログ/トラブルシューティングのドキュメントを追加。
- フォルダー読み込み時の診断ログを強化（空フォルダー含む）。
- ファイルビューでマウスオーバー時に詳細情報のツールチップを表示。

### 修正
- EXIF 編集時の JPEG 再エンコードを避け、ロスレスで更新するように改善。
- EXIF 位置情報のクリアと地図クリックでの位置指定を追加。
- 位置選択中でも地図をパンできるように改善。
- 戻る/進む操作の失敗時に履歴を復元し、状態が壊れないように改善。
- メタデータ読み込み時の予期しない例外でクラッシュする問題を回避。

### 改善
- LastFolderPath のパスリカバリを改善：無効なパスの場合、親フォルダに順次フォールバックし、ユーザーの作業ディレクトリ復元性を向上。復元されたパスは設定に保存され、次回起動時に再利用される。
- フォルダ読み込み時にプレースホルダーを先に表示し、サムネイルを非同期生成して順次反映するよう改善。
- パッケージ版では OS のスプラッシュを優先し、未パッケージ時は独自スプラッシュを中央表示・最前面表示するように改善。

## [1.4.0] - 2026-01-06

### 追加
- 画像ファイルの関連付け（.jpg/.jpeg/.png/.heic）とファイル起動対応を追加。
- パンくずの区切り「>」クリックで子階層を開ける操作を追加。
- 地図ピンクリック時に該当写真へフォーカスする動作を追加。

### 変更
- フォルダ移動をダブルクリック操作に変更。
- プレビュー最大化時の地図エリア調整と高 DPI 画面フィットを改善。
- マップピンの位置合わせを先端基準に調整。
- サムネイル生成を高品質化。
- タスクバー背景の透明化用に unplated アイコンを追加。
- タイトルバーのアイコンをアプリ用アイコンに反映。

### 修正
- 位置情報がない写真で Null Island にピンが立つ問題を回避。

### 削除
- Store 方針に合わせて更新確認 UI を削除。

## [1.3.0] - 2026-01-02

### 追加
- 地図マーカークリック時のツールチップに EXIF 情報（撮影日時、カメラ、ファイル名、解像度）を表示。
- ツールチップから Google Maps で位置を開くリンクを追加。
- 衛星地図タイル（Esri WorldImagery）を追加し、OpenStreetMap と切り替え可能に。
- 地図タイルソース選択メニュー（Settings > Map tile source）を追加。
- タイルソースごとに独立したキャッシュディレクトリを使用。

### 変更
- 地図タイルソースの設定を永続化。
- ツールチップの多言語対応（日本語/英語）。

## [1.2.0] - 2025-12-30

### 追加
- 地図の初期倍率を設定メニューから変更できるように追加。

## [1.1.1] - 2025-12-30

### 修正
- About 表示のバージョン差分を解消するためのアセンブリ版同期。
- Release ワークフローでのバージョン整合チェックを強化。

## [1.1.0] - 2025-12-30

### 追加
- GitHub Release を参照したアップデート通知（自動/手動チェック）。
- MSI インストーラーの自動生成とリリースワークフロー整備。
- ユニット/結合/E2E テストの追加と CI 組み込み。

### 変更
- ローカル実行をアンパッケージ既定に変更（`WindowsPackageType=None`）。
- リリースチェックリストとバージョン整合チェックの強化。

## [1.0.0] - 2025-12-30

### 追加
- ファイルブラウザ（フォルダ選択、検索/画像フィルタ、パンくず、表示切替）。
- ファイル操作（新規フォルダ/移動/リネーム/削除、ドラッグ&ドロップ）。
- EXIF/GPS 抽出、複数選択の地図マーカー表示と自動フィット。
- 画像プレビュー（ズーム/パン/最大化、前後ナビ）。
- Mapsui による地図表示とオフラインタイルキャッシュ。
- ステータスバー/通知、起動スプラッシュ画面。
- 設定の永続化、言語/テーマ切替、設定のエクスポート/インポート。
- `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` へのアプリログ出力。

### 変更
- 地図表示を WebView2/Leaflet から Mapsui に移行。
- 解析の厳格化とフォーマットチェックを CI とフックに導入。
- 主要依存関係の更新（Windows App SDK、MetadataExtractor、Mapsui）。

### 修正
- WebView2 初期化失敗時のフォールバック表示を追加。
- `AppWindow` を安全に扱うようにウィンドウサイズ計算を修正。

### 削除
- WebView2 向けの旧タイルキャッシュ資産を整理。
