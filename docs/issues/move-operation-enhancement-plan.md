# 実装計画書: ファイル移動操作の強化（ISSUE #69）

> 作成日: 2026-03-19
> 対象 ISSUE: [#69 ファイル移動操作の強化](https://github.com/scottlz0310/PhotoGeoExplorer/issues/69)

---

## 1. スコープ

### 対象

| 課題 | 内容 | 本 Issue 対象 |
|---|---|---|
| 課題1 | 上書き確認がエラーダイアログのみ | ✅ |
| 課題2 | 大量ファイル移動時のプログレスフィードバックがない | ✅ |
| 課題3 | キャンセル機能がない | ✅ |
| 課題4 | エラー発生時に残りファイルを中断する | ✅ |
| 追加拡張 | 最近使用したフォルダへのクイックアクセス | ❌ 別 Issue |
| 削除操作改善 | `DeleteItemsAsync()` の同様改善 | ❌ 別 Issue |
| 1000件以上確認 | 大量ファイル操作前の確認ダイアログ | ❌ 別 Issue |

---

## 2. 設計決定

### 2-1. `ConflictResolution` enum の配置

`FileBrowserPaneViewModel.cs` 内の `internal enum` として定義する。
ViewModel の `ExecuteMoveOperationAsync` が `Func<..., Task<ConflictResolution>>` を受け取るため、View↔ViewModel 境界を跨いで参照できる場所（ViewModel 側）に定義する必要がある。

```csharp
// FileBrowserPaneViewModel.cs 内
internal enum ConflictResolution
{
    None,          // 初期値 — 個別確認モード
    Overwrite,     // 上書き
    OverwriteAll,  // すべて上書き
    Skip,          // スキップ
    SkipAll,       // すべてスキップ
    Cancel,        // キャンセル
}
```

### 2-2. ViewModel への進捗通知方法

ViewModel に3つのメソッドを追加し、View から呼び出す。

| メソッド | 責務 |
|---|---|
| `StartMoveOperation(int total): CancellationToken` | CTS 初期化・タイマー開始・CancellationToken 返却 |
| `IncrementMoveProgress()` | 完了カウンタを `Interlocked.Increment` でスレッドセーフ更新 |
| `FinishMoveOperation(int succeeded, int failed, int skipped)` | タイマー停止・サマリーメッセージ表示 |

### 2-3. キャンセル後の `RefreshAsync()` 実行

キャンセル・エラーサマリー後も必ず `RefreshAsync()` を実行し、移動済みファイルを UIに正しく反映する。

### 2-4. View/ViewModel の責務分担方針（設計上の注意点）

#### 現状の設計と MVVM 理想形の差異

`MoveItemsToFolderAsync()` はループ制御・カウント集計・エラー判定などのビジネスロジックを含んでおり、厳格な MVVM 原則では ViewModel 側に置くべき処理です。

| 責務 | 現在の配置 | MVVM の理想 |
|---|---|---|
| ループ制御（foreach） | View | ViewModel |
| succeeded / failed / skipped カウント | View | ViewModel |
| 競合チェックロジック | View | ViewModel |
| エラー時スキップ判断 | View | ViewModel |
| ダイアログ表示 | View | View（正しい） |
| StatusBarText 更新 | ViewModel | ViewModel（正しい） |

#### 本 Issue での方針（Option B: ViewModel がループ制御を担う）

**`MoveItemsToFolderAsync()` のループ制御・カウント集計・キャンセル管理は ViewModel（`ExecuteMoveOperationAsync`）に実装する。**
View はダイアログコールバック（`ShowMoveConflictDialogAsync`）を渡す。

理由：
- MVVM 責務ガードレールの原則（ViewModel の単体テスト可能性）に準拠する
- `ConflictResolution` を ViewModel 内 `internal enum` に定義できるため、View↔ViewModel 境界で型の参照が成立する
- PR-D でリファクタリング（ループ移転）と ISSUE #69 機能追加を同時に実施する

#### 将来の対応（別 Issue 推奨）

> 削除・リネーム操作のループ制御も同様に ViewModel 層または Service 層に移譲する MVVM リファクタリング

---

## 3. アーキテクチャ概要

```
FileBrowserPaneView.xaml.cs（View - UI/IO層）
  ├─ MoveItemsToFolderAsync()             ← 大幅改修
  │    └─ ViewModel.ExecuteMoveOperationAsync(   ← 新規
  │           targetPath, items,
  │           ShowMoveConflictDialogAsync)       ← UIコールバック渡し
  └─ ShowMoveConflictDialogAsync()        ← 新規追加

FileBrowserPaneViewModel.cs（ViewModel - 状態管理層）
  ├─ ConflictResolution [internal enum]   ← 新規追加
  ├─ _moveCts: CancellationTokenSource?   ← 新規フィールド
  ├─ _moveTotal: int                      ← 新規フィールド
  ├─ _moveCompleted: int                  ← 新規フィールド
  ├─ _moveUpdateTimer: DispatcherQueueTimer? ← 新規フィールド
  ├─ ExecuteMoveOperationAsync(targetPath, items, confirmCallback) ← 新規
  │    ├─ StartMoveOperation(total)
  │    ├─ foreach ループ
  │    │   ├─ token.ThrowIfCancellationRequested()
  │    │   ├─ 競合チェック → confirmCallback()  ← View コールバック呼び出し
  │    │   ├─ ファイル移動（上書き/スキップ対応）
  │    │   └─ IncrementMoveProgress()
  │    ├─ FinishMoveOperation(succeeded, failed, skipped)
  │    └─ RefreshAsync()
  ├─ StartMoveOperation(total): CancellationToken ← 新規メソッド
  ├─ IncrementMoveProgress()              ← 新規メソッド
  └─ FinishMoveOperation(succeeded, failed, skipped) ← 新規メソッド
```

---

## 4. コード変更詳細

### 4-1. `FileBrowserPaneViewModel.cs` への追加

#### 新規フィールド（既存フィールド群の末尾に追加）

```csharp
// Move 操作管理
private CancellationTokenSource? _moveCts;
private int _moveTotal;
private int _moveCompleted;
private DispatcherQueueTimer? _moveUpdateTimer;
```

#### `StartMoveOperation(int total): CancellationToken`

```csharp
internal CancellationToken StartMoveOperation(int total)
{
    // 既存処理をキャンセル
    var previousCts = _moveCts;
    var cts = new CancellationTokenSource();
    _moveCts = cts;
    if (previousCts is not null)
    {
        previousCts.CancelAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        previousCts.Dispose();
    }

    _moveTotal = total;
    _moveCompleted = 0;

    // タイマー開始（_thumbnailUpdateTimer パターンと同様）
    if (_dispatcherQueue is not null)
    {
        _moveUpdateTimer = _dispatcherQueue.CreateTimer();
        _moveUpdateTimer.Interval = TimeSpan.FromMilliseconds(300);
        _moveUpdateTimer.Tick += OnMoveUpdateTimerTick;
        _moveUpdateTimer.Start();
    }

    return cts.Token;
}
```

#### `IncrementMoveProgress()`

```csharp
internal void IncrementMoveProgress()
{
    Interlocked.Increment(ref _moveCompleted);
}
```

#### `FinishMoveOperation(int succeeded, int failed, int skipped)`

```csharp
internal void FinishMoveOperation(int succeeded, int failed, int skipped)
{
    // タイマー停止
    if (_moveUpdateTimer is not null)
    {
        _moveUpdateTimer.Stop();
        _moveUpdateTimer.Tick -= OnMoveUpdateTimerTick;
        _moveUpdateTimer = null;
    }
    _moveCts?.Dispose();
    _moveCts = null;

    // 最終サマリーをステータスバーに表示
    var summary = BuildMoveSummary(succeeded, failed, skipped);
    StatusBarText = summary;
    AppLog.Info($"Move operation completed: {succeeded} succeeded, {failed} failed, {skipped} skipped.");
}
```

#### `OnMoveUpdateTimerTick()`（タイマーイベント、UI スレッドで実行）

```csharp
private void OnMoveUpdateTimerTick(DispatcherQueueTimer sender, object args)
{
    var completed = Volatile.Read(ref _moveCompleted);
    var total = _moveTotal;
    // 例: 「5 / 120 ファイルを移動中...」
    StatusBarText = LocalizationService.Format("Status.MovingFiles", completed, total);
}
```

#### `BuildMoveSummary()` ヘルパー

```csharp
private static string BuildMoveSummary(int succeeded, int failed, int skipped)
{
    // ローカライズ済み文字列でサマリー構築
    // 例: 「移動完了: 10 件成功 / 2 件失敗 / 3 件スキップ」
    return LocalizationService.Format("Status.MoveCompleted", succeeded, failed, skipped);
}
```

---

### 4-2. `FileBrowserPaneView.xaml.cs` への追加・変更

#### `ConflictResolution` enum（`FileBrowserPaneViewModel.cs` に定義済み - View への追加不要）

`ConflictResolution` は ViewModel の `internal enum` として定義するため、View 側への追加は不要です。
View は `FileBrowserPaneViewModel.ConflictResolution` をそのまま参照できます（同一アセンブリ内の `internal` 型）。

#### `ShowMoveConflictDialogAsync()` 新規メソッド

- `ContentDialog` を使用（既存の `ShowConfirmationDialogAsync()` パターンを参考）
- 単一ファイル時: 「上書き」「スキップ」「キャンセル」
- 複数ファイル時: 「上書き」「すべて上書き」「スキップ」「すべてスキップ」「キャンセル」
- `EnsureXamlRootAsync()` で XamlRoot 待機（既存パターン）

```csharp
private async Task<ConflictResolution> ShowMoveConflictDialogAsync(
    string fileName,
    bool hasRemainingItems)
{
    await EnsureXamlRootAsync().ConfigureAwait(true);

    var contentDialog = new ContentDialog
    {
        Title = LocalizationService.GetString("Dialog.FileConflict.Title"),
        Content = new TextBlock
        {
            Text = LocalizationService.Format("Dialog.FileConflict.Detail", fileName),
            TextWrapping = TextWrapping.Wrap,
        },
        PrimaryButtonText = LocalizationService.GetString("Dialog.FileConflict.Overwrite"),
        SecondaryButtonText = LocalizationService.GetString("Dialog.FileConflict.Skip"),
        CloseButtonText = LocalizationService.GetString("Dialog.FileConflict.Cancel"),
        DefaultButton = ContentDialogButton.Secondary,
        XamlRoot = XamlRoot,
    };

    if (hasRemainingItems)
    {
        // 「すべて〜」ボタンを ContentDialog の追加コントロールで実装
        // StackPanel に ToggleButton または RadioButton を配置する方式
        // ※ ContentDialog は Primary / Secondary / Close の3択のみのため、
        //   「すべて上書き」「すべてスキップ」はカスタムコンテンツで実現
    }

    var result = await contentDialog.ShowAsync().ConfigureAwait(true);
    return result switch
    {
        ContentDialogResult.Primary => ConflictResolution.Overwrite,
        ContentDialogResult.Secondary => ConflictResolution.Skip,
        _ => ConflictResolution.Cancel,
    };
}
```

> **実装上の注意**: `ContentDialog` は Primary/Secondary/Close の3択のみネイティブサポート。
> 「すべて上書き」「すべてスキップ」は ContentDialog のカスタムコンテンツ（`CheckBox` 等）で実現するか、
> `ContentDialog` を継承したカスタムダイアログクラスを作成する。

#### `MoveItemsToFolderAsync()` 大幅改修

```csharp
private async Task MoveItemsToFolderAsync(string targetFolderPath, IReadOnlyList<PhotoListItem> items)
{
    // 1. ViewModel からキャンセルトークン取得・プログレス開始
    var token = ViewModel.StartMoveOperation(items.Count);

    int succeeded = 0, failed = 0, skipped = 0;
    var bulkResolution = ConflictResolution.None; // None は「個別確認」モード

    try
    {
        foreach (var item in items)
        {
            // 2. キャンセル判定（現在処理中のファイルを完了させてから停止）
            token.ThrowIfCancellationRequested();

            // 3. パス検証（既存ロジック）
            // ...

            // 4. 競合チェック
            var targetPath = Path.Combine(targetFolderPath, Path.GetFileName(item.FullPath));
            bool shouldOverwrite = false;
            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                ConflictResolution resolution;
                if (bulkResolution is ConflictResolution.OverwriteAll)
                {
                    resolution = ConflictResolution.Overwrite;
                }
                else if (bulkResolution is ConflictResolution.SkipAll)
                {
                    resolution = ConflictResolution.Skip;
                }
                else
                {
                    bool hasRemaining = items.Count > 1;
                    resolution = await ShowMoveConflictDialogAsync(
                        Path.GetFileName(item.FullPath), hasRemaining).ConfigureAwait(true);
                    if (resolution is ConflictResolution.OverwriteAll or ConflictResolution.SkipAll)
                    {
                        bulkResolution = resolution;
                    }
                }

                if (resolution is ConflictResolution.Cancel)
                {
                    skipped += items.Count - succeeded - failed - skipped; // 残り全件をスキップカウント
                    break;
                }
                if (resolution is ConflictResolution.Skip or ConflictResolution.SkipAll)
                {
                    skipped++;
                    ViewModel.IncrementMoveProgress();
                    continue;
                }
                // Overwrite / OverwriteAll: overwrite=true で続行
                shouldOverwrite = true;
            }

            // 5. 移動実行
            try
            {
                if (Directory.Exists(item.FullPath))
                    Directory.Move(item.FullPath, targetPath);
                else
                    File.Move(item.FullPath, targetPath, overwrite: shouldOverwrite);
                succeeded++;
            }
            catch (IOException ex)
            {
                AppLog.Error($"Failed to move item: {item.FullPath}", ex);
                failed++;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Error($"Failed to move item: {item.FullPath}", ex);
                failed++;
            }
            // ... 他の例外

            ViewModel.IncrementMoveProgress();
        }
    }
    catch (OperationCanceledException)
    {
        AppLog.Info($"Move operation cancelled. {succeeded} succeeded so far.");
        skipped += items.Count - succeeded - failed - skipped;
    }
    finally
    {
        // 6. 完了処理（キャンセル時も実行）
        ViewModel.FinishMoveOperation(succeeded, failed, skipped);
        await ViewModel.RefreshAsync().ConfigureAwait(true);
    }
}
```

---

## 5. 文字列リソース追加

対象ファイル: `PhotoGeoExplorer/Strings/` 以下の各 `.resw` ファイル

| キー | 日本語 | 用途 |
|---|---|---|
| `Dialog.FileConflict.Title` | `同名ファイルが存在します` | 競合ダイアログタイトル |
| `Dialog.FileConflict.Detail` | `{0} は移動先に既に存在します。どうしますか？` | 競合ダイアログ本文 |
| `Dialog.FileConflict.Overwrite` | `上書き` | 上書きボタン |
| `Dialog.FileConflict.OverwriteAll` | `すべて上書き` | すべて上書きボタン |
| `Dialog.FileConflict.Skip` | `スキップ` | スキップボタン |
| `Dialog.FileConflict.SkipAll` | `すべてスキップ` | すべてスキップボタン |
| `Dialog.FileConflict.Cancel` | `キャンセル` | キャンセルボタン |
| `Status.MovingFiles` | `{0} / {1} ファイルを移動中...` | 進捗ステータスバー |
| `Status.MoveCompleted` | `移動完了: {0} 件成功 / {1} 件失敗 / {2} 件スキップ` | 完了サマリー |
| `Status.MoveCancelled` | `移動キャンセル: {0} 件成功 / {1} 件スキップ` | キャンセルサマリー |

---

## 6. テスト追加

### `FileBrowserPaneViewModelTests.cs` への追加テスト

| テストケース | 検証内容 |
|---|---|
| `StartMoveOperation_ReturnsValidToken` | CancellationToken が有効であること |
| `StartMoveOperation_CancelsPreviousOperation` | 既存操作がキャンセルされること |
| `IncrementMoveProgress_ThreadSafe` | 並行呼び出しで正確にカウントアップされること |
| `FinishMoveOperation_StopsTimer` | タイマーが停止すること |
| `FinishMoveOperation_UpdatesStatusBar` | サマリーがステータスバーに反映されること |

---

## 7. 実装上の注意点

| 項目 | 内容 |
|---|---|
| ContentDialog 3択超え | Primary/Secondary/Close + カスタムコンテンツ（CheckBox）で「すべて上書き」「すべてスキップ」を実現する |
| スレッドセーフ | `_moveCompleted` の更新は必ず `Interlocked.Increment` を使用 |
| タイマーリーク | `FinishMoveOperation()` で必ずタイマーを停止し Tick ハンドラを解除する |
| OperationCanceledException | `catch (OperationCanceledException)` を `MoveItemsToFolderAsync` 内に追加し、正常系として処理する |
| XamlRoot 待機 | ダイアログ表示前に既存の `EnsureXamlRootAsync()` を呼び出す |
| 上書き時の `File.Move` | `File.Move(src, dst, overwrite: true)` を使用（第3引数に `true` を渡す） |
| `Directory.Move` の上書き | `Directory.Move` は上書き不可のため、競合チェックで `Directory.Exists` 時はスキップ扱いとする（または別のエラー扱いする） |

### Directory.Move の上書き制限への対応

> ⚠️ **重要**: `System.IO.Directory.Move()` は移動先が既に存在すると例外を投げるため、
> フォルダ同士の上書き（マージ）は本実装ではサポートしません。
> フォルダ競合時は「スキップ」扱いとし、ダイアログでは「フォルダは上書きできません。スキップします。」と表示する方式を検討します。

---

## 8. 影響範囲

| ファイル | 変更種別 | 内容 |
|---|---|---|
| `FileBrowserPaneView.xaml.cs` | 改修 | `MoveItemsToFolderAsync()` 大幅改修、`ShowMoveConflictDialogAsync()` 追加、`ConflictResolution` enum 追加 |
| `FileBrowserPaneViewModel.cs` | 追加 | `_moveCts` / `_moveTotal` / `_moveCompleted` / `_moveUpdateTimer` フィールド、3メソッド追加 |
| `Strings/*.resw` | 追加 | 文字列リソース10件追加 |
| `FileBrowserPaneViewModelTests.cs` | 追加 | Move 操作関連テスト5件追加 |

---

## 9. リスクと対策

| リスク | 対策 |
|---|---|
| ContentDialog で5択以上が実現できない | カスタムコンテンツ（CheckBox）でボタン数を3択に抑え、「すべて〜」はチェックボックスで選択させる |
| DispatcherQueue 取得タイミング（テスト環境） | `_dispatcherQueue is null` の場合はタイマーをスキップ（既存サムネイルパターンと同様） |
| フォルダ移動（Directory.Move）の上書き | フォルダ競合はスキップ扱いとして一貫 |
| 長大な MoveItemsToFolderAsync() | リファクタリングで `ExecuteMoveItemAsync()` ヘルパーを抽出することを検討 |
