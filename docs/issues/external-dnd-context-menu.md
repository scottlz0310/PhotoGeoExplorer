# 外部アプリへのドラッグアンドドロップ対応と右クリックコンテキストメニューの拡充

> **注記**: このファイルは GitHub Issue のドラフトです。内容を確認後、GitHub Issue として投稿してください。

## 概要

ファイルブラウザ（`FileBrowserPane`）でのユーザー操作体験を向上させるため、以下の2つの機能拡張を行う。

1. **外部アプリへのドラッグアンドドロップ対応** — 現在は PhotoGeoExplorer 内部でのファイル移動のみ対応。エクスプローラーなど外部アプリへのファイルコピー操作を可能にする。
2. **右クリックコンテキストメニューの拡充** — 「エクスプローラーで表示」「パスをコピー」「既定のアプリで開く」を追加し、ファイル操作の利便性を高める。

---

## 現状の分析

### ドラッグアンドドロップ（外部出力が未対応）

**`OnFileItemsDragStarting`** (`FileBrowserPaneView.xaml.cs` 行 530–544):

```csharp
private void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
{
    // ドラッグアイテムを収集
    _dragItems = e.Items.OfType<PhotoListItem>().ToList();
    if (_dragItems.Count == 0 && ViewModel.SelectedItems.Count > 0)
        _dragItems = ViewModel.SelectedItems.ToList();

    e.Data.RequestedOperation = DataPackageOperation.Move;   // ← Move のみ
    e.Data.Properties[InternalDragKey] = true;               // ← 内部マークのみ設定
    // StorageItems は一切設定しない → 外部アプリはファイルと認識できない
}
```

XAML では `CanDragItems="True"` が3つのリストビュー（Details / Icon / List）に設定済みで、ドラッグ自体は始動できる。しかし `DataPackage` に `StorageItems` が含まれないため、エクスプローラー等の外部ドロップターゲットにはファイルとして認識されない。

**`OnFileListDragOver`** (`FileBrowserPaneView.xaml.cs` 行 431–455) も外部ドロップ受け入れ側の判定のみ実装しており、外部への出力制御は含まれていない。

**`IsInternalDrag`** (`FileBrowserPaneView.xaml.cs` 行 1057–1066) は `InternalDragKey` の有無で内部/外部を区別している。このロジックはそのまま維持できる。

### 右クリックコンテキストメニュー（項目が限定的）

**`BuildFileContextFlyout`** (`FileBrowserPaneView.xaml.cs` 行 989–1056) が構築する現在の項目：

| 項目 | リソースキー | 有効条件 |
|------|-------------|----------|
| 新しいフォルダー | `Menu.NewFolder` | `CanCreateFolder` |
| 名前の変更 | `Menu.Rename` | `CanRenameSelection` |
| 移動 | `Menu.Move` | `CanModifySelection` |
| 親フォルダーへ移動 | `Menu.MoveToParent` | `CanMoveToParentSelection` |
| EXIFを編集 | `Menu.EditExif` | `CanEditExif` |
| 削除 | `Menu.Delete` | `CanModifySelection` |

エクスプローラーとの連携や外部アプリ起動などの操作がなく、基本的なファイルマネージャーとしての操作が欠けている。

**参考**: `MainWindow.xaml.cs` 行 439–471 では `Windows.System.Launcher.LaunchFolderPathAsync` を使ったログフォルダーを開く処理が実装済みで、同パターンが使える。

---

## 実装計画

### 1. 外部アプリへのドラッグアンドドロップ対応

#### 対象ファイル

- `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs`

#### 変更内容

**`OnFileItemsDragStarting` の改修**

ドラッグ開始時に `StorageItems` を `DataPackage` にセットする。

> **注意 — `async void` と例外処理**: `DragItemsStartingEventArgs` はイベントハンドラーであるため `async void` は適切。ただし、`StorageFile.GetFileFromPathAsync` などが例外をスローした場合（権限エラー等）、`deferral.Complete()` が呼ばれないとドラッグ操作がハングするリスクがある。`try/finally` で `deferral.Complete()` を必ず呼ぶようにし、例外は `AppLog.Error` でログ記録してからドラッグを中断する実装とする。

