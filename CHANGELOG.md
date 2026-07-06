# 変更履歴

このプロジェクトの主な変更点をここに記録します。

## [Unreleased]

### CI
- E2E ワークフローの総実行時間を短縮 (#182)
  - **実測（attempt 単位、GitHub Actions ログの実際の attempt 別 Passed/Failed 行に基づく再集計）**: 分割前後を問わず直近の main/PR 実行はいずれも 1 回目の試行で 1 本だけ flaky 失敗し 2 回目で成功する既存パターンが確認された（例: 分割前 main run `28786690007`＝13本中1本失敗→retry成功、`28333186948`／`28058515771`＝7本中1本失敗→retry成功）。これは本 PR が原因ではなく E2E スイートに元々存在する未解決の flaky（#180 が前提とする flaky 切り分けが必要な事象）であり、単一 attempt の実行時間を単純にテスト数へ按分した当初の分析は誤りだったため、以下は attempt 単位の実測に基づき訂正する
  - 固定コスト（checkout/restore/build/ランタイム確認）は約 2.5〜3 分でほぼ一定。#181 で 3→13 本に増えテスト実行時間（1 attempt のクリーン実行で 13 本 ≈ 2m40s〜3m0s）が支配的コストになったため、runner 分割が最も効果的と判断
  - `AppE2ETests` の全 13 テストに `[Trait("Suite", "1"|"2")]` を付与し、実行時間が概ね均等になるよう 7本/6本に分割（クリーン attempt 実測: suite1 ≈ 1m39s / suite2 ≈ 1m00s）
  - `e2e.yml` を `matrix: suite: [1, 2]`（`fail-fast: false`）でジョブ分割。suite 1 は正フィルタ（`Suite=1`）、suite 2 は否定フィルタ（`Suite!=1`）で実行し、`[Trait("Suite", ...)]` の付け忘れがあった新規テストも自動的に suite 2 側で実行されるようにして「両 suite から漏れて CI が静かに未実行になる」リスクを構造的に排除（thread-owl レビュー指摘対応）。TRX ファイル名・アーティファクト名に suite を含め衝突を回避
  - `actions/cache/restore` + 条件付き `actions/cache/save`（v6、Node.js 24 対応）で NuGet パッケージキャッシュ（`~/.nuget/packages`、キーは csproj/global.json のハッシュ）を追加し restore を短縮。保存は suite 1 のみに限定し matrix 両ジョブの重複 save による無駄を排除（Copilot/thread-owl レビュー指摘対応）。`packages.lock.json` は導入せず（依存解決方式・Renovate 運用への影響を避けるため）
  - **根本原因の特定（thread-owl レビューで発覚）**: 分割前 4 run は毎回 `DeleteConfirmationDialogShowsForMultipleSelection` が attempt 1 で失敗していたが、これは「同じテストが壊れている」のではなく「そのプロセスで最初に実行されるテストである」ことが原因と判明（失敗前に完了したテストが 0 件であることをログで確認）。分割後は suite 2 の最初のテスト `ClipboardCopyPasteCopiesFileIntoSubfolder` も同様に attempt 1 で失敗するようになった（2 run とも再現）。つまり実態は「プロセス内で最初に実行される `[E2EFact]` は JIT ウォームアップ・AV スキャン・WinUI3 初回描画等のコールドスタートコストにより UIA タイムアウトしやすい」という既存の系統的 flaky であり、matrix 分割によって「最初の1本」の枠が単一プロセス1つから suite 数分（2つ）に増えたことで、flaky の発生機会が比例して増加していた
  - **対応**: `AppE2ETests` に `IClassFixture<WarmupFixture>` を導入し、各 matrix job（テストプロセス）で実テスト開始前に 1 回だけアプリを起動→終了するウォームアップを追加。これにより OS/AV/JIT/コンポジタのコールドスタートコストを実テスト計測から除外し、「最初の1本」問題を suite 分割の有無によらず解消する。ウォームアップ自体の失敗は best-effort（実テストの成否に影響させない）
  - **flaky 率への影響**: 各 matrix job は独立した runner（VM）で実行されるため、issue #182 で明示的に非推奨とされた「同一マシン内 xUnit 並列によるフォーカス競合」は発生しない。ウォームアップ導入により「最初の1本」のコールドスタート flaky は理論上解消されるが、ウォームアップ導入後の CI 実測での再検証が必要（PR レビュー継続中に追記）
  - wall clock は attempt を含む実測で比較する必要がある: 分割前（単一 job・retry込み）の job 全体は 8m51s（run `28786046897`）。分割後（2 job 並列・両 suite とも retry込み）は wall clock = max(suite1 job, suite2 job) = 6m35s（run `28788908674`）。retry を含めても約 25% の短縮を確認（ウォームアップ追加後の再計測は別途反映）
  - リトライ回数上限・`blame-hang-timeout` は変更なし

### テスト
- E2E に FileBrowser 操作系の残りシナリオ（右クリック複数選択復元 / クリップボード / 移動・コピー競合キャンセル）と操作系 fixture を追加し、#181 の受け入れ条件を完了 (#181)
  - `RightClickOnSelectionPreservesMultipleSelection`: 複数選択中に選択項目を右クリックしてコンテキストメニューを開いても複数選択が維持されること（VM `ResolveRightTapSelection` による復元）を検証。復元は RightTapped ハンドラのロジックのため APPS キーではなくマウス右クリックで駆動し、ESC で非破壊に終了
  - `ClipboardCopyPasteCopiesFileIntoSubfolder` / `ClipboardCutPasteMovesFileIntoSubfolder`: Ctrl+C / Ctrl+X → フォルダへダブルクリック遷移 → Ctrl+V のキーボード操作で VM のクリップボード経路（`CopySelectionToClipboard` / `CutSelectionToClipboard` / `ExecutePasteAsync`）を駆動し、貼り付け先への項目出現（UI）とディスク状態（コピー元残存 / 移動元消滅）で検証。コンテキストメニューの `CopyPathMenuItem` / `CopyMenuItem` は別経路のため不使用（棚卸し `E2E-Phase5-Audit.md` §4 準拠）
  - `MoveConflictCancelKeepsSourceWithoutErrorDialog` / `CopyConflictCancelKeepsSourceWithoutErrorDialog`: 同名衝突での移動/コピーで競合ダイアログが表示され、キャンセル時にエラーダイアログが出ない（`FileOperationSummary.HasReportableFailures` の Cancelled 除外）ことと、操作元・衝突先の両ファイルが無傷で残ることを検証。`PasteSelectionAsyncCore` は move/copy で競合ダイアログ・エラー表示とも別分岐のため両方を駆動（thread-owl レビュー指摘対応）
  - `ClipboardShortcutWithoutSelectionIsNoOp`: 選択なし時のクリップボードショートカットが no-op であることを検証（thread-owl レビュー指摘対応）。選択ありの Ctrl+C 後に選択解除して Ctrl+X を送出し、貼り付けがコピーとして成立（項目出現＋コピー元残存）することで、no-op が Copy 状態を破壊しないことを積極的に検証（negative 待機だけでは Ctrl 送出失敗と区別できないため）
  - `E2ETestData` に操作系 fixture のオプトイン拡張（複数選択・クリップボード用 `second.jpg`、移動競合用 `folder/sample.jpg` 同名衝突）と `RootPath` 公開を追加。既定 fixture 前提の既存 7 テストには影響なし。破壊操作は従来どおり temp 隔離で完結
  - production: 競合ダイアログ文面に `FileBrowser.ConflictMessage`、メッセージダイアログ文面に `FileBrowser.MessageDialogText` の automation ID を付与（`FileBrowserDialogs`、UI 挙動不変。エラーダイアログ非出現をロケール非依存で検証するためのフック）
  - ローカル検証: 新規 6 テスト各 2 ラウンド pass、全 13 テスト通し 13/13 pass、単体テスト 629/629 pass
- E2E `WaitForMainWindow` が起動時の SplashWindow を誤取得する flaky を修正 (#186)
  - `MainWindow.xaml` の RootGrid に `AutomationProperties.AutomationId="MainWindow"` を付与し、`SplashWindow.xaml` に `"SplashWindow"` を付与。`TryGetMainWindow` を `AutomationId="MainWindow"` マーカーの肯定的識別に変更（SplashWindow 除外ではなく MainWindow を確実に選択する）
  - SplashWindow が `IsAlwaysOnTop` で先に `Activate` される起動シーケンスで `Process.MainWindowHandle` が SplashWindow を指す問題を解消
  - `WaitForMainWindowReturnsMainWindowNotSplashWindow` テストを追加：`WaitForMainWindow` が返すウィンドウに `MainWindow` マーカーが存在し `SplashWindow` マーカーが存在しないことを検証

### バグ修正
- リネーム・フォルダ作成・外部ファイルドロップ後に `COMException (0x8001010E: RPC_E_WRONG_THREAD)` でクラッシュする問題を修正 (#188)
  - `ExecuteRenameAsync` / `ExecuteCreateFolderAsync` / `HandleExternalFileDropAsync` で `await RefreshAsync().ConfigureAwait(false)` 後に UIスレッド外から `SelectedItem` セッター（WinRT PropertyChanged 通知）を呼んでいたため
  - `SelectItemByPath` の呼び出しを `_uiDispatcher.RunAsync` でラップし、`SelectedItem` への代入を UIスレッド上で実行するよう修正

### テスト
- E2E に削除確認ダイアログ文面の分岐検証シナリオを追加し、コンテキストメニューヘルパーを ID 指定の汎用版へ一般化 (#181)
  - `DeleteConfirmationDialogShowsForSingleFile` / `...ForSingleFolder` / `...ForMultipleSelection`: 各選択パターンで削除確認ダイアログが実機表示されキャンセルできること（非破壊）を検証。トリガーは選択を変えない `Delete` キー。文面の分岐内容（File / Folder / Multiple）の正確性は単体テスト（`BuildDeleteConfirmationMessage`）が担保するため、E2E は文面が非空（＝ダイアログ表示）であることをロケール／リソース解決非依存に確認（未パッケージ起動・en ランナーでは未解決のリソースキーが返るため、解決済み文面に依存しない）
  - **連続ダイアログ開閉の flaky を避けるため 1 テスト 1 ダイアログに分割**（各シナリオをアプリ起動から独立）。確認ダイアログ出現まで対象項目への focus+`Delete` 送出をリトライ、`Focus()` の UIA COMException(0x80040201) を `ignoreException` で吸収、文面が非空になるまで待機。ローカルで 3 テスト × 2 ラウンド = 6/6 pass を確認
  - `OpenExifMenuForItemName` を automation ID 引数を取る `OpenContextMenuItemForItemName` へ汎用化し、EXIF 専用の名前フォールバック（`FindEditExifMenuItemByName`）を ID 直引きに置換して削除。既存 ExifEditor 2 テストは `FileBrowser.EditExifMenuItem` で呼ぶよう更新
  - production: 確認ダイアログのメッセージ TextBlock に `FileBrowser.ConfirmationMessage` ID を付与（`FileBrowserDialogs.ShowConfirmationAsync`、UI 挙動不変）
  - #181 を 3 分割した P5-B-1（基盤＋削除確認）。残り（右クリック選択復元 / clipboard / 移動競合）＋fixture 拡張は後続 PR
- E2E（`PhotoGeoExplorer.E2E`）の現状フィット棚卸しを実施し、コンテキストメニュー全項目に automation ID を付与して P5-B（操作系シナリオ追加）の前提を整備 (#180)
  - `FileBrowserMenuBuilder.BuildFileContextFlyout()` の項目は従来 `FileBrowser.EditExifMenuItem` のみ ID 付与済みだったため、新規フォルダ / エクスプローラーで開く / フォルダをエクスプローラーで開く / パスをコピー / Google マップで開く / リネーム / 移動 / 親フォルダへ移動 / コピー / 削除の各項目に `FileBrowser.*MenuItem` 形式（既存命名に統一）の ID を付与。名前部分一致に依存しない要素特定を可能にしメニュー起因の flaky 低減にも寄与
  - 既存 3 E2E テストが参照する automation ID（`FileList*` / `FileBrowser.EditExifMenuItem` / `ExifEditor.*` / `MetadataSummaryText` / `PreviewImage` / `MapStatusPanel`）が Phase 1 分割後も全て生存・命名一貫であることを確認（整合点検）
  - #174（テストホスト内の実 watcher クラッシュ、PR #183 で解消）と E2E flaky は別系統であることを切り分け（E2E は別プロセスのアプリを起動しアプリ終了後に temp 削除するため watcher 競合は波及しない）。production コードの挙動変更なし（ID 付与のみ）
- 単体テストが `FileBrowserPaneViewModel` 経由で実 `FolderWatcherService`（実 `FileSystemWatcher` ＋ `System.Threading.Timer`）を起動しないよう no-op フェイクを注入し、テストホストの非決定的クラッシュ要因を解消 (#174)
  - `LoadFolderAsync` が temp ディレクトリを実監視し、ディレクトリ削除と debounce タイマー発火が競合してテストホストが途中終了し得る問題（#159/PR #173 で観測）への対処
  - `NoOpFolderWatcherService` を `TestUtilities.cs` の共有ヘルパー（`internal sealed` ＋ static `Shared`）へ昇格し、`PhotoGeoExplorer.Tests` アセンブリ内の **全 `FileBrowserPaneViewModel` 生成箇所**に注入。`FileBrowserPaneViewModelTests`（`CreateViewModelWithFakes` ＋ インライン生成）に加え、`StartupCoordinatorTests` / `SettingsCoordinatorTests` / `SettingsPaneViewModelTests` の生成箇所も網羅
  - 特に `StartupCoordinatorTests` の `ApplyStartupAsync*` 2 テストは `LoadFolderAsync(tempDir)` → 実 watcher 起動直後に `Dispose()` で同 tempDir を再帰削除しており、#174 と同型の latent な非決定的クラッシュを含んでいた。本注入でこれを除去し「単体テストは実 watcher を一切起動しない」をアセンブリ全体で保証
  - production コードは変更なし（テストでは `UiDispatcher.TryEnqueue` が no-op のため VM 側ハンドラは元から無害）
- View 層 UI 構築ヘルパーを codecov のカバレッジ集計対象から除外し、`codecov/patch` の構造的 fail を解消 (#171)
  - #156/#157/#158 で `*View.xaml.cs`（既に `codecov.yml` で ignore 済み）から分離した `FileBrowserDialogs` / `FileBrowserDragDropHandler` / `FileBrowserMenuBuilder` は、`MenuFlyout` / `ContentDialog` / `DataPackage` 等の WinRT 型を直接生成する単体テスト不能な View 責務（MVVM ガードレール準拠）。同種コードが ignore 対象外に移ったため patch coverage が常時 0% で fail していた
  - 当該 3 ファイルのみを `codecov.yml` の `ignore` に追加。テスト可能な純粋関数（`FileBrowserDialogErrorMap`）・ViewModel・Service はカバレッジ集計対象として維持（ブランケット除外はしない）

### ドキュメント
- E2E 現状フィット棚卸し結果（automation ID 一覧・操作系 ID ギャップと対応・テストデータ拡張要件・#174 との関連・E2E 安定化方針・P5-B 着手前提）を `docs/Architecture/E2E-Phase5-Audit.md` に整理 (#180)
- FileBrowser ゴッドクラス解体 Phase 1（#150 / 子 issue #152〜#159）の成果を `docs/Architecture/FileBrowserDecomposition-Phase1-Summary.md` にモジュール行数で集計（View `1,941→908`・ViewModel `2,089→1,263`、責務別モジュール 9 件を新設）(#159)

### リファクタリング
- `FileBrowserPaneView` に残存していた操作判断ロジックを ViewModel / Service へ移し MVVM 境界を是正 (#159)
  - エラーダイアログ表示要否の判定を `FileOperationSummary.HasReportableFailures`（キャンセルを除く失敗の有無）へ集約し、move/copy/delete/paste/drop で散在していた `HasFailures && Failures.Any(... != Cancelled)` と素の `HasFailures` の不統一を解消（ユーザーキャンセル時はエラーダイアログを表示しない挙動へ統一）
  - 「単一フォルダ選択なら移動ではなく開く」判断を VM `IsSingleFolderSelected` へ、削除確認メッセージ（単数/複数・ファイル/フォルダ分岐）の組み立てを VM `BuildDeleteConfirmationMessage()` へ、Ctrl+C/X の選択有無判定を VM `CopySelectionToClipboard()` / `CutSelectionToClipboard()` へ、右クリック時の選択復元対象の決定を VM `ResolveRightTapSelection()` へ移動。View はダイアログ/ピッカー表示・ListView 操作・キーイベント解釈のみに限定
  - VM へ移したロジックと `HasReportableFailures` にパラメータ化単体テストを追加（計 21 ケース）。VM へ移動した削除メッセージ組み立ては `FileBrowserDialogs.BuildDeleteMessage` を削除して一本化
  - #156/#157/#158 完了後の #150 Phase 1 最終タスク。挙動はキャンセル時のダイアログ抑止統一を除き不変
- コンテキストメニュー・フライアウト構築を `FileBrowserPaneView` コードビハインドから `FileBrowserMenuBuilder` へ分離 (#158)
  - ファイル一覧のコンテキストメニュー（`BuildFileContextFlyout`）、詳細表示の列トグルメニュー（構築・キャッシュ・表示前同期・トグル反映）、ブレッドクラムの子フォルダ一覧フライアウト（`ShowBreadcrumbChildrenFlyout`）を `Panes/FileBrowser/FileBrowserMenuBuilder.cs`（internal sealed）へ移動
  - メニュー構築は純粋な View 責務のため ViewModel ではなく View 層ヘルパーとして分離（MVVM ガードレール準拠）。ビルダーはコンストラクタで ViewModel アクセサと、コンテキストメニュー各項目のクリックハンドラ群（`FileContextMenuHandlers` レコード）を受け取る。ダイアログ・ピッカー操作を伴うクリック処理は View 責務のため実装は View に残す
  - 列トグルは `Tag` 文字列定数（`DetailsColumnModifiedTag` 等）＋ `switch` による VM プロパティ更新を、`(ToggleMenuFlyoutItem, getter, setter)` の対応表へ整理し、`SyncDetailsColumnsFlyout` の逐次代入と `OnDetailsColumnMenuItemClicked` の分岐を排除
  - ブレッドクラム子フォルダフライアウト表示中の誤ナビゲーション抑止フラグ（旧 `_suppressBreadcrumbNavigation`）はフライアウト寿命と連動するためビルダーが所有し、`SuppressBreadcrumbNavigation` プロパティで公開。View の `OnBreadcrumbItemClicked` はこれを参照（表示・操作挙動は不変）
  - XAML のイベント結線（`Drop`/右クリック/列メニュー/ブレッドクラム）は View に薄い委譲ハンドラとして残し、処理本体をビルダーへ移譲。未使用化した `using Microsoft.UI.Xaml.Automation` を削除
  - `FileBrowserPaneView.xaml.cs` を 1,191 行から 805 行へ削減（−386 行）
- ドラッグ＆ドロップ処理を `FileBrowserPaneView` コードビハインドから `FileBrowserDragDropHandler` へ分離 (#157)
  - 内部項目の移動/コピー（`OnFileListDragOver` / `OnFileListDrop`）、Explorer 等へのドラッグアウト（`OnFileItemsDragStarting` の StorageItems 遅延提供・`OnFileItemsDragCompleted` の Move 完了後リフレッシュ）、外部ファイル/フォルダの受け入れ、ブレッドクラムへのドロップ（`OnBreadcrumbDragOver` / `OnBreadcrumbDrop`）、ヒットテスト（`IsInternalDrag` / `TryGetDropTargetFolder` / `TryGetBreadcrumbTarget`）、ドラッグ状態（`_dragItems` / `_wasInternalDrop` / `InternalDragKey`）を `Panes/FileBrowser/FileBrowserDragDropHandler.cs`（internal sealed）へ移動
  - D&D はビジュアルツリー操作・`DataPackage` 操作を伴う純粋な View 責務のため、ViewModel ではなく View 層ヘルパーとして分離（MVVM ガードレール準拠）。ハンドラはコンストラクタでヒットテスト用ルート要素（RootGrid）・ViewModel アクセサ・`FileBrowserDialogs` を受け取る
  - XAML のイベント結線（`DragOver` / `Drop` / `DragItemsStarting` / `DragItemsCompleted`）は View に薄い委譲ハンドラとして残し、処理本体をハンドラへ移譲（表示・操作挙動は不変）
  - `FileBrowserPaneView.xaml.cs` を 1,515 行から 1,191 行へ削減（−324 行）。ビジュアルツリー汎用ヘルパー `FindAncestor<T>` は View の他箇所でも使用するため View に残し、D&D 専用の `IsDescendantOf` はハンドラへ移動
- `FileBrowserDialogs` のエラー → リソースキーのマッピングをテスト可能な純粋関数 `FileBrowserDialogErrorMap` へ分離 (#167)
  - 作成/リネーム・Move・Copy・削除の各操作エラー（`FileOperationError` → タイトル/メッセージのリソースキー）の `switch` を、WinUI 非依存の `internal static` クラス `Panes/FileBrowser/FileBrowserDialogErrorMap.cs` へ抽出。`ContentDialog` 表示・`LocalizationService` 解決から切り離し、対応関係をユニットテストで固定できるようにした
  - `FileBrowserDialogs` の各 `ShowXxxOperationErrorAsync` はマッピング関数の結果（リソースキー）を解決して `ShowMessageAsync` を呼ぶだけに簡素化（表示挙動は不変）
  - 既知の `FileOperationError` 全 8 値を網羅する `FileBrowserDialogErrorMapTests`（32 ケース、パラメータ化テスト）を新設。default フォールバックも併せて固定
- ダイアログ・ピッカー表示を `FileBrowserPaneView` コードビハインドから `FileBrowserDialogs` ヘルパーへ分離 (#156)
  - 競合解決（Move/Copy）・テキスト入力・確認・メッセージ・各種操作エラー（作成/リネーム/Move/Copy/削除）ダイアログ、フォルダピッカー（HWND Interop 含む）、`EnsureXamlRootAsync` を `Panes/FileBrowser/FileBrowserDialogs.cs`（internal sealed）へ移動。View は `XamlRoot` 供給元（RootGrid）と `HostWindow` アクセサをコンストラクタで渡し、`_dialogs.ShowXxxAsync(...)` 経由で呼び出す（VM へ渡す競合解決コールバック型は不変）
  - リソースキー以外ほぼ同一だった `ShowMoveConflictDialogAsync` / `ShowCopyConflictDialogAsync` を、リソースキー接頭辞をパラメータ化した単一実装 `ShowConflictAsync` に統合
  - 操作エラーダイアログ群（`FileOperationError` → タイトル/メッセージの switch）は分岐表が操作種別ごとに異なるため統合せず移動のみとした（汎用 `DialogService` との統合可否を含め将来のフォローアップは #156 に記録）
  - `FileBrowserPaneView.xaml.cs` を 1,921 行から 1,515 行へ削減（−406 行）。ダイアログ表示は View の正当な責務のため挙動は不変で、既存テスト 576 件は全件パス
- ステータス表示とメタデータロードを `FileBrowserPaneViewModel` から子 ViewModel `FileBrowserStatusViewModel` へ分離 (#155)
  - ステータスオーバーレイ（空フォルダ・エラー時の案内とアクション）、ステータスバー（フォルダ/件数/選択/GPS アイコン）、選択アイテムの EXIF メタデータ非同期ロード（先行ロードをキャンセルする CTS 管理を含む）を `Panes/FileBrowser/FileBrowserStatusViewModel.cs`（internal sealed, BindableBase, IDisposable）へ移動
  - 親 ViewModel は `Status` プロパティとして子 VM を公開し、選択変更・フォルダ読み込み・フィルタ変更のタイミングでメソッド呼び出しで状態を渡す（イベント購読ではなく明示的な呼び出し）。XAML バインディングは `Status.StatusBarText` 等のパスに更新（`FileBrowserPaneView.xaml` のオーバーレイ、`MainWindow.xaml` のステータスバー）
  - メタデータ取得は `Func` 注入のテスト用シーム（既定は `ExifService.GetMetadataAsync`）とし、UI 更新は `IUiDispatcher`（#152）経由を維持。Move/Copy の進捗・完了メッセージは `FileOperationCoordinator`（#154）から `Status.SetStatusBarText` への通知経路で維持
  - オーバーレイ表示・ステータスバー組み立て・GPS アイコン状態（有効/測位失敗/GPS なし）・メタデータロードのキャンセルを検証する `FileBrowserStatusViewModelTests`（15 件、GPS 状態はパラメータ化テスト）を新設
- ファイル操作・進捗・クリップボード管理を `FileBrowserPaneViewModel` から `FileOperationCoordinator` へ分離 (#154)
  - Move/Copy でほぼ同一だった進捗管理（CTS・進捗タイマー・カウンタ・キャンセルコマンド連携）を、操作種別（進捗メッセージのリソースキー・進捗状態通知）をパラメータ化した共通実装 `ExecuteTransferAsync` に統一
  - クリップボード状態（項目・Cut/Copy 種別・Cut 全件成功時のクリア判定）を Coordinator へ移動。`IsMoveInProgress` / `IsCopyInProgress` / `CanPasteSelection` 等の XAML バインディングは ViewModel のプロパティとして維持し、コンストラクタ注入のコールバックで変更通知を中継
  - #137 で入れた CancelCommand との race 対策（`finally` 内の UI スレッド集約）は共通実装でも維持。競合解決コールバックの UI スレッド marshal も `IUiDispatcher` 経由のまま
  - 操作完了後の `RefreshAsync` 呼び出し・選択復元・完了メッセージ表示は ViewModel 側の責務として残し（共通ヘルパー `FinishTransferAsync` に集約）、`ExecuteXxx` 系の internal シグネチャは View からの呼び出し口として維持（既存テストは VM 経由のまま全件パス）
  - 統一後の共通進捗実装・キャンセル連携・クリップボード管理・バリデーションを直接検証する `FileOperationCoordinatorTests`（28 件、Move/Copy はパラメータ化テストで対称検証）を新設
- サムネイル生成処理を `FileBrowserPaneViewModel` から `ThumbnailGenerationCoordinator` へ分離 (#153)
  - SemaphoreSlim による並列数制限・UI スレッドタイマーによるバッチ更新・CancellationTokenSource 管理を `Panes/FileBrowser/ThumbnailGenerationCoordinator.cs`（internal sealed, IDisposable）へ移動。ViewModel は `StartGeneration` / `Dispose` を呼ぶだけになった
  - #147 / #137 で入れた並行処理対策（Task.Run 前のトークンキャプチャ、Dispose 時の全タスク完了待ち、ObjectDisposedException の安全網）は移動先でも維持
  - #147 の回帰テスト 2 件をリフレクションによる private フィールドアクセスから Coordinator の公開 API（コンストラクタ注入・internal メンバー）経由のテストへ書き換え、`ThumbnailGenerationCoordinatorTests` に移設。生成対象フィルタ・並列数制限・バッチ更新・キャンセルの単体テストも追加
- UI スレッドディスパッチ処理を `FileBrowserPaneViewModel` から共通サービス `UiDispatcher` へ抽出 (#152)
  - `IUiDispatcher` / `IUiDispatcherTimer` インターフェースを `Services/` に新設し、`RunAsync` / `EnqueueAsync<T>` / `TryEnqueue` / `CreateTimer` を提供
  - `DispatcherQueue` が取得できないテスト環境では従来どおり同期実行にフォールバック
  - ViewModel はコンストラクタ DI で `IUiDispatcher` を受け取り、`DispatcherQueue` への直接依存を排除
  - `SetDispatcherQueue` 経路を廃止。ViewModel は `MainWindow` の DI 構築（UI スレッド上）で生成されるため、View の `OnLoaded` / `OnDataContextChanged` からの再設定は不要と確認

### 修正
- `LoadFolderAsync` の並行実行で破棄済み CTS の `Token` アクセスにより `ObjectDisposedException` が発生し、フォルダ読み込みは成功しているのに「フォルダーの読み込みに失敗しました」エラーオーバーレイが誤表示される問題を修正 (#164)
  - `CancellationToken` を CTS 生成直後にローカルへ保持し、並行呼び出しが CTS を `Cancel` + `Dispose` した後も破棄済み CTS に触れないよう変更。並行実行を決定的に再現する回帰テストを `FileBrowserPaneViewModelTests` に追加
  - 競合の引き金だった「画像のみ表示」トグルの二重発火を解消。チェックボックスの `Checked`/`Unchecked` ハンドラと `ToggleImagesOnlyCommand` 内の `RefreshAsync` を削除し、再読み込みは `ShowImagesOnly` setter（`UpdateFilterState`）の 1 経路に統一
  - `FileBrowserStatusViewModel.LoadMetadataAsync` の同系統 race（`_metadataCts` の差し替えが最初の await 後に行われるため、連続選択時に先行ロードのキャンセル漏れ→古いメタデータで GPS アイコン・`SelectedMetadata` が上書きされる）も修正。差し替えを最初の await より前の同期実行に移動し、回帰テストを追加
  - `ResetFilters` で 2 つのフィルタを setter 経由で更新すると `UpdateFilterState` が二重発火し `LoadFolderAsync` が並行実行される残存パターンを解消（レビュー指摘対応）。フィールドを直接更新して再読み込みを 1 回に合流させ、回帰テストを追加
  - overlay の「フィルタをリセット」経路で View 側 `ResetFiltersAsync` が `ResetFilters()` の直後に `RefreshAsync()` を呼び `LoadFolderAsync` が 2 回並行実行される（メニュー経路は 1 回で挙動が割れていた）残存パターンを解消（レビュー指摘対応）。`ResetFilters()` が再読み込みを担う設計に合わせ View 側の冗長な `RefreshAsync` 呼び出しを除去し、両 UI 経路を 1 回に統一
- メタデータロード中にキャンセル（CTS 破棄）が走った後、ローダーがキャンセルを観測せず正常リターンすると破棄済み CTS の `Token` getter で `ObjectDisposedException` が発生する潜在競合を修正 (#163)
  - `CancellationToken` を await 前にローカルへ保持し、破棄済み CTS に触れないよう変更。競合を決定的に再現する回帰テストを `FileBrowserStatusViewModelTests` に追加
- Release ビルドで `HarfBuzzSharp` などのネイティブ依存の PDB を処理しようとして `mspdbcmf.exe` パス構築バグ (MSB6011) が発生し CI が失敗する問題を修正
  - `AppxSymbolPackageEnabled=false` を Release 構成に追加してシンボルパッケージ生成を無効化

## [1.8.5] - 2026-06-09

### 改善
- 問題検出バナーの文言を修正。graceful degradationで例外をキャッチして正常終了した場合でもバナーが表示されるため、「正常に終了しませんでした」「クラッシュレポート」等の不正確な表現を「前回の実行中に問題が検出されました」「問題レポート」等に変更 (#146)
- メタデータ解析の部分失敗をgraceful degradationで処理し、アプリのクラッシュを防止 (#144)
  - `ExifService.ReadMetadata` のタグ取得処理全体を `MetadataException` でラップ。将来 `Get*` 系タグが追加された場合も自動的に保護される
  - `MapPaneService.LoadMetadataForItemAsync` に汎用例外キャッチを追加。想定外の例外が発生しても該当ファイルをスキップしてアプリ動作を継続する
  - `OnUnobservedTaskException` で `WriteCrashLog` を呼ぶよう修正。観測されない Task 例外も次回起動時のクラッシュ報告フローに乗るようになった

### 修正
- サムネイル生成中にフォルダ切り替えやアプリ終了を行うと `System.ObjectDisposedException` → `UnobservedTaskException` でクラッシュする問題を修正
  - `CancellationTokenSource` を `Dispose()` した後、LINQ `Select` の遅延評価で `cts.Token` にアクセスし `ObjectDisposedException` が発生していた
  - `Task.Run` 前にトークンをキャプチャ (`var token = cts.Token`) し、fire-and-forget タスク内に `OperationCanceledException` の catch を追加
  - 全アクティブタスクを `_activeThumbnailTasks`（HashSet）で追跡し、`Dispose()` でセマフォを破棄する前に全タスクの完了を待つよう修正。フォルダ切り替えで旧バッチが上書きされる問題も解消
  - `GenerateThumbnailAsync` の `WaitAsync` / `Release()` に `ObjectDisposedException` キャッチを追加（タイムアウト超過時の安全網）
- `Date/Time Original` タグを持たない写真を読み込むと `MetadataExtractor.MetadataException` でクラッシュする問題を修正 (#142)
  - `ExifSubIfdDirectory` は存在するが撮影日時タグがない JPEG（GPS タグのみ付与された写真等）を開いた際に発生
  - `GetDateTime`（タグ不在時に例外をスロー）を `TryGetDateTime` に変更し、タグ不在時はファイル更新日時（`FileMetadataDirectory.TagFileModifiedDate`）にフォールバックして正常続行するよう修正
- ファイル移動・コピー操作完了時に `RPC_E_WRONG_THREAD (0x8001010E)` でクラッシュする問題を修正 (#137)
  - `ExecuteMoveItemsToFolderAsync` / `ExecuteCopyItemsToFolderAsync` の `finally` ブロックが `ConfigureAwait(false)` によりバックグラウンドスレッドで実行され、`IsMoveInProgress` / `IsCopyInProgress` の `PropertyChanged` が UI スレッド外から発火していた
  - UI 状態の更新（プロパティ変更・`RaiseCanExecuteChanged`）を `RunOnUIThreadAsync` でラップし、UI スレッドへ marshal するよう修正

## [1.8.4] - 2026-05-28

### リファクタリング
- `FileSystemWatcher` 監視ロジックを `FileBrowserPaneViewModel` から `FolderWatcherService` へ移動 (#132)
  - `IFolderWatcherService` インターフェースを導入し、ViewModel の `System.IO` 直接依存を排除
  - デバウンスを `System.Threading.Timer` ベースに変更し WinUI 非依存に
  - コンストラクタ引数でインターバルを差し替え可能にし、単体テストに対応

### 追加
- ファイルブラウザに `FileSystemWatcher` による外部変更検知を追加 (#129)
  - 外部アプリ（エクスプローラー・CLI 等）がフォルダ内のファイルを追加・削除・リネームした場合に自動でリストを更新
  - 短時間の大量イベントをデバウンス（500ms）でまとめてリフレッシュ
  - ネットワークドライブ等で `FileSystemWatcher` が利用できない場合は 60 秒間隔のポーリングにフォールバック
  - バッファオーバーフロー等のエラー時に Watcher を自動再設定
  - 移動・コピー処理進行中はリフレッシュを抑制し、操作完了後の更新と競合しない設計

### 修正
- ファイル移動・コピー操作で競合スキップが発生した際にファイル一覧が自動更新されない問題を修正 (#128)
  - `ExecuteMoveItemsToFolderAsync` / `ExecuteCopyItemsToFolderAsync` のリフレッシュ条件を `SuccessCount > 0` から `SuccessCount > 0 || SkipCount > 0` に変更
  - 競合ダイアログで「スキップ」「すべてスキップ」を選んだ後もリストが最新状態に更新されるようになった
- Partner Center 自動提出スクリプトが `Set-StrictMode -Version Latest` 環境でプロパティ不在エラーになる問題を修正
  - `pendingApplicationSubmission` / `minimumDirectXVersion` / `minimumSystemRam` の存在確認を `PSObject.Properties` 経由に変更
- `Submit-ToPartnerCenter.ps1`: HTTP レスポンスなし（接続失敗等）の例外処理で `?.StatusCode.value__` が StrictMode エラーになる問題を修正（`?.StatusCode?.value__` に変更）
- `release.yml`: `generate_release_notes: true` が既存リリースへの再実行時にノートを重複付加する問題を修正
  - `CHANGELOG.md` の該当バージョンセクションをリリースノートとして使用するよう変更し、再実行しても重複しない設計に変更
- `release.yml`: CHANGELOG 抽出ステップで `Add-Content` が Windows 上で CRLF を付加し `GITHUB_OUTPUT` のパースが不安定になる問題を修正
  - `[IO.File]::AppendAllText` + 明示的 LF に切り替え、`$notes` 内の CRLF を LF へ正規化

## [1.8.3] - 2026-05-27

### 追加
- クラッシュレポート送信ダイアログを追加（#123）
  - バナーの「フォルダを開く」を「報告する」ボタンに変更。クリックでダイアログを表示。
  - 「GitHub で報告する」: クラッシュログを埋め込んだ Issue 作成ページをブラウザで開く。
  - 「メールで報告する」: ログ全文をクリップボードにコピーし、標準メールアプリを `mailto:photogeoexplorer@outlook.com` で起動（件名・本文案内付き）。
  - ダイアログ内に「ログフォルダーを開く」リンクを設置。
- Partner Center へのストア提出を CI から自動化（#121）
  - `scripts/Submit-ToPartnerCenter.ps1`: MSIX/appxupload API（`manage.devcenter.microsoft.com/v1.0/my/`）を使いリリースビルドを自動提出。
  - `release.yml` に `Submit to Partner Center` ステップを追加し、タグプッシュ後に自動実行。
  - listing data CSV からリリースノート・説明文をマッピングして更新。パッケージは旧版を `PendingDelete` にしつつ新版を `PendingUpload` として追加。
  - `-DryRun` フラグで API 呼び出しなしのプレビュー表示に対応。保留中申請が存在する場合は `-DeletePending` 明示なしで失敗終了（安全側設計）。

## [1.8.2] - 2026-05-27

### 追加
- クラッシュレポート基盤を実装（#118）
  - `CrashReportService` を新規追加: `running.lock` による異常終了検出・`CrashReports/crash_<timestamp>.log` へのクラッシュログ保存・パス/UNCパスのマスク処理。
  - 未処理例外（UI スレッド例外・AppDomain 例外）発生時にクラッシュログを自動保存。
  - 次回起動時に前回異常終了を検出した場合、InfoBar バナーで通知し「クラッシュログフォルダーを開く」ボタンを表示。
  - 自動送信なし・ローカル収集のみ。

### 修正
- クラッシュレポートバナーの `x:Uid` 命名衝突による起動クラッシュを修正（#120）
  - `InfoBar` の `x:Uid="CrashReportBanner"` と子 `Button` の `x:Uid="CrashReportBanner.OpenFolderButton"` が WinUI 3 のプレフィックス走査で衝突し `XamlParseException` が発生していた問題を解消。
  - `Button` の `x:Uid` を `CrashReportOpenFolderButton` に変更し、両リソースファイルのキーも合わせて更新。
- EXIF編集ボタン連打による `ContentDialog` 二重表示クラッシュを修正（#116）
  - `RelayCommand` / `RelayCommand<T>` に `_isExecuting` フラグを追加し、非同期実行中は `CanExecute = false` を返すよう変更。
  - `finally` での `RaiseCanExecuteChanged()` を `DispatcherQueue.TryEnqueue` 経由で UI スレッドから通知するよう修正。
  - WinUI 3 の「同時に ContentDialog は1つのみ」制約への対処であり、EXIF編集以外の全非同期コマンドにも適用される。

## [1.8.1] - 2026-05-25

### 修正
- フォルダ遷移の繰り返しでクラッシュする問題を修正
  - WinRT マーシャリングで `StackTrace` が null になる問題を回避するため `Exception.ToString()` でログ記録。
  - `OnUnhandledException` で `StackTrace` が null の例外（WinRT マーシャリング由来）のみ継続し、C# 側の例外はクラッシュさせて診断可能性を維持。
  - `LoadFolderAsync` に汎用キャッチを追加し、予期せぬ例外時に一覧クリアとエラー表示で UI 状態を整合。
- 右クリックで複数選択が単数になる問題を修正
  - `ViewModel.SelectedItem = item` のセットが TwoWay バインディング経由で `listView.SelectedItems` を単数に上書きしていた原因を特定。
  - `item` を `listView.SelectedItems.Add()` の先頭に追加することで TwoWay による SelectedItem の再セットを no-op にし上書きを防止。
  - ViewModel に `BeginBatchSelectionUpdate()` / `EndBatchSelectionUpdate()` を追加し、一括操作中の `OnSelectedItemChanged` 副作用を抑制。

## [1.8.0] - 2026-05-24

### 修正
- Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+A がファイルリストで動作しない問題を修正（#110）
  - WinUI 3 の `ListView` が `Ctrl+C` 等を内部でハンドルするため、XAML `KeyDown` では届かなかった。
  - `Loaded` で `AddHandler(KeyDownEvent, handledEventsToo: true)` に変更。
- SHIFT+クリック複数選択後に右クリックで単数になる問題を修正（#110）
  - `PointerPressed` に `AddHandler(handledEventsToo: true)` を追加し、右ボタン押下時に ListView の内部選択リセットを止める。
- ファイルリストの余白クリックで選択解除・フォーカスが当たらない問題を修正（#110）
  - `Tapped` ハンドラを追加し、アイテム以外の余白タップ時に `SelectedItems.Clear()` + `Focus()` を実行。

### 変更
- ファイル削除をゴミ箱移動に変更（#90）
  - `DeleteItems`（直接削除）を `DeleteItemsAsync`（非同期ゴミ箱移動）に置き換え。
  - `Windows.Storage.StorageFile/StorageFolder.DeleteAsync(StorageDeleteOption.Default)` を使用。
  - ドライブ種別によってゴミ箱を経由しない場合（ネットワーク・リムーバブルドライブ等）は OS の既定挙動に従う。
  - UI 文言を「削除」から「ゴミ箱へ移動」に統一（メニュー・確認ダイアログ・エラーダイアログ）。

### 追加
- Ctrl+ドラッグ（内部コピー）に競合確認・進捗・キャンセル対応を追加（#108）
  - `IFileOperationService.CopyItemsAsync` を追加。競合ダイアログコールバック・`IProgress<int>`・`CancellationToken` に対応。
  - `FileOperationService.CopyItemsAsync` を実装（`MoveItemsAsync` と同パターン: 上書き/スキップ/中止 選択可能）。
  - `FileBrowserPaneViewModel` にコピー進捗フィールド・`IsCopyInProgress`・`CancelCopyCommand`・`CancelCopyVisibility` を追加。
  - `ExecuteCopyItemsToFolderAsync` を `resolveConflictAsync` 対応の非同期版に更新。
  - `ExecutePasteAsync` をコピー貼り付け時も競合ダイアログを表示するよう更新。
  - `ShowCopyConflictDialogAsync` をView 層に追加（上書き/すべて上書き/スキップ/すべてスキップ/中止）。
  - ステータスバーにコピー中止ボタン `CancelCopyButton` を追加。
  - リソースファイルに `Dialog.CopyConflict.*`、`Message.CopyProgress`、`Message.CopyDone`、`CancelCopyButton.Content` を追加。
- Explorer 風のドラッグアウト対応（#89）
  - `DragItemsStarting` で `SetDataProvider(StorageItems)` を追加し、Explorer や他アプリへのドラッグアウトを実現。
  - `RequestedOperation = Copy | Move` に変更して、同一/別ドライブの挙動を Explorer 側に委ねる。
  - `Ctrl+ドラッグ` でコピー、通常ドラッグで移動として扱われるよう内部ドラッグに Ctrl 判定を追加。
  - ドラッグ中にキャプション「コピー」/「移動」を表示して操作フィードバックを明示。
  - 外部アプリへの Move 完了後（`DragItemsCompleted.DropResult == Move`）にリストを自動更新。
- Explorer 風のキーボードショートカット対応（#88）
  - `Ctrl+A`: ファイル一覧を全選択。
  - `Ctrl+C` / `Ctrl+X`: 選択ファイルをコピー / 切り取り対象として記録。
  - `Ctrl+V`: 現在のフォルダへ貼り付け（コピーまたは移動）。
  - `Delete`: 選択ファイルの削除操作を開始。
  - `F2`: 単一選択ファイルのリネーム操作を開始。
  - `Esc`: 選択解除。
  - テキスト入力欄へのフォーカス中はショートカットを妨げない（ファイルリストにフォーカスがある場合のみ動作）。
  - `ViewModel.SetClipboard` / `ExecutePasteAsync` を追加し、ViewModel 単体テスト 5 件を追加。
- ファイル移動操作の強化（#69）
  - 移動先に同名の項目がある場合に、上書き / スキップ / すべて上書き / すべてスキップ / 中止 の 5 択ダイアログを表示。
  - 移動中はステータスバーに「移動中… N/M 件」をリアルタイム表示（300ms 間隔ポーリング）。
  - 移動完了後に「移動完了: 成功 N 件、スキップ M 件、失敗 K 件」をステータスバーに表示。
  - ステータスバー右端に「中止」ボタンを追加。移動中のみ表示し、クリックで `CancellationToken` をキャンセル。
  - `IFileOperationService.MoveItemsAsync` を新規追加（非同期競合コールバック・`IProgress<int>`・`CancellationToken` 対応）。
  - `ConflictResolution` enum を追加（`Overwrite`, `Skip`, `Cancel`, `OverwriteAll`, `SkipAll`）。
  - `FileOperationError.Cancelled` を追加。
  - `FileOperationSummary` に `SkipCount` を追加。
  - `MoveItemsAsync` の単体テスト 5 件を追加。
- Explorer 風の複数選択と右クリック操作（#87）
  - 右クリック選択挙動を Explorer 風に変更（選択済み項目→複数選択を維持 / 未選択項目→その項目のみ選択）。
  - ステータスバーに複数選択時の件数表示（「N 件を選択中」）を追加。複数選択中はステータスバーの GPS アイコンを非表示に統一。
  - 右クリックメニューに「ファイルの場所を開く」「Explorer で開く」「パスをコピー」「Google Maps で開く」「コピー」を追加。
  - `FileOperationService.CopyItems` を追加し、選択ファイルを指定フォルダへコピーする基本機能を実装（上書き確認・進捗は #69 対応）。
  - `FileOperationServiceTests` に `CopyItems` の単体テスト 3 件を追加。

### 修正
- E2E テスト コンテキストメニュー取得のフレーキーを追加修正（#95 継続）。
  - `WaitForListItems` を先頭アイテムだけでなく指定件数すべての BoundingRectangle が有効になるまで待機するよう強化（タイムアウト 5s → 8s）。
  - `WaitForElementClickable` のタイムアウトを 3s → 8s に延長し、CI ランナー描画遅延への耐性を向上。
  - `TryWaitForEditExifMenuItem` の各試行タイムアウトを 3s/2s → 5s/4s に延長。
- E2E ワークフローの Windows App SDK runtime 1.8 インストール手順を安定化（#101）。
  - `windowsappruntimeinstall-x64.exe` を優先インストール方法として採用（winget の `0x80070002` エラー対策）。
  - winget → msix 直接インストールの順でフォールバックする3段構成に変更。
- E2E テスト `ExifEditorSaveAndReopenKeepsCoordinates` のフレーキー修正（#95）。
  - `OpenExifMenuForItemName` の catch 節に `NoClickablePointException` を追加。`listItem.RightClick()` が `NoClickablePointException` を投げると catch されずにテスト失敗していた問題を解消。
  - `TryScrollIntoView` / `WaitForElementClickable` ヘルパーを追加し、右クリック前にリスト項目を可視領域にスクロールして描画完了を待機するよう改善。
  - `listItem.RightClick()` を `RightClickElementCenter(listItem)` に統一し、BoundingRectangle が無効な場合のクリックを回避。
  - `WaitForListItems` に安定化待機を追加。アイテム数が揃った後、先頭アイテムの BoundingRectangle が非ゼロになるまで待機することで CI ランナーの描画遅延に対応。

### リファクタリング
- MVVM 境界違反修正（#92 PR-C）: `MainViewModel` から旧ファイルブラウザ責務を完全削除。
  - `Items`, `BreadcrumbItems`, `CurrentFolderPath`, `SelectedItem`, `SelectedMetadata`, `SelectedPreview` など旧ファイルブラウザ状態フィールドをすべて削除。
  - `LoadFolderAsync`, `NavigateUpAsync`, `NavigateBackAsync`, `NavigateForwardAsync`, `RefreshAsync`, `ToggleSort`, `SelectNext`, `SelectPrevious`, `SelectItemByPath`, `ResetFilters`, `UpdateSelection`, `InitializeAsync`, `OpenHomeAsync` などのメソッドをすべて削除。
  - サムネイル非同期生成インフラ（`StartBackgroundThumbnailGeneration`, `GenerateThumbnailAsync`, タイマー, セマフォ）を削除。
  - ステータスオーバーレイ関連（`StatusTitle/Detail/Symbol/PrimaryAction/SecondaryAction`）を削除。
  - ステータスバー（`StatusBarText`, `StatusBarLocationGlyph/Visibility/Tooltip`）を削除。
  - `FileSystemService` コンストラクタ依存を削除。`IDisposable` を除去。
  - `using System.IO;`, `System.Collections.ObjectModel;`, `System.Linq;`, `Microsoft.UI.Dispatching;`, `Microsoft.UI.Xaml.Media.Imaging;` を削除。
  - `MainViewModelTests` から旧ファイルブラウザのテスト 12 件を削除し、Shell 責務 4 件のみ残存。
  - `SettingsPaneViewModelTests` / `SettingsCoordinatorTests` のコンストラクタ呼び出しを `new MainViewModel(workspaceState)` に更新。
- MVVM 境界違反修正（#92 PR-B）: `MapPaneViewModel` / `PreviewPaneViewModel` の IO 依存・直接サービス呼び出しを移管。
  - `MapPaneViewModel.GetPinPath`（`Path.Combine` によるピン画像パス生成）→ `IMapPaneService.GetPinImagePath` へ移管。
  - `MapPaneViewModel.TryCreatePinStyle` の `File.Exists` → `IMapPaneService.FileExistsAtPath` へ移管。
  - `GetPinPath` private static メソッドを削除し `using System.IO;` を `MapPaneViewModel` から除去。
  - `PreviewPaneViewModel` の `ExifService.GetMetadataAsync` 直呼び出し → `IPreviewPaneService.GetMetadataAsync` 経由へ統一。
  - `PreviewPaneService` に `GetMetadataAsync` を追加し `ExifService` へ委譲。
- MVVM 境界違反修正（#92 PR-A）: `FileBrowserPaneViewModel` から `System.IO` 直接依存を除去。
  - `Directory.GetParent` → `IFileOperationService.GetParentPath` に置き換え（`CanNavigateUp`, `CanMoveToParentSelection`, `NavigateUpAsync`）。
  - `Directory.Exists` → `IFileOperationService.FolderExistsAtPath` に置き換え（`LoadFolderAsync`, `OpenHomeAsync`）。
  - `Path.GetExtension` による JPEG 判定 → `IFileOperationService.IsJpegFile` に移管（`IsJpegFile` プライベートメソッド）。
  - `IFileOperationService` に `FolderExistsAtPath` / `IsJpegFile` を追加し `FileOperationService` に実装。
  - `using System.IO;` を `FileBrowserPaneViewModel.cs` から削除。
  - `FileOperationServiceTests` に `FolderExistsAtPath` / `IsJpegFile` のテストを追加。
- MVVM 境界違反修正（#91）: `FileBrowserPaneView.xaml.cs` から `System.IO` 依存・ファイル操作 `foreach` ループ・パス検証関数（`IsSamePath`/`IsDescendantPath`/`ContainsInvalidFileNameChars`/`NormalizeRename`）を完全撤去。
  - `IFileOperationService` / `FileOperationService` を `Services/` に新設し、フォルダ作成・リネーム・移動・削除の実処理とパス検証ロジックを集約。
  - `FileBrowserPaneViewModel` に `Execute*` メソッド群を追加（`ExecuteCreateFolderAsync`, `ExecuteRenameAsync`, `ExecuteMoveItemsToFolderAsync`, `ExecuteMoveToParentAsync`, `ExecuteDeleteItemsAsync`, `HandleExternalFileDropAsync`）。
  - View は Picker / ダイアログ表示 / D&D イベント受付 / エラー表示に限定。
  - `FileOperationResult`（単発操作）と `FileOperationSummary`（複数件操作）DTO を導入し、成功/失敗詳細を VM → View へ型安全に伝達。
  - `FileOperationServiceTests.cs` を追加し、パス検証・ファイル操作成功・エラー変換（IoError / AlreadyExists / DescendantPath）を網羅。

### 変更
- `SixLabors.ImageSharp` を 3.1.12 → 4.0.0 に更新。v4 はビルド時ライセンス検証（`ValidateLicenseTask`）が必須のため、`Directory.Build.props` に環境変数 `SIXLABORS_LICENSE_KEY` から `$(SixLaborsLicenseKey)` への転写を追加し、CI ワークフロー（ci.yml / e2e.yml / release.yml）に GitHub シークレット `SIXLABORS_LICENSE_KEY` の受け渡しを追加。**このプロジェクトは MIT ライセンスのオープンソースプロジェクトであり SixLabors コミュニティライセンス（無償）の取得対象です。** ライセンスキーは https://licensing.sixlabors.com/ で取得し、GitHub リポジトリの Secrets に `SIXLABORS_LICENSE_KEY` として設定してください。

### ドキュメント
- `docs/refactor_plan_v1.8/phase1_ideal_architecture.md` と `docs/refactor_plan_v1.8/phase2_repository_audit.md` を追加し、v1.8 リファクタリング計画の Phase 1 / Phase 2 文書を整備。
- 上記リファクタリング計画書をブラッシュアップし、例外禁止ポリシー、アンチパターン定義、禁止判断、テスト起点監査、優先順位付けルール、Issue 分解ヒントを追加。
- `docs/refactor_plan_v1.8/phase2_audit_result.md` を追加し、Phase 2 の実監査結果を根拠コード・重大度・優先順位・ViewModel スコアつきで整理。
- `docs/refactor_plan_v1.8/phase2_audit_result.md` の Phase 3 接続を見直し、A-01 の Issue 粒度をユースケース単位へ細分化し、A-03 を優先度前倒し、Application Service / Infrastructure Service 観点の Issue 種別を追加。
- `AgentGuidelineSource.md` に MVVM 責務ガードレールを追加（View/ViewModel/Service 各層の記述可否ルール・判断基準・コード例）。
- `AgentGuidelineSource.md` のトークン削減：PR レビュールーティンを `docs/pr-review-routine.md` へ分離、開発時確認サイクルセクション削除、プロジェクト構成の箇条書きを ASCII ツリーに統合。
- ブランチ運用ルールを更新：`main` 直接コミットを完全禁止（ブランチ保護前提）、CHANGELOG・tasks.md の更新を PR 内包含に変更。
- ISSUE #69（ファイル移動操作強化）の実装計画書 `docs/issues/move-operation-enhancement-plan.md` と タスクリスト `docs/issues/move-operation-enhancement-tasks.md` を追加。
- `AGENTS.md` / `CLAUDE.md` / `.github/copilot-instructions.md` を `AgentGuidelineSource.md` と同期。

### 変更
- CI/CD ワークフロー（ci.yml, security-check.yml, e2e.yml, release.yml）の `dotnet-version` 直書きを廃止し、`global-json-file: global.json` を使用して SDK バージョンを `global.json` で一元管理するよう変更。

## [1.7.2] - 2026-03-06

### 修正
- 位置情報のある写真を選択したとき `MapStatusOverlay`（暗いオーバーレイ）が消えずにマップ全体にマスクがかかったままになるリグレッションを修正。`_statusVisibility` 初期値を `Collapsed` に変更し、`Map` プロパティ変更時にも `UpdateMapStatusFromViewModel()` を呼ぶよう修正。
- 上記リグレッションの再発防止として、`MapPaneViewModelTests.InitialStateIsCorrect` に `StatusVisibility == Collapsed` の検証を追加し、E2E で `sample.jpg` 選択後に `MapStatusPanel` が非表示になることをアサートするよう修正。
- ファイルビュー(詳細)の位置情報表示に「測位失敗の可能性」状態を追加し、`0,0` など地図で無効扱いの座標は専用アイコンとツールチップで区別表示するよう改善。

## [1.7.1] - 2026-03-04

### 変更
- Renovate の `customManagers`（regex）を追加し、`PhotoGeoExplorer.csproj` / `PhotoGeoExplorer.Tests.csproj` と `docs/archive/PhotoGeoExplorer_plan.md` の `netX.Y-windows10.0.19041.0` 表記で `netX.Y` 部分を追従更新できるように変更。
- `docs/help/index*.html` / `docs/privacy-policy*.html` / `docs/index.html` の配色を `prefers-color-scheme`（light/dark）追随に更新し、テーマ切替時の視認性を改善。
- `scripts/DevInstall.ps1` の既定動作を「ビルド再利用」から「毎回リビルド」へ変更し、ビルド再利用は `-ReuseBuild` 明示時のみ行うように変更。

### 修正
- Store 版などでタイルキャッシュディレクトリ初期化に失敗した場合でも、永続キャッシュなしで地図レイヤーを継続生成するフォールバックを追加し、地図表示不能によるマスク残留を回避。
- フォルダ読み込み初期列挙で JPEG の EXIF を同期解析していた処理を除去し、一覧の初期表示が全件解析待ちでブロックされる回帰を修正（アイコン先行表示 + サムネイル遅延反映の応答性を回復）。
- フォルダ読み込み初期列挙でキャッシュ済みサムネイル確認・解像度取得・サムネイル同期デコードを行わないようにし、詳細列表示時でもアイコン先行表示と遅延反映を維持するよう改善。
- 遅延読み込み時に EXIF メタデータ（撮影日時・位置情報）を一覧アイテムへ段階反映する処理を追加し、詳細列が最後まで空表示のままになる回帰を修正。

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
