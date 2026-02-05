# FileBrowser Pane 統合ガイド

このドキュメントは、FileBrowser Pane を MainWindow に統合する手順を説明します。

## 概要

FileBrowser Pane は、MainWindow から分離されたファイルブラウザ機能を提供します。以下のコンポーネントで構成されています：

- **FileBrowserPaneService**: ファイルシステム操作、ナビゲーション履歴、ソート処理
- **FileBrowserPaneViewModel**: UI状態管理、コマンド、WorkspaceState連携
- **FileBrowserPaneView.xaml**: XAML UI定義（未実装 - Windows環境で作成が必要）

## 既に実装された機能

### FileBrowserPaneService

- ✅ ファイル一覧読み込み（`LoadFolderAsync`）
- ✅ ナビゲーション履歴管理（戻る/進む、最大100履歴）
- ✅ ソート機能（自然順ソート、Name/Modified/Resolution/Size）
- ✅ ブレッドクラム生成（`GetBreadcrumbs`）
- ✅ 単体テスト（16テスト）

### FileBrowserPaneViewModel

- ✅ ファイル一覧の状態管理（`ObservableCollection<PhotoListItem>`）
- ✅ フォルダナビゲーション（Home/Up/Back/Forward/Refresh）
- ✅ ソート切替（4列、昇順/降順）
- ✅ 検索/フィルタ機能（`SearchText`、`ShowImagesOnly`）
- ✅ サムネイル生成の非同期処理（並列3、バッチ更新300ms）
- ✅ WorkspaceState 連携（選択ファイル、インデックス、カウント）
- ✅ 単体テスト（17テスト）

## 次のステップ：MainWindow への統合

### ステップ1: FileBrowserPaneView.xaml の作成

**場所**: `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml`

既存の `MainWindow.xaml` から FileBrowser 関連の XAML を抽出し、DataTemplate として定義します。

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:PhotoGeoExplorer.Panes.FileBrowser">
    
    <DataTemplate x:Key="FileBrowserPaneTemplate" x:DataType="vm:FileBrowserPaneViewModel">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />  <!-- ツールバー（ナビゲーション、検索） -->
                <RowDefinition Height="Auto" />  <!-- ブレッドクラム -->
                <RowDefinition Height="*" />     <!-- ファイル一覧 -->
                <RowDefinition Height="Auto" />  <!-- ステータスバー -->
            </Grid.RowDefinitions>
            
            <!-- ツールバー: ナビゲーションボタン -->
            <CommandBar Grid.Row="0">
                <AppBarButton Icon="Home" Label="Home" Command="{Binding NavigateHomeCommand}" />
                <AppBarButton Icon="Up" Label="Up" Command="{Binding NavigateUpCommand}" />
                <AppBarButton Icon="Back" Label="Back" Command="{Binding NavigateBackCommand}" />
                <AppBarButton Icon="Forward" Label="Forward" Command="{Binding NavigateForwardCommand}" />
                <AppBarButton Icon="Refresh" Label="Refresh" Command="{Binding RefreshCommand}" />
                <!-- 検索ボックス、フィルタなど -->
            </CommandBar>
            
            <!-- ブレッドクラムバー -->
            <BreadcrumbBar Grid.Row="1" ItemsSource="{Binding BreadcrumbItems}" />
            
            <!-- ファイル一覧（ListView / GridView） -->
            <ListView Grid.Row="2" ItemsSource="{Binding Items}" 
                      SelectedItem="{Binding SelectedItem, Mode=TwoWay}" />
        </Grid>
    </DataTemplate>
</ResourceDictionary>
```

**参考**:
- `PhotoGeoExplorer/MainWindow.xaml` の行 349-600（BreadcrumbBar、ListView）
- `PhotoGeoExplorer/Panes/Settings/SettingsPaneView.xaml`（DataTemplate の例）

### ステップ2: MainWindow.xaml への DataTemplate 追加

`MainWindow.xaml` の `<Window.Resources>` に FileBrowser Pane のテンプレートを追加します：

```xml
<Window.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <!-- 既存 -->
            <ResourceDictionary Source="ms-appx:///Panes/Settings/SettingsPaneView.xaml" />
            <ResourceDictionary Source="ms-appx:///Panes/Map/MapPaneView.xaml" />
            
            <!-- 新規: FileBrowser Pane -->
            <ResourceDictionary Source="ms-appx:///Panes/FileBrowser/FileBrowserPaneView.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Window.Resources>
```

### ステップ3: MainWindow.xaml.cs で FileBrowserPaneViewModel を初期化

`MainWindow.xaml.cs` の `InitializePanes()` メソッドに追加：

```csharp
private FileBrowserPaneViewModel? _fileBrowserPane;

private void InitializePanes()
{
    // 既存の Pane 初期化...
    
    // FileBrowser Pane の初期化
    _fileBrowserPane = new FileBrowserPaneViewModel(
        new FileBrowserPaneService(),
        _viewModel.WorkspaceState);
    
    _ = _fileBrowserPane.InitializeAsync();
}
```

### ステップ4: MainWindow のレイアウトに配置

`MainWindow.xaml` の `FileBrowserPane` に FileBrowserPaneViewModel を配置：

```xml
<!-- 既存の FileBrowserPane Grid -->
<Grid x:Name="FileBrowserPane" Grid.Row="0" Grid.Column="0">
    <!-- 既存の内容を削除し、ContentControl に置き換え -->
    <ContentControl 
        Content="{Binding FileBrowserPane}"
        ContentTemplate="{StaticResource FileBrowserPaneTemplate}" />
