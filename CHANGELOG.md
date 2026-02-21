# 変更履歴

このプロジェクトの主な変更点をここに記録します。

## [Unreleased]

### 変更
- Renovate の `customManagers`（regex）を追加し、`PhotoGeoExplorer.csproj` / `PhotoGeoExplorer.Tests.csproj` と `docs/archive/PhotoGeoExplorer_plan.md` の `netX.Y-windows10.0.19041.0` 表記で `netX.Y` 部分を追従更新できるように変更。

## [1.7.0] - 2026-02-21

### 追加
- Cloudflare Pages の `docs` 配信に合わせ、`docs/help/index.html` と `docs/help/index.en.html` を追加。
- ファイル一覧の詳細表示に、撮影日時列と位置情報有無アイコン列を追加し、列表示の切り替えメニューを実装（#122）。
- 設定ペインに、ペインレイアウトプリセット（左・中央・右 / 左・右上/右下 / 左上左下・右）と領域ビュー割り当て（File / Preview / Map）を追加（#124）。

### 変更
- ヘルプ HTML と README のプライバシーポリシー参照先を `https://photogeoexplorer.pages.dev/privacy-policy` に切り替え。
- Cloudflare 配信方式を GitHub Actions workflow から Cloudflare Pages の Git 連携自動デプロイへ統一。
- `docs/GitHubPagesSetup.md` と `docs/MicrosoftStore.md` を更新。
- 詳細表示列の表示/非表示設定を `settings.json` に保存し、再起動時に復元するよう変更（#122）。
- ペインレイアウト設定（プリセット/領域割り当て）を `settings.json` に保存し、起動時に MainWindow レイアウトへ復元するよう変更（#124）。
- AI エージェント共通ガイドラインの PR 監視ループに、通常コメント（Issue comments: Codecov など Bot コメント）確認を追加。
- レイアウト分割計算・ペイン配置判定・プレビュー操作計算を純粋関数化し、UI依存なしで単体テストできるよう整理。
- `MainWindowLayoutCoordinator` のスプリッター更新計算を `TryComputeSplitterLengths` に集約し、`GridLength` 更新分岐を単体テストで検証できるよう整理。
- Renovate での追従更新を前提に、`.NET SDK 10.0.103` を `global.json`（`rollForward: latestPatch`）と GitHub Actions (`actions/setup-dotnet`) の両方で明示固定。

### 修正
- 設定ペインの表示言語コンボボックスで、システム既定選択時に起動直後の現在値が空表示になることがある問題を修正（#125）。
- ヘルプ表示で外部 URL の読み込みに失敗した場合、ローカル同梱ヘルプへ自動フォールバックするよう改善。
- Cloudflare 配信の `/privacy-policy` パスが確実に解決されるよう、`docs/privacy-policy/index.html` を追加。
- ペインレイアウト変更直後に、プレビューの自動再フィットと Fit ボタン（View）が効かないことがある問題を修正（#124）。
- 設定ペインのレイアウト設定向け `Resources.resw` に残っていた未使用キーを整理し、参照実体のあるキーのみへ統一。
- 設定ペインのレイアウト設定ロジック（プリセット切替・領域重複解消・領域ラベル）と `SettingsCoordinator` の同値レイアウト早期 return 分岐に対する単体テストを追加し、`codecov/patch` 失敗の要因を解消。

## [1.6.0] - 2026-02-20

### 追加
- AI エージェント共通ガイドラインに「PR 作成後の自動レビュー対応ルーティン」を追加。
- MainWindow スリム化（第2期）の実装計画書とタスク細分文書を追加（`docs/Architecture/MainWindowSlimdown-Plan.md`, `MainWindowSlimdown-Tasks.md`）。
- E2E の Required check 化に向けた運用手順書を追加（`docs/CI-E2E-RequiredCheck.md`）。

### 変更
- v1.6.0 は GitHub Release のみ公開し、Microsoft Store 版は据え置き（次回更新予定）。
- AI エージェント共通ガイドラインに、MainWindow 肥大化防止ガードレールと最小構成略図を追加。
- MainWindow スリム化（第2期）PR-8 の最終クリーンアップを実施（#102）。
  - `MainWindow.xaml.cs` の残存不要 `using` を整理し、最終行数 619 行（目標: 800 行以下）を確認。
  - `docs/Architecture/PaneSystem.md` / `MainWindow-Orchestration-Review.md` / `MainWindowSlimdown-Plan.md` を完了状態へ更新。
