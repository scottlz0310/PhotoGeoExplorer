> [!NOTE]
> このファイルは GitHub Issue #69 のドラフトです。内容が確定したら Issue 側へ反映してください。

# Issue: ファイル移動操作の強化（上書き確認・プログレス・キャンセル・スキップ）

## 背景

選択ファイルをフォルダへ移動する基本機能（コンテキストメニュー「移動」→フォルダ選択→`File.Move`）はすでに実装済みです。

- コンテキストメニュー: `BuildFileContextFlyout()`（`FileBrowserPaneView.xaml.cs:988`）に「移動」「親フォルダへ移動」が存在
- フォルダ選択: `PickFolderAsync()`（同:1324）に `FolderPicker` 実装済み
- 移動処理: `MoveItemsToFolderAsync()`（同:1127）に `System.IO.File.Move` / `Directory.Move` 実装済み

本 Issue では、現状実装の課題を整理し改善する。

---

## 現状の課題

### 課題 1: 上書き確認がエラーダイアログのみ

移動先に同名ファイルが存在する場合、現状はエラーを表示して中断するのみ。

```csharp
// FileBrowserPaneView.xaml.cs:1167-1173（現状）
if (Directory.Exists(targetPath) || File.Exists(targetPath))
{
    await ShowMessageDialogAsync(
        LocalizationService.GetString("Dialog.AlreadyExists.Title"),
        LocalizationService.GetString("Dialog.AlreadyExistsDestination.Detail")).ConfigureAwait(true);
    return;
}
```

**改善案**: 「上書き」「スキップ」「キャンセル」を選択できる確認ダイアログを表示する。

#### 上書き確認ダイアログ仕様

| 選択肢 | 動作 |
|---|---|
| 上書き | 現在のファイルを上書きして続行 |
| スキップ | 現在のファイルをスキップして続行 |
| キャンセル | 残りのファイルをすべて中止 |

複数ファイル移動時は追加で以下を表示する：

| 選択肢 | 動作 |
|---|---|
| すべて上書き | 以降の競合をすべて上書き |
| すべてスキップ | 以降の競合をすべてスキップ |

ユーザーが「すべて〜」を選択した場合、その設定を以降のループ処理に引き継ぎ、同じダイアログを再表示しない。

---

### 課題 2: 大量ファイル移動時のプログレスフィードバックがない

`MoveItemsToFolderAsync` は単純な `foreach` ループのみで、操作中の進捗が分からない。

**改善案**: サムネイル生成と同様に `DispatcherQueueTimer`（300ms 間隔）でステータスバーを更新する。

```
例: 「5 / 120 ファイルを移動中...」
```

参考実装: `FileBrowserPaneViewModel.cs` の `StartBackgroundThumbnailGeneration()`

#### プログレス設計（責務分担）

- 進捗状態（処理済み件数 / 総件数）は **ViewModel に保持**する（`_moveCompleted` / `_moveTotal` フィールド。`Interlocked.Increment` でスレッドセーフに更新）
- UI の更新は **ViewModel 側の `DispatcherQueueTimer`**（`_moveUpdateTimer`、300ms 間隔）でポーリングして行う（`_thumbnailUpdateTimer` パターンと同様）
- 移動完了後またはキャンセル後はタイマーを停止し、最終サマリーをステータスバーに表示する

---

### 課題 3: キャンセル機能がない

`MoveItemsToFolderAsync` に `CancellationToken` が渡されておらず、長時間操作を途中停止できない。

**改善案**: 既存の `_loadFolderCts` パターンに倣い `CancellationTokenSource` を導入する。

```csharp
// 参考パターン（FileBrowserPaneViewModel.cs の _loadFolderCts）
var previousCts = _moveCts;
var cts = new CancellationTokenSource();
_moveCts = cts;
if (previousCts is not null)
{
    await previousCts.CancelAsync().ConfigureAwait(false);
    previousCts.Dispose();
}
// ...
cts.Token.ThrowIfCancellationRequested();
```

#### キャンセル仕様

- 現在処理中のファイルは完了させてからキャンセルを適用する（ファイルの中断書き込みを防ぐ）
- 次のファイル処理を開始する直前にキャンセル判定を行う
- キャンセル時は途中まで移動済みのファイルをロールバックしない（部分完了を保持）
- キャンセル後はその時点の「N 件成功 / M 件キャンセル」をステータスバーに表示する

---

### 課題 4: エラー発生時に残りファイルを中断する

複数ファイル移動中にエラーが発生すると、残りのファイルを処理せずに `return` する。

**改善案**: エラーが発生したファイルをスキップして続行し、完了後に「N 件成功 / M 件失敗」のサマリーを表示する。削除操作（`DeleteItemsAsync`）も同様の改善が対象。

---

## 追加拡張（別 Issue 化を推奨）

### 最近使用したフォルダへのクイックアクセス

フォルダ選択ダイアログを毎回開かずに、コンテキストメニューから最近使用したフォルダへ直接移動できる。

```
移動 →
    フォルダを選択…        ← 現在の実装
    ──────────────────────
    📁 最近使用したフォルダ1   ← 新規
    📁 最近使用したフォルダ2
```

---

## 技術メモ

- ファイル操作は `System.IO.File.Move` / `Directory.Move` を使用（既存パターン踏襲。`StorageFile.MoveAsync` は不使用）
- `FolderPicker` の hwnd 取得は `HostWindow` 経由で `WindowNative.GetWindowHandle(HostWindow)` を使用（`PickFolderAsync` に実装済み）
- 上書き確認ダイアログ: View 内に既存の `ShowConfirmationDialogAsync`（Primary / Secondary の 2 択）があるが、本機能では 3〜5 択が必要なため新規ダイアログメソッドを追加する
- 主な修正対象: `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs`、`FileBrowserPaneViewModel.cs`

### ログ戦略

- 個々のエラーは `AppLog.Error($"Failed to move item: {sourcePath}", ex)` に記録（既存パターン踏襲）
- 移動完了時に最終サマリー（成功件数・失敗件数・スキップ件数）を `AppLog.Info` で出力する

### パフォーマンス考慮

- 1000 ファイル以上を選択して移動しようとした場合、処理前に確認ダイアログを表示することを検討する（将来的な事故防止。本 Issue のスコープ外）

### ファイルロック対策

- 移動対象ファイルが他プロセスによってロックされている場合（`IOException`）、スキップ対象として扱い、失敗件数に計上する

---

## 受け入れ条件

- [ ] 移動先に同名ファイルが存在する場合、上書き確認ダイアログが表示される
- [ ] 複数ファイル移動時に「すべて上書き」「すべてスキップ」が選択できる
- [ ] 複数ファイル移動中に進捗がステータスバーで確認できる
- [ ] 移動操作をキャンセルできる（現在処理中のファイルは完了させてから停止）
- [ ] 1 ファイルのエラーで全体が中断されず、スキップして続行できる
- [ ] 移動完了後またはキャンセル後に結果サマリーがステータスバーに表示される
- [ ] 既存の削除・移動テストが引き続き通過する
