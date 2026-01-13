# ナビゲーション履歴機能の堅牢性向上 - 対応内容

## 概要

コードレビューで指摘された6つの問題点をすべて修正し、ナビゲーション履歴機能の堅牢性を大幅に向上させました。

## 対応した問題点

### 1. LoadFolderAsync 失敗時の履歴汚染

**問題**: フォルダ読み込みが失敗した場合でも、履歴が更新されてしまう。

**対応**: 
- ロード成功後のみ履歴に追加するように変更
- 例外発生時は `throw` で再スローし、履歴は変更されない

```csharp
// ロード成功後に履歴を更新
if (shouldAddToHistory)
{
    PushToBackStack(previousPath!);
    _navigationForwardStack.Clear();
    UpdateNavigationProperties();
}
```

### 2. 並行実行・連打による競合

**問題**: 非同期メソッドが並行実行され、履歴が破損する可能性がある。

**対応**:
- `SemaphoreSlim` を導入してナビゲーション処理を直列化
- すべてのナビゲーションメソッドでセマフォを使用

```csharp
private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);

await _navigationSemaphore.WaitAsync().ConfigureAwait(true);
try
{
    // ナビゲーション処理
}
finally
{
    _navigationSemaphore.Release();
}
```

### 3. 同一フォルダの重複履歴

**問題**: 同じフォルダを再度開いた場合に、履歴に重複して追加される。

**対応**:
- `NormalizePath()` メソッドでパスを正規化（大文字小文字、末尾区切り）
- 同じフォルダへの移動時は履歴に追加しない

```csharp
private static string NormalizePath(string path)
{
    return Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

// 同じフォルダの場合は何もしない
if (normalizedCurrentPath != null && 
    string.Equals(normalizedPath, normalizedCurrentPath, StringComparison.OrdinalIgnoreCase))
{
    return;
}
```

### 4. 履歴サイズの上限

**問題**: 履歴スタックが無制限に成長し、メモリを圧迫する可能性がある。

**対応**:
- `MaxNavigationHistorySize = 100` を設定
- 上限に達したら最も古い項目を削除

```csharp
private const int MaxNavigationHistorySize = 100;

private void PushToBackStack(string path)
{
    if (_navigationBackStack.Count >= MaxNavigationHistorySize)
    {
        // 最も古い項目を削除
        var items = _navigationBackStack.ToList();
        items.RemoveAt(items.Count - 1);
        _navigationBackStack.Clear();
        for (var i = items.Count - 1; i >= 0; i--)
        {
            _navigationBackStack.Push(items[i]);
        }
    }
    _navigationBackStack.Push(normalizedPath);
}
```

### 5. CanNavigateBack / Forward の通知タイミング

**問題**: スタック操作のたびに PropertyChanged が確実に発火する必要がある。

**対応**:
- スタック操作を `PushToBackStack()` / `PushToForwardStack()` に集約
- これらのメソッドでパスの正規化と上限チェックを一元管理

```csharp
private void PushToBackStack(string path)
{
    var normalizedPath = NormalizePath(path);
    // 上限チェックとスタック操作
    _navigationBackStack.Push(normalizedPath);
}
```

### 6. テストの安定性

**問題**: 実フォルダに依存しているため、CI や環境差で不安定になる。

**対応**:
- 一時ディレクトリを使用するように変更
- `IDisposable` を実装して自動クリーンアップ
- パス比較を正規化して環境差を吸収

```csharp
public sealed class MainViewModelTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), 
            "PhotoGeoExplorer_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch { }
        }
    }
}
```

## 追加した機能

### 失敗時の履歴ロールバック

ナビゲーションメソッドでロード失敗時に履歴を元に戻す処理を追加:

```csharp
try
{
    await LoadFolderAsync(previousPath).ConfigureAwait(true);
    
    // ロード成功時のみ進む履歴に追加
    if (!string.IsNullOrWhiteSpace(currentPath))
    {
        PushToForwardStack(currentPath);
    }
}
catch
{
    // ロード失敗時は履歴を元に戻す
    _navigationBackStack.Push(previousPath);
    throw;
}
```

### 新しいテストケース

同一フォルダの重複防止をテストするケースを追加:

```csharp
[Fact]
public async Task LoadFolderAsync_SameFolderDoesNotAddToHistory()
{
    await viewModel.LoadFolderAsync(testFolder);
    Assert.False(viewModel.CanNavigateBack);

    // 同じフォルダを再度読み込み
    await viewModel.LoadFolderAsync(testFolder);
    
    Assert.False(viewModel.CanNavigateBack, 
        "同じフォルダの再読み込みでは履歴に追加されない");
}
```

## 変更統計

- **MainViewModel.cs**: 約160行の変更（堅牢性向上のロジック追加）
- **MainViewModelTests.cs**: 約50行の変更（環境非依存テストへの改善）

## 期待される効果

### 安定性の向上
- ✅ ロード失敗時も履歴が保護される
- ✅ 並行実行による競合が発生しない
- ✅ 長時間利用時もメモリが圧迫されない

### ユーザビリティの向上
- ✅ 同じフォルダを繰り返し開いても履歴が汚染されない
- ✅ パスの正規化により大文字小文字の違いを吸収

### 保守性の向上
- ✅ テストが環境非依存で安定
- ✅ スタック操作が一元化され保守しやすい

## まとめ

コードレビューで指摘されたすべての問題点に対応し、ナビゲーション履歴機能がより堅牢で安全になりました。

特に、並行実行の防止とロード失敗時の履歴保護により、実運用時の不具合リスクが大幅に低減されました。