- MainWindow のメニュー Click リレーハンドラを撤去し、XAML から直接 Command バインディングに変更（#99）。
  - FileBrowserPaneViewModel に `ToggleImagesOnlyCommand`・`SetViewModeCommand` を追加。
  - FileBrowserPaneView にファイル操作系 Command プロパティを追加（`OpenFolderCommand` 等）。
  - MainWindow.xaml.cs から 14 個のリレーハンドラ（約 83 行）を削除。
- E2E ワークフロー（`.github/workflows/e2e.yml`）を `workflow_dispatch` のみから `pull_request/push` 常時実行へ拡張。
  - トリガー対象: `main` / `develop`
  - `dotnet test` に `--no-build` を追加し、事前ビルド済み成果物を再利用して二重ビルドを回避。
  - `Windows App SDK Runtime 1.8` を明示検証し、未導入時はインストールして再検証する手順に改善（起動時 bootstrap 失敗を回避）。
  - E2E 実行後（成功/失敗問わず）に `TestResults`、E2E 診断スクリーンショット、`%LocalAppData%\\PhotoGeoExplorer\\Logs` を artifact として収集・保存。
  - `Run E2E tests` を最大 2 回実行（1 回リトライ）にし、初回失敗時は `PhotoGeoExplorer` プロセスをクリーンアップして再試行する運用に改善。
- Security Checks の Gitleaks 実行方式を `gitleaks-action@v2` から CLI 直接実行へ変更（ライセンス未設定環境でも継続運用できるよう改善）。
- EXIF 編集フローを `MainWindow` から `IExifEditorService` に移管し、`FileBrowserPaneViewModel.EditExifCommand` から実行する構成へ変更（ISSUE#97 PR-2 着手）。
  - `IDialogService` / `DialogService` を追加し、ContentDialog 表示と XamlRoot 待機を共通化。
  - `IExifMetadataService` / `ExifMetadataService` を追加し、EXIF 読み書き依存を抽象化。
  - `MapPaneViewControl` を `IExifLocationPicker` 実装として接続。
  - EXIF ダイアログ要素と EXIF コンテキストメニューに AutomationId を追加し、E2E から安定して操作可能に改善。
- Map→FileBrowser の橋渡しを `MainWindow` 経由から `WorkspaceState` 経由へ移行（ISSUE#98 PR-3）。
  - `WorkspaceState` にフォーカス要求・選択要求・通知要求イベントを追加。
  - `MapPaneViewModel` が `WorkspaceState` へ要求を発行し、`MapPaneViewControl` の独自イベントを撤去。
  - `FileBrowserPaneViewModel` が `WorkspaceState` の要求を購読し、選択/フォーカスを反映。
  - `FileBrowserPaneService` に `filePath -> PhotoListItem` 解決ロジックを追加。
- ヘルプ機能を `IHelpService` / `HelpService` に移管し、MainWindow のヘルプ関連処理をサービスへ集約（ISSUE#103 PR-4）。
  - Help メニュー（Getting Started / Basic operations / Detailed help / About）を `MainViewModel` の Command バインディングへ変更。
  - `HelpService` がヘルプダイアログ、HTML ヘルプ別窓、WebView2 寿命管理、外部リンク起動を担当。
  - `HelpServiceTests` を追加し、ヘルプ HTML のファイル名決定と URI 解決（優先/フォールバック）を検証。
- 設定ロジックを `ISettingsCoordinator` / `SettingsCoordinator` に移管し、MainWindow の設定責務を統合（ISSUE#100 PR-5）。
  - 設定メニュー（言語/テーマ/ズーム/タイル/Export/Import）を `MainViewModel` Command + 設定状態プロパティへ一本化。
  - `MainWindow.xaml.cs` から設定ロード/保存/デバウンス/メニューチェック更新などの旧メソッド群を削除し、`SettingsCoordinator` 呼び出しへ置換。
  - `SettingsCoordinatorTests` を追加し、ロード/保存/デバウンスおよび正規化ロジックを検証。
