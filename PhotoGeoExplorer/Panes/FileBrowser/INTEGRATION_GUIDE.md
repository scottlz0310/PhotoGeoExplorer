# FileBrowser Pane 統合ガイド

このドキュメントは、FileBrowser Pane を MainWindow に統合する手順と、現在の実装状況を整理したものです。

## 概要

FileBrowser Pane は、MainWindow から分離されたファイルブラウザ機能を提供します。以下のコンポーネントで構成されています：

- **FileBrowserPaneService**: ファイルシステム操作、ナビゲーション履歴、ソート処理
- **FileBrowserPaneViewModel**: UI状態管理、コマンド、WorkspaceState連携、ステータス表示
- **FileBrowserPaneView.xaml**: UserControl として実装（FileBrowser UI 本体）

## 現在の実装状況 (2026-02-05)

- ✅ FileBrowserPaneService（読み込み/履歴/ソート/ブレッドクラム）
- ✅ FileBrowserPaneViewModel（一覧・ナビゲーション・ソート・フィルタ・サムネイル・選択連携・ステータス）
- ✅ FileBrowserPaneView（UserControl）作成済み
- ✅ MainWindow 統合（FileBrowserPaneControl を配置・VM 初期化）
- ✅ 設定連携（ShowImagesOnly / FileViewMode / LastFolderPath の保存・復元）
- ✅ MainWindow 旧 FileBrowser イベント/ヘルパーの削除（UI/イベントを Pane に集約）
- ✅ FileBrowserPaneViewModel のテスト追加（CanCreateFolder/CanRenameSelection など）

## 統合の要点（実装済み）

### 1. FileBrowserPaneView.xaml を UserControl として作成

**場所**: `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml`

FileBrowser UI を UserControl へ移植しています。DataTemplate ではなく UserControl を採用し、
ドラッグ&ドロップやコンテキストメニューなどの UI イベントは code-behind に集約しています。

### 2. MainWindow.xaml に配置

```xml
<Grid x:Name="FileBrowserPane" Grid.Column="0">
    <fileBrowser:FileBrowserPaneView x:Name="FileBrowserPaneControl" />
</Grid>
```

### 3. MainWindow.xaml.cs で ViewModel 初期化

```csharp
_fileBrowserPaneViewModel = new FileBrowserPaneViewModel(
    new FileBrowserPaneService(),
    _viewModel.WorkspaceState);

FileBrowserPaneControl.DataContext = _fileBrowserPaneViewModel;
FileBrowserPaneControl.HostWindow = this;
FileBrowserPaneControl.EditExifRequested += OnEditExifRequested;
```

- `HostWindow` は FolderPicker 初期化用
- `EditExifRequested` は MainWindow の EXIF 編集処理へ委譲

### 4. 設定保存の連携

`FileBrowserPaneViewModel` の `ShowImagesOnly` / `FileViewMode` / `CurrentFolderPath` 変更を
MainWindow 側で監視し、`ScheduleSettingsSave()` を呼び出します。

```csharp
_fileBrowserPaneViewModel.PropertyChanged += OnFileBrowserPanePropertyChanged;
```

### 5. メニューのバインディング切替

MainWindow メニューの `IsEnabled` を `FileBrowserPaneViewModel` に接続します。

```xml
<MenuFlyoutItem
    Text="New folder"
    IsEnabled="{Binding ElementName=FileBrowserPaneControl, Path=DataContext.CanCreateFolder}" />
```

### 6. MainWindow の旧 FileBrowser イベント削除

旧 FileBrowser UI イベントは `FileBrowserPaneView.xaml.cs` に集約し、
`MainWindow.xaml.cs` から該当ハンドラ/ヘルパーを削除しています。

## テスト

- 単体テスト:

```bash
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileBrowserPane
```

- 手動テスト:
  - フォルダナビゲーション（Home/Up/Back/Forward）
  - ソート切替（Name/Modified/Resolution/Size）
  - 検索/フィルタ（Enterで検索、Show images only）
  - 右クリックメニュー（新規/リネーム/移動/削除/EXIF編集）
  - ドラッグ&ドロップ
  - Map/Preview 連携（WorkspaceState 経由）