```csharp
private async void OnFileItemsDragStarting(object sender, DragItemsStartingEventArgs e)
{
    // 既存のアイテム収集ロジックはそのまま維持
    _dragItems = e.Items.OfType<PhotoListItem>().ToList();
    if (_dragItems.Count == 0 && ViewModel.SelectedItems.Count > 0)
        _dragItems = ViewModel.SelectedItems.ToList();

    // 内部ドラッグマークは引き続きセット
    e.Data.Properties[InternalDragKey] = true;

    // 外部アプリ向けに StorageItems を追加セット（非同期のため Deferral を取得）
    var deferral = e.Data.GetDeferral();
    try
    {
        var storageItems = new List<IStorageItem>();
        foreach (var item in _dragItems)
        {
            if (item.IsFolder)
                storageItems.Add(await StorageFolder.GetFolderFromPathAsync(item.FilePath));
            else
                storageItems.Add(await StorageFile.GetFileFromPathAsync(item.FilePath));
        }
        // readOnly: true — 外部アプリによるファイル変更を防ぐ（コピー専用）
        e.Data.SetStorageItems(storageItems, readOnly: true);
        // Copy と Move の両方を許可（外部は Copy、内部は Move として扱う）
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
    }
    catch (Exception ex)
    {
        // 権限エラーなどでドラッグを中断
        AppLog.Error("Failed to set StorageItems for drag operation.", ex);
        _dragItems = null;
    }
    finally
    {
        deferral.Complete();
    }
}
```

**`OnFileItemsDragCompleted` の確認**

```csharp
private void OnFileItemsDragCompleted(object sender, DragItemsCompletedEventArgs e)
{
    // 外部ドロップ（Copy）の場合はファイル削除しない
    // 内部ドロップ（Move）の判定は IsInternalDrag で制御済みのため変更不要
    _dragItems = null;
}
```

#### 技術的考慮事項

- `DragItemsStartingEventArgs` の `Data` は `DataPackage`。`GetDeferral()` を使って非同期操作を完了させる必要がある。
- MSIX パッケージングのセキュリティコンテキストでは、`broadFileSystemAccess` 能力が `Package.appxmanifest` に含まれていない場合、`StorageFile.GetFileFromPathAsync` がアクセス拒否になることがある。現状の実装でフォルダー移動が動作していることから、既にアクセス権は得られている可能性が高いが、要確認。
- 外部アプリへのドロップ後に Move が誤動作しないよう `IsInternalDrag` 判定の整合性を確認する。

---

### 2. 右クリックコンテキストメニューの拡充

#### 2-1. リソース文字列の追加

**`PhotoGeoExplorer/Strings/ja-JP/Resources.resw`**

```xml
<data name="Menu.ShowInExplorer" xml:space="preserve">
  <value>エクスプローラーで表示</value>
</data>
<data name="Menu.CopyPath" xml:space="preserve">
  <value>パスをコピー</value>
</data>
<data name="Menu.OpenWithDefaultApp" xml:space="preserve">
  <value>既定のアプリで開く</value>
</data>
```

**`PhotoGeoExplorer/Strings/en-US/Resources.resw`**

```xml
<data name="Menu.ShowInExplorer" xml:space="preserve">
  <value>Show in Explorer</value>
</data>
<data name="Menu.CopyPath" xml:space="preserve">
  <value>Copy Path</value>
</data>
<data name="Menu.OpenWithDefaultApp" xml:space="preserve">
  <value>Open with Default App</value>
</data>
```

#### 2-2. ViewModel プロパティの追加

**`PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs`**

既存の `CanModifySelection` / `CanRenameSelection` に倣い追加：

```csharp
/// <summary>1件以上選択されている場合に有効</summary>
public bool CanShowInExplorer => CanModifySelection;

/// <summary>1件以上選択されている場合に有効（複数選択時は改行区切りで全パスをコピー）</summary>
public bool CanCopyPath => CanModifySelection;

/// <summary>1件のみ選択されている場合に有効（複数ファイルの同時起動は対象外）</summary>
public bool CanOpenWithDefaultApp => CanRenameSelection;
```

`UpdateSelection` や選択状態変更時に `OnPropertyChanged` も追加する。