- 設定Paneの本番統合を実施し、`Settings (development)` 導線を正式な `Settings...` 導線へ置換。
  - 設定メニューを「設定ダイアログ起動 + フィルターリセット」に整理し、言語/テーマ/地図/Import/Export は設定Paneに集約。
  - `SettingsPaneViewModel` を `SettingsCoordinator` 連携に変更し、実行中の設定変更・保存・Import/Export が即時にアプリ状態へ反映されるよう改善。
  - 設定Pane内の「MainWindow統合時に実装予定」表記を撤去し、Import/Export ボタンを有効化。
- MapPaneService の CA2025 アナライザ警告を抑制（`SemaphoreSlim` の安全な使用パターン）。
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
- 設定ペインの表示言語コンボボックスで、システム既定選択時に現在値が表示されない問題を修正。
  - `SettingsPaneViewModel` がシステム既定を空文字として保持し、`ComboBoxItem Tag=""` と整合するように改善。
- ファイルメニュー（Open/New/Rename/Move/Delete/Refresh/Home/Up）が反応しない回帰を修正。
  - `MainWindow.xaml` のファイルメニュー項目を `Click` ハンドラ経由に統一し、`FileBrowserPaneView` の操作メソッドへ確実に委譲するよう変更。
- WorkspaceState 監視を Pane 自己購読へ移行し、MainWindow の仲介を縮小（#101）。
  - `MapPaneViewModel` が `WorkspaceState.SelectedPhotos` を直接購読して地図マーカー更新を実行するよう変更。
  - `MainWindow.xaml.cs` の `OnWorkspaceStatePropertyChanged` を削除し、FileBrowser 変更時の設定保存トリガーを `SettingsCoordinator` に集約。
  - スプリッタ操作の確定時に `PersistLayoutSettingsCommand` 経由で `SettingsCoordinator.ScheduleSave()` を呼ぶ構成へ変更。
- 複数ピン表示後の `Ctrl + ドラッグ` 矩形選択で部分選択がファイル一覧へ反映されない問題を修正（#114）。
  - `WorkspaceState.PhotoSelectionRequested` を MainWindow で受け取り、`FileBrowserPaneView.SelectItemsByFilePaths` により UI 選択状態を同期するよう改善。
- 表示メニュー（Icon/List/Details、画像のみ表示）の操作が反映されない問題を修正。
  - 表示メニューは `Click` ハンドラ経由で `FileBrowserPaneViewModel` を直接更新する実装に戻し、確実に動作するよう改善。
- 表示メニューの画像フィルタ文言をトグル化し、状態に応じて「全てのファイルを表示」↔「画像のみを表示」を表示するよう改善。
  - MainWindow でメニュー項目テキストを明示更新し、文言が表示されない問題を修正。
- MainWindow に残っていた File/View メニューの Click リレーハンドラを撤去し、Command バインディングへ再移行（#118）。
  - `FileBrowserPaneViewModel` にファイル操作 Command（Open/Create/Rename/Move/MoveToParent/Delete）を追加。
  - `FileBrowserPaneView` が `ConfigureUiActionHandlers(...)` 経由でダイアログ/ピッカーを補助し、ViewModel から直接 UI 依存処理を呼ばない構成に整理。
  - `MainWindow.xaml` の MenuFlyoutItem は `ElementName` バインドから `x:Bind` へ変更し、メニューのポップアップ分離で Command 解決が不安定になる回帰を防止。
- 起動時のフォルダ/ファイルアクティベーション処理を `IStartupCoordinator` / `StartupCoordinator` に移管し、MainWindow の起動責務を縮小（#104）。
  - `GetStartupFolderOverride` / `TryGetOptionValue` / `ApplyStartupFolderOverrideAsync` / `ApplyStartupFileActivationAsync` を MainWindow からサービスへ移動。
  - `StartupCoordinatorTests` を追加し、引数解析と起動時フォルダ・ファイル適用を検証。
- パッケージ実行（MSIX/Store）でも独自スプラッシュを表示し、最短表示時間を 3 秒に統一。
  - `App.xaml.cs` でスプラッシュ表示を非パッケージ限定から全起動形態へ変更。
  - `MinimumSplashDurationMs` を 2000ms から 3000ms に変更。
