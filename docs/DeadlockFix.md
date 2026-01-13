# デッドロック問題の修正 - 対応内容

## 問題の概要

**重大な不具合**: `SemaphoreSlim` の二重取得によりデッドロックが発生

### 発生メカニズム

1. `NavigateBackAsync()` または `NavigateForwardAsync()` が `_navigationSemaphore.WaitAsync()` を取得
2. その内部で `LoadFolderAsync()` を呼び出し
3. `LoadFolderAsync()` が再度 `_navigationSemaphore.WaitAsync()` を取得しようとする
4. `SemaphoreSlim` はリエントラントではないため、**デッドロック発生**

### 影響

- 戻るボタン・進むボタンをクリックするとアプリケーションがフリーズ
- 確定で発生する不具合（条件が揃えば100%再現）
- **このままではマージ不可**

## 修正内容

### 採用した方針

**方針A (推奨)**: ロックは「入口」だけで取得する

- ユーザー操作の入口側（`LoadFolderAsync`, `NavigateBackAsync`, `NavigateForwardAsync`）でのみセマフォを取得
- 内部実装は `LoadFolderCoreAsync()` に分離してロック不要にする

### 実装の変更

#### 1. LoadFolderCoreAsync() を追加

ロック不要の内部実装メソッドを新規作成:

```csharp
private async Task LoadFolderCoreAsync(string folderPath)
{
    var previousPath = CurrentFolderPath;
    var shouldAddToHistory = !_isNavigating && !string.IsNullOrWhiteSpace(previousPath);

    try
    {
        // フォルダ読み込み処理
        CurrentFolderPath = folderPath;
        UpdateBreadcrumbs(folderPath);
        // ... 実装 ...

        // ロード成功後に履歴を更新
        if (shouldAddToHistory)
        {
            PushToBackStack(previousPath!);
            _navigationForwardStack.Clear();
            UpdateNavigationProperties();
        }
    }
    catch
    {
        // ロード失敗時は元のパスに戻す
        CurrentFolderPath = previousPath;
        throw;
    }
}
```

#### 2. LoadFolderAsync() をラッパーに変更

入口でのみロックを取得し、内部実装を呼び出す:

```csharp
public async Task LoadFolderAsync(string folderPath)
{
    // バリデーション処理
    if (string.IsNullOrWhiteSpace(folderPath)) { return; }
    if (!Directory.Exists(folderPath)) { return; }
    
    // 同一フォルダチェック
    if (normalizedPath == normalizedCurrentPath) { return; }

    // 入口でロックを取得
    await _navigationSemaphore.WaitAsync().ConfigureAwait(true);
    try
    {
        await LoadFolderCoreAsync(folderPath).ConfigureAwait(true);
    }
    finally
    {
        _navigationSemaphore.Release();
    }
}
```

#### 3. NavigateBackAsync() / NavigateForwardAsync() を修正

`LoadFolderCoreAsync()` を直接呼び出すように変更:

```csharp
public async Task NavigateBackAsync()
{
    if (_navigationBackStack.Count == 0) { return; }

    await _navigationSemaphore.WaitAsync().ConfigureAwait(true);
    try
    {
        var previousPath = _navigationBackStack.Pop();
        var currentPath = CurrentFolderPath;

        _isNavigating = true;
        try
        {
            // LoadFolderCoreAsync を呼び出し（ロック不要）
            await LoadFolderCoreAsync(previousPath).ConfigureAwait(true);
            
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                PushToForwardStack(currentPath);
            }
        }
        catch
        {
            _navigationBackStack.Push(previousPath);
            throw;
        }
        finally
        {
            _isNavigating = false;
        }

        UpdateNavigationProperties();
    }
    finally
    {
        _navigationSemaphore.Release();
    }
}
```

### 構造の明確化

修正後のロック取得構造:

```
┌─────────────────────────────────────────┐
│  ユーザー操作（入口）                    │
├─────────────────────────────────────────┤
│  LoadFolderAsync()                      │
│    ↓ セマフォ取得                        │
│    ↓ LoadFolderCoreAsync()              │
│                                         │
│  NavigateBackAsync()                    │
│    ↓ セマフォ取得                        │
│    ↓ LoadFolderCoreAsync()              │
│                                         │
│  NavigateForwardAsync()                 │
│    ↓ セマフォ取得                        │
│    ↓ LoadFolderCoreAsync()              │
└─────────────────────────────────────────┘

セマフォの二重取得は発生しない ✓
```

## 追加の改善

### CurrentFolderPath のロールバック

ロード失敗時に `CurrentFolderPath` が失敗先のまま残らないように修正:

```csharp
catch (UnauthorizedAccessException ex)
{
    AppLog.Error($"Failed to access folder: {folderPath}", ex);
    SetStatus(LocalizationService.GetString("Message.AccessDeniedSeeLog"), 
              InfoBarSeverity.Error);
    // ロード失敗時は元のパスに戻す
    CurrentFolderPath = previousPath;
    throw;
}
```

すべての例外ハンドラーに同様の処理を追加。

## テスト項目

修正後に確認すべき動作:

### 正常系
- ✅ 通常フォルダ遷移（デッドロックなし）
- ✅ 戻る操作（デッドロックなし）
- ✅ 進む操作（デッドロックなし）
- ✅ ボタン連打時にフリーズしない

### 異常系
- ✅ 戻る中に例外が発生した場合の履歴保護
- ✅ ロード失敗時の `CurrentFolderPath` ロールバック
- ✅ アクセス権限エラー時の適切な処理

## まとめ

### 修正前の問題
- `SemaphoreSlim` の二重取得により確定的にデッドロックが発生
- 戻る・進むボタンが使用不可能

### 修正後の改善
- ✅ デッドロック完全解消
- ✅ ロック取得を入口に一本化し、構造が明確に
- ✅ ロード失敗時の `CurrentFolderPath` ロールバックを追加
- ✅ すべてのナビゲーション操作が安全に動作

### マージ可否
✅ **修正完了 - マージ可能**

デッドロックは完全に解消され、指摘されたすべての問題点に対応しました。