</Grid>
```

`MainWindow.xaml.cs` で Binding を設定：

```csharp
// DataContext に FileBrowserPane を追加
FileBrowserPane.DataContext = this;

// または MainViewModel に FileBrowserPane プロパティを追加
public FileBrowserPaneViewModel FileBrowserPane => _fileBrowserPane!;
```

### ステップ5: MainViewModel からのロジック移行

**MainViewModel から削除するもの**:
- `Items` プロパティ → `FileBrowserPaneViewModel.Items`
- `BreadcrumbItems` プロパティ → `FileBrowserPaneViewModel.BreadcrumbItems`
- `LoadFolderAsync` メソッド → `FileBrowserPaneViewModel.LoadFolderAsync`
- `NavigateBackAsync` メソッド → `FileBrowserPaneViewModel.NavigateBackAsync`
- `NavigateForwardAsync` メソッド → `FileBrowserPaneViewModel.NavigateForwardAsync`
- `ToggleSort` メソッド → `FileBrowserPaneViewModel.ToggleSort`
- `SearchText` プロパティ → `FileBrowserPaneViewModel.SearchText`
- `ShowImagesOnly` プロパティ → `FileBrowserPaneViewModel.ShowImagesOnly`
- サムネイル生成関連のフィールド/メソッド → `FileBrowserPaneViewModel` 内部実装

**MainWindow.xaml.cs から更新するもの**:
- イベントハンドラ（`OnNavigateBackClicked` など）を `FileBrowserPaneViewModel` のコマンドに置き換え

```csharp
// 旧: MainWindow.xaml.cs
private async void OnNavigateBackClicked(object sender, RoutedEventArgs e)
{
    await _viewModel.NavigateBackAsync().ConfigureAwait(false);
}

// 新: XAML でコマンドバインディング
<AppBarButton Command="{Binding NavigateBackCommand}" />
```

### ステップ6: WorkspaceState 連携の確認

FileBrowserPaneViewModel は既に WorkspaceState に連携しているため、選択状態は自動的に Map Pane と Preview Pane に反映されます。

```csharp
// FileBrowserPaneViewModel.OnSelectedItemChanged() で実装済み
_workspaceState.SelectedPhotos = selectedPhotos;
_workspaceState.SelectedPhotoCount = selectedPhotos.Count;
_workspaceState.CurrentPhotoIndex = index;
_workspaceState.PhotoListCount = photoItems.Count;
```

## テスト

### 単体テスト

既に実装されています：

```bash
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileBrowserPane
```

### 手動テスト

1. **フォルダナビゲーション**
   - Home ボタンでピクチャフォルダへ移動
   - Up ボタンで親フォルダへ移動
   - ブレッドクラムでフォルダを選択

2. **ナビゲーション履歴**
   - Back ボタンで前のフォルダに戻る
   - Forward ボタンで次のフォルダに進む

3. **ソート**
   - 列ヘッダーをクリックしてソート切替
   - 昇順/降順の切替

4. **検索/フィルタ**
   - 検索ボックスでファイル名検索
   - 「画像のみ表示」トグルでフィルタ

5. **サムネイル生成**
   - フォルダを開いたときにサムネイルが非同期で生成される
   - プレースホルダーが表示され、生成完了後に置き換わる

6. **WorkspaceState 連携**
   - 写真を選択したときに Map Pane でマーカーが表示される
   - Preview Pane で画像が表示される

## トラブルシューティング

### ビルドエラー

**問題**: XAML コンパイラエラー  
**解決**: Visual Studio 2022 でビルドしてください。Linux 環境では XAML コンパイラが動作しません。

### UI が表示されない

**問題**: FileBrowserPaneView が表示されない  
**解決**: 
1. `MainWindow.xaml` の `<ResourceDictionary.MergedDictionaries>` に追加されているか確認
2. `ContentControl` の `ContentTemplate` バインディングを確認
3. `FileBrowserPaneViewModel` が初期化されているか確認

### サムネイルが生成されない

**問題**: サムネイルが表示されない  
**解決**:
1. `ThumbnailService` が正常に動作しているか確認
2. ログファイル (`%LocalAppData%\PhotoGeoExplorer\Logs\app.log`) でエラーを確認
3. UIスレッドで BitmapImage が作成されているか確認

## 参考資料

- `docs/Architecture/PaneSystem.md` - Pane System アーキテクチャガイド
- `PhotoGeoExplorer/Panes/Settings/` - Settings Pane の実装例
- `PhotoGeoExplorer/Panes/Map/` - Map Pane の実装例
- `PhotoGeoExplorer/ViewModels/MainViewModel.cs` - 既存の実装（移行元）
- `PhotoGeoExplorer/MainWindow.xaml` - 既存の XAML（移行元）

## まとめ

FileBrowser Pane の基盤（Service、ViewModel、Tests）は完成しました。次のステップは：

1. **Windows 環境で**:
   - `FileBrowserPaneView.xaml` を作成
   - MainWindow に統合
   - 手動テストで動作確認

2. **既存コードのクリーンアップ**:
   - MainViewModel からファイルブラウザ関連のコードを削除
   - MainWindow.xaml.cs のイベントハンドラを削除
   - 不要なフィールド/プロパティを削除

3. **ドキュメント更新**:
   - `docs/Architecture/PaneSystem.md` を更新
   - CHANGELOG.md に変更を記録

これで、FileBrowser 機能が独立した Pane として動作し、MainWindow がよりシンプルになります！