- 設定Paneの設定変更が即時反映されないことがある問題を修正。
  - 保存ボタンを廃止し、設定変更をその場で即時適用する動作に統一。
  - ダイアログクローズ時に最終状態を再保存するよう改善。
  - テーマ/地図タイル/ズームの UI バインディングを型安全なプロパティ経由に変更し、変更反映の信頼性を改善。
  - ズーム入力が空になった際の `NaN/Infinity` を無視し、意図せずズーム値が変化する不具合を修正。
  - 明示保存時に保留中のデバウンス保存をキャンセルし、設定ファイルの二重保存を抑止。
  - 言語変更時は `ChangeLanguageAsync` 側の保存を優先し、重複する明示保存を回避。
  - SettingsPane の見出し/選択肢を `x:Uid` + `.resw` に統一し、英語UIで日本語が混在する問題を修正。
  - `SaveIfDirtyAsync` に dirty 判定を追加し、変更がない場合の不要な保存を抑止。
  - 言語変更の即時適用は再起動プロンプトなしで行い、確定保存時にのみプロンプトを表示するよう調整。
  - 地図ズームレベル定義を `MapZoomLevelCatalog` に集約し、UI と保存処理の定義重複を解消。
- E2E テストで一部ランナー環境において `IsOffscreen` プロパティ未サポート例外が発生する問題を修正。
  - `AppE2ETests` の UI プロパティ参照を `SafeGet` ベースに変更し、例外耐性を強化。
  - EXIF コンテキストメニュー取得を右クリック + `Apps` キーのリトライに改善し、フレークを低減。
- EXIF コンテキストメニュー取得に `list` 右クリック・`Shift+F10`・process 非依存探索のフォールバックを追加し、CI の取得失敗を低減。
- `ExifEditorSaveAndReopenKeepsCoordinates` の一覧読み込み待機条件を `minimumCount: 2` に揃え、`sample.jpg` 未反映タイミングでのコンテキストメニュー失敗を低減。
- `ExifEditorSaveAndReopenKeepsCoordinates` にコンテキストメニューのウォームアップ手順（`folder` で開閉）を追加し、`folder` 側のメニューが無い環境では best-effort でスキップするように改善。
- EXIF コンテキストメニュー探索を強化し、項目中心左クリックでのフォーカス取得、`Focus` 時の COM 例外を無視した再試行、`Keyboard.Type`/`TypeSimultaneously` によるキー入力安定化、`Apps` キー優先・要素中心座標での右クリック・`MenuItem(Name contains EXIF)` フォールバック・失敗時の一覧スナップショット出力を追加。
- E2E テストのファイル一覧探索に `FileListList` を追加し、表示モードによって `WaitForList` が失敗する問題を修正。
- `WaitForList` のタイムアウト契約を `TimeoutException` に統一し、タイムアウト時の診断ログ確実化と `List/DataGrid` フォールバック探索を追加。
- `WaitForList` タイムアウト時に、ファイル一覧候補の UIA 診断ログとスクリーンショットを出力するよう改善。
- E2E の `WaitForMainWindow` に COM/Win32 タイムアウト耐性を追加し、`GetMainWindow` 取得失敗時は同一プロセスの Window をデスクトップ走査で補完。
- `WaitForMainWindow` のリトライを `ignoreException` 対応にし、デスクトップ走査時の一時的な UIA COM タイムアウトを失敗扱いせず再試行するよう改善。
- `FindByAutomationId` と関連リトライを例外許容化し、UIA 検索中の一時的 COM タイムアウトでテストが即失敗しないよう改善。
- Map 初期化時に `MapPaneViewModel.Map` の変更通知が発火せず、地図が表示されない場合がある問題を修正。
- CI 相当のローカル品質ゲート（analyzer/nullability）で発生するビルド失敗を解消。

### テスト
- Map 選択判定のテストを拡充（矩形境界判定、重複除外、閾値判定、引数異常系）。
- `ExifEditorServiceTests` を追加し、編集可否バリデーションと編集フロー分岐（キャンセル/位置取得/保存）を検証。
- EXIF 編集フローの E2E テストを追加（右クリックメニュー有効状態、日時UIトグル、座標保存後の再編集確認）。
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
