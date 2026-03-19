# tasks.md: FileBrowserPane MVVM リファクタリング ＋ ISSUE #69 機能追加

> 背景 ISSUE: [#69 ファイル移動操作の強化](https://github.com/scottlz0310/PhotoGeoExplorer/issues/69)
> 実装計画書: [move-operation-enhancement-plan.md](./move-operation-enhancement-plan.md)
>
> **方針**: `FileBrowserPaneView.xaml.cs` にあるビジネスロジックを ViewModel / Service 層へ段階的に移行し、
> ISSUE #69 の機能追加（上書き確認・プログレス・キャンセル・スキップ）は PR-D で統合する。
> 各 PR は独立してマージ可能。

---

## PR-A: パスユーティリティを Service 層へ移行

> ブランチ: `refactor/filebrowser-path-utilities`
> 規模: 小〜中 / 手戻りリスク: 低 / テスト追加: しやすい（純粋関数）

### 実装

- [ ] **A-1** `FileBrowserPaneService`（または `FileOperationService`）へ以下の static メソッドを移行
  - `IsSamePath(string, string): bool`（View L1025）
  - `IsDescendantPath(string, string): bool`（View L1035）
  - `ContainsInvalidFileNameChars(string): bool`（View L1158）
  - `NormalizeRename(string, string): string`（View L1163）
  - `BuildDeleteMessage(IReadOnlyList<PhotoListItem>): string`（View L1110）
- [ ] **A-2** View 側の該当コードをサービス呼び出しに置換
- [ ] **A-3** 既存呼び出し箇所がすべてコンパイルエラーなしで動作することを確認

### テスト

- [ ] **A-4** `FileBrowserPaneServiceTests.cs` に以下のテストを追加
  - `IsSamePath_SamePathReturnsTrue`
  - `IsSamePath_DifferentPathReturnsFalse`
  - `IsDescendantPath_ChildPathReturnsTrue`
  - `IsDescendantPath_UnrelatedPathReturnsFalse`
  - `ContainsInvalidFileNameChars_WithInvalidChar_ReturnsTrue`
  - `ContainsInvalidFileNameChars_ValidName_ReturnsFalse`
  - `NormalizeRename_PreservesExtension`
  - `NormalizeRename_AddsExtensionWhenMissing`
  - `BuildDeleteMessage_SingleFile`
  - `BuildDeleteMessage_MultipleFiles`

### 品質確認・PR

- [ ] **A-5** `dotnet format PhotoGeoExplorer.sln`
- [ ] **A-6** `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64`（警告ゼロ）
- [ ] **A-7** `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`（既存テスト全通過）
- [ ] **A-8** `CHANGELOG.md` を更新
- [ ] **A-9** PR 作成・Copilot 自動レビュー対応

---

## PR-B: フォルダ作成操作を ViewModel+Service へ移行

> ブランチ: `refactor/filebrowser-create-folder`
> 前提: PR-A マージ済み
> 規模: 小〜中

### 実装

- [ ] **B-1** `FileBrowserPaneService` に `CreateFolderAsync(string parentPath, string folderName): Task<bool>` を実装
  - `Directory.CreateDirectory()` のラップ
  - 例外処理（`UnauthorizedAccessException`, `IOException` 等）を含む
- [ ] **B-2** `FileBrowserPaneViewModel` に `ExecuteCreateFolderAsync(string folderName): Task` を実装
  - Service の `CreateFolderAsync()` を呼び出す
  - 成功・失敗を `StatusBarText` に反映
- [ ] **B-3** View の `CreateFolderAsyncCore()`（L591）を ViewModel 呼び出しに縮小（ダイアログ表示のみ残す）

### テスト

- [ ] **B-4** `FileBrowserPaneServiceTests.cs` に追加
  - `CreateFolderAsync_Success`
  - `CreateFolderAsync_AlreadyExists_ReturnsFalse`
  - `CreateFolderAsync_Unauthorized_ReturnsFalse`
- [ ] **B-5** `FileBrowserPaneViewModelTests.cs` に追加
  - `ExecuteCreateFolderAsync_CallsService`
  - `ExecuteCreateFolderAsync_UpdatesStatusBarOnSuccess`

### 品質確認・PR

- [ ] **B-6** フォーマット・ビルド・テスト確認
- [ ] **B-7** `CHANGELOG.md` を更新
- [ ] **B-8** PR 作成・Copilot 自動レビュー対応

---

## PR-C: リネーム操作を ViewModel+Service へ移行

> ブランチ: `refactor/filebrowser-rename`
> 前提: PR-A, PR-B マージ済み
> 規模: 中

### 実装

- [ ] **C-1** `FileBrowserPaneService` に `RenameItemAsync(string sourcePath, string newName): Task<bool>` を実装
  - `File.Move()` / `Directory.Move()` のラップ
  - パス検証（PR-A で移行した `ContainsInvalidFileNameChars`, `NormalizeRename` を使用）
- [ ] **C-2** `FileBrowserPaneViewModel` に `ExecuteRenameAsync(PhotoListItem item, string newName): Task` を実装
- [ ] **C-3** View の `RenameSelectionAsyncCore()`（L639）をダイアログ表示 + ViewModel 委譲に縮小

### テスト

- [ ] **C-4** `FileBrowserPaneServiceTests.cs` に追加
  - `RenameItemAsync_File_Success`
  - `RenameItemAsync_Directory_Success`
  - `RenameItemAsync_InvalidName_ReturnsFalse`
  - `RenameItemAsync_TargetExists_ReturnsFalse`
- [ ] **C-5** `FileBrowserPaneViewModelTests.cs` に追加
  - `ExecuteRenameAsync_CallsService`
  - `ExecuteRenameAsync_UpdatesStatusBarOnFailure`

### 品質確認・PR

- [ ] **C-6** フォーマット・ビルド・テスト確認
- [ ] **C-7** `CHANGELOG.md` を更新
- [ ] **C-8** PR 作成・Copilot 自動レビュー対応

---

## PR-D: 移動操作リファクタリング ＋ ISSUE #69 機能追加（統合）

> ブランチ: `feature/issue-69-move-enhancement`
> 前提: PR-A, PR-B, PR-C マージ済み
> 規模: 大 / **ISSUE #69 の受け入れ条件を満たす**

### 文字列リソース追加

- [ ] **D-1** `Strings/ja-JP/Resources.resw` に以下のキーを追加
  - `Dialog.FileConflict.Title` / `Dialog.FileConflict.Detail`
  - `Dialog.FileConflict.Overwrite` / `Dialog.FileConflict.OverwriteAll`
  - `Dialog.FileConflict.Skip` / `Dialog.FileConflict.SkipAll`
  - `Dialog.FileConflict.Cancel`
  - `Status.MovingFiles` / `Status.MoveCompleted` / `Status.MoveCancelled`
- [ ] **D-2** 英語ロケール `Resources.resw` にも同キーを追加

### Service 層の実装

- [ ] **D-3** `FileBrowserPaneService` に `MoveItemAsync(string sourcePath, string targetPath, bool overwrite): Task<MoveItemResult>` を実装
  - `File.Move(src, dst, overwrite)` / `Directory.Move()` のラップ
  - 戻り値: `Succeeded` / `Skipped` / `Failed` を表す enum または結果型
  - 例外処理（`IOException`, `UnauthorizedAccessException` 等）をキャッチしてログ + `Failed` 返却

### ViewModel 層の実装

- [ ] **D-4** 新規フィールドを追加
  - `_moveCts: CancellationTokenSource?`
  - `_moveTotal: int` / `_moveCompleted: int`
  - `_moveUpdateTimer: DispatcherQueueTimer?`
- [ ] **D-5** `StartMoveOperation(int total): CancellationToken` を実装
  - 既存 CTS のキャンセル・Dispose → 新規 CTS 作成・保存
  - カウンタリセット
  - `DispatcherQueueTimer`（300ms）初期化・開始
- [ ] **D-6** `IncrementMoveProgress()` を実装（`Interlocked.Increment`）
- [ ] **D-7** `FinishMoveOperation(int succeeded, int failed, int skipped)` を実装
  - タイマー停止・ハンドラ解除
  - `StatusBarText` にサマリー設定・`AppLog.Info` 出力
- [ ] **D-8** `OnMoveUpdateTimerTick()` イベントハンドラを実装
  - `StatusBarText` を「N / M ファイルを移動中...」に更新
- [ ] **D-9** `ExecuteMoveOperationAsync(string targetPath, IReadOnlyList<PhotoListItem> items, Func<string, bool, Task<ConflictResolution>> confirmCallback): Task` を実装
  - ループ制御・カウント集計・キャンセル判定を ViewModel が担う
  - 競合発生時は `confirmCallback`（View のダイアログ）に委譲

### View 層の改修

- [ ] **D-10** `ConflictResolution` private enum を追加（`None` / `Overwrite` / `OverwriteAll` / `Skip` / `SkipAll` / `Cancel`）
- [ ] **D-11** `ShowMoveConflictDialogAsync(string fileName, bool hasRemainingItems): Task<ConflictResolution>` を実装
  - `EnsureXamlRootAsync()` で XamlRoot 待機
  - 単一/複数で ContentDialog コンテンツを切り替え
  - 「すべて上書き」「すべてスキップ」は ContentDialog 内の `CheckBox` で実現
- [ ] **D-12** `MoveSelectionAsyncCore()`（L698）をダイアログ + ViewModel 委譲に縮小
  - `PickFolderAsync()` でフォルダ取得
  - `ViewModel.ExecuteMoveOperationAsync(targetPath, items, ShowMoveConflictDialogAsync)` を呼び出す
- [ ] **D-13** View から旧 `MoveItemsToFolderAsync()` を削除

### テスト

- [ ] **D-14** `FileBrowserPaneServiceTests.cs` に追加
  - `MoveItemAsync_File_Success`
  - `MoveItemAsync_FileOverwrite_Success`
  - `MoveItemAsync_Directory_Success`
  - `MoveItemAsync_Locked_ReturnsFailure`
- [ ] **D-15** `FileBrowserPaneViewModelTests.cs` に追加
  - `StartMoveOperation_ReturnsValidToken`
  - `StartMoveOperation_CancelsPreviousOperation`
  - `IncrementMoveProgress_ThreadSafe`
  - `FinishMoveOperation_UpdatesStatusBarText`
  - `ExecuteMoveOperationAsync_SkipsOnConflictWhenCallbackReturnsSkip`
  - `ExecuteMoveOperationAsync_OverwritesWhenCallbackReturnsOverwrite`
  - `ExecuteMoveOperationAsync_StopsOnCancellation`

### 品質確認・PR

- [ ] **D-16** 手動動作確認
  - 競合なし: 通常移動が成功すること
  - 競合あり（単一）: 3択ダイアログが表示されること
  - 競合あり（複数）: 「すべて〜」オプション付きダイアログが表示されること
  - 「すべて上書き」後: 以降ダイアログが再表示されないこと
  - 「すべてスキップ」後: 以降ダイアログが再表示されないこと
  - キャンセル: 残りが処理されず、サマリーが表示されること
  - プログレス: ステータスバーに「N / M ファイルを移動中...」が表示されること
  - 完了後: サマリーが表示されること
  - エラー: スキップされて失敗件数がサマリーに含まれること
- [ ] **D-17** フォーマット・ビルド・テスト確認
- [ ] **D-18** `CHANGELOG.md` を更新（ISSUE #69 受け入れ条件の達成を記載）
- [ ] **D-19** PR 作成・Copilot 自動レビュー対応

---

## PR-E: 削除操作を ViewModel+Service へ移行

> ブランチ: `refactor/filebrowser-delete`
> 前提: PR-D マージ済み（MoveItemAsync と同パターンを踏襲）
> 規模: 中

### 実装

- [ ] **E-1** `FileBrowserPaneService` に `DeleteItemAsync(string path): Task<bool>` を実装
- [ ] **E-2** `FileBrowserPaneViewModel` に `ExecuteDeleteOperationAsync(IReadOnlyList<PhotoListItem> items): Task` を実装
  - エラー時スキップ・サマリー表示（PR-D の MoveOperation と同パターン）
- [ ] **E-3** View の `DeleteSelectionAsyncCore()`（L745）を確認ダイアログ + ViewModel 委譲に縮小
- [ ] **E-4** View から旧 `DeleteItemsAsync()`（L1077）を削除

### テスト

- [ ] **E-5** `FileBrowserPaneServiceTests.cs` に追加
  - `DeleteItemAsync_File_Success`
  - `DeleteItemAsync_Directory_Success`
  - `DeleteItemAsync_NotFound_ReturnsFalse`
- [ ] **E-6** `FileBrowserPaneViewModelTests.cs` に追加
  - `ExecuteDeleteOperationAsync_SkipsErroredItem`
  - `ExecuteDeleteOperationAsync_UpdatesStatusBarWithSummary`

### 品質確認・PR

- [ ] **E-7** フォーマット・ビルド・テスト確認
- [ ] **E-8** `CHANGELOG.md` を更新
- [ ] **E-9** PR 作成・Copilot 自動レビュー対応

---

## PR-F: フィルタ・ステータス・ドロップ処理を ViewModel へ移行

> ブランチ: `refactor/filebrowser-viewmodel-cleanup`
> 前提: PR-E マージ済み
> 規模: 中 / View のクリーンアップ仕上げ

### 実装

- [ ] **F-1** `FocusPhotoItem()` の LINQ 検索ロジック（L245）を ViewModel に移動
- [ ] **F-2** `PerformStatusActionAsync()` の switch 判定ロジック（L311）を ViewModel に移動
- [ ] **F-3** `OnFileListDrop()` のアイテム解析ループ（L454）を ViewModel に移動
  - `StorageItem` 取得は View に残し、解析済みパスリストを ViewModel へ渡す
- [ ] **F-4** `ResetFiltersAsync()` のロジック部分（L179）を ViewModel に移動
- [ ] **F-5** View 側の各呼び出し箇所を ViewModel メソッドの委譲に縮小

### テスト

- [ ] **F-6** `FileBrowserPaneViewModelTests.cs` に追加
  - `PerformStatusAction_OpenFolder_CallsOpenFolderAction`
  - `PerformStatusAction_Refresh_CallsRefreshAsync`
  - `ResetFilters_ClearsSearchText`
  - `ResetFilters_ClearsShowImagesOnly`

### 品質確認・PR

- [ ] **F-7** フォーマット・ビルド（警告ゼロ）・テスト確認
- [ ] **F-8** `CHANGELOG.md` を更新（リファクタリング完了を記載）
- [ ] **F-9** PR 作成・Copilot 自動レビュー対応

---

## 参考資料

- [実装計画書](./move-operation-enhancement-plan.md)
- [ISSUE ドラフト](./move-operation-enhancement.md)
- `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs`
- `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs`
- `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneService.cs`
- `docs/Architecture/PaneSystem.md`