#### 2-3. コンテキストメニュー項目の追加

**`PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs`**

`BuildFileContextFlyout` に3項目を追加。メニュー構成案：

```
新しいフォルダー
─────────────
名前の変更
移動
親フォルダーへ移動
─────────────
エクスプローラーで表示   ← 新規
パスをコピー             ← 新規
─────────────
既定のアプリで開く       ← 新規
─────────────
EXIFを編集
削除
```

**「エクスプローラーで表示」の実装例**:

```csharp
private async void OnShowInExplorerClicked(object sender, RoutedEventArgs e)
{
    var selectedItems = ViewModel?.SelectedItems;
    if (selectedItems is null || selectedItems.Count == 0) return;

    var item = selectedItems[0];
    if (item.IsFolder)
    {
        await Windows.System.Launcher.LaunchFolderPathAsync(item.FilePath);
    }
    else
    {
        var parentDir = Path.GetDirectoryName(item.FilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(parentDir);
            var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
            var options = new Windows.System.FolderLauncherOptions();
            options.ItemsToSelect.Add(file);
            await Windows.System.Launcher.LaunchFolderAsync(folder, options);
        }
    }
}
```

**「パスをコピー」の実装例**:

```csharp
private void OnCopyPathClicked(object sender, RoutedEventArgs e)
{
    var selectedItems = ViewModel?.SelectedItems;
    if (selectedItems is null || selectedItems.Count == 0) return;

    var paths = string.Join(Environment.NewLine,
        selectedItems.Select(item => item.FilePath));

    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
    dataPackage.SetText(paths);
    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
}
```

**「既定のアプリで開く」の実装例**:

```csharp
private async void OnOpenWithDefaultAppClicked(object sender, RoutedEventArgs e)
{
    var item = ViewModel?.SelectedItem;
    if (item is null) return;

    if (item.IsFolder)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(item.FilePath);
        await Windows.System.Launcher.LaunchFolderAsync(folder);
    }
    else
    {
        var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
        await Windows.System.Launcher.LaunchFileAsync(file);
    }
}
```

---

### 3. テスト

#### 単体テスト (`PhotoGeoExplorer.Tests/`)

`FileBrowserPaneViewModelTests`（既存テストパターンに倣う）に追加：

- `CanShowInExplorer_WhenItemSelected_ReturnsTrue`
- `CanShowInExplorer_WhenNoItemSelected_ReturnsFalse`
- `CanCopyPath_WhenItemSelected_ReturnsTrue`
- `CanCopyPath_WhenNoItemSelected_ReturnsFalse`
- `CanOpenWithDefaultApp_WhenSingleItemSelected_ReturnsTrue`
- `CanOpenWithDefaultApp_WhenMultipleItemsSelected_ReturnsFalse`

#### E2E テスト (`PhotoGeoExplorer.E2E/`)

`AppE2ETests.cs` の `OpenExifMenuForItemName` パターンを参考に：

- 右クリックメニューに「エクスプローラーで表示」「パスをコピー」「既定のアプリで開く」が表示されることを確認
- 「パスをコピー」クリック後にクリップボードに正しいパスが入ることを確認（可能な場合）

---

## 影響範囲まとめ

| ファイル | 変更内容 |
|--------|---------|
| `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs` | `OnFileItemsDragStarting` 改修、コンテキストメニュー項目追加 |
| `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs` | `CanShowInExplorer`, `CanCopyPath`, `CanOpenWithDefaultApp` 追加 |
| `PhotoGeoExplorer/Strings/ja-JP/Resources.resw` | メニュー文字列3件追加 |
| `PhotoGeoExplorer/Strings/en-US/Resources.resw` | メニュー文字列3件追加 |
| `PhotoGeoExplorer.Tests/` | ViewModel の Can* テスト追加 |
| `PhotoGeoExplorer.E2E/` | 右クリックメニュー確認 E2E テスト追加 |

---

## 優先度

- **外部 DnD**: 中〜高（頻繁に使われる操作で、現状のドラッグが「無反応」に見えるため UX 上の問題がある）
- **コンテキストメニュー拡充**: 中（あると便利だが、必須ではない）

---

## ラベル候補

- `enhancement`
- `UX`
- `file-operations`
