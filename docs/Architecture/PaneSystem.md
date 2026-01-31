# Pane System アーキテクチャガイド

## 概要

PhotoGeoExplorer では、MainWindow の肥大化（4000行超）を防ぐため、**Shell + Pane** アーキテクチャを採用します。

- **MainWindow (Shell)**: レイアウトとペイン配置のみを担当
- **Pane (UserControl)**: 機能単位のUI + ViewModel + Service

このドキュメントは、新しいPaneの作成方法と、アーキテクチャの責務境界を定義します。

**サンプル実装:**
- [`PhotoGeoExplorer/Panes/Settings/`](../../PhotoGeoExplorer/Panes/Settings/) - 設定Paneのサンプル実装
  - `SettingsPaneViewModel.cs` - ViewModel の実装例
  - `SettingsPaneView.xaml` / `SettingsPaneView.xaml.cs` - View の実装例
- [`PhotoGeoExplorer.Tests/SettingsPaneViewModelTests.cs`](../../PhotoGeoExplorer.Tests/SettingsPaneViewModelTests.cs) - ViewModel のテスト例

## アーキテクチャ原則

### MainWindow の責務（Shell専任）

**✅ OK（MainWindowでやること）**

1. レイアウト（Grid / SplitView / Pane配置）
2. ペインの表示切替（どのPaneを出すか）
3. アプリ全体イベントの入口（Window閉じる等）
   - ただし、処理本体はServiceへ委譲
4. Composition Root（DI初期化・VM生成）

**❌ NG（MainWindowでやってはいけないこと）**

1. 業務ロジック
2. 状態管理（選択状態、編集状態、ロード状態…）
3. I/O（ファイル、DB、外部API、設定保存）
4. 増殖するイベントハンドラ
   - 何かやるならCommand/Serviceへ

### Pane の責務

**✅ Pane + PaneViewModel + Service で完結させる**

- UI表示とユーザー操作の受付
- ViewModel による状態管理
- Service によるビジネスロジックとI/O

**❌ 他のPaneへの直接参照は禁止**

- PaneA が PaneB を直接呼ぶ構造は密結合を生む
- 連携が必要な場合は以下を使用：
  - **WorkspaceState（共有状態ストア）**
  - **Messenger/Event（疎結合通信）**

## ディレクトリ構成

```
/PhotoGeoExplorer
  /Panes
    /Map
      MapPaneView.xaml          # UI定義
      MapPaneView.xaml.cs       # コードビハインド（最小限）
      MapPaneViewModel.cs       # ViewModel
    /Preview
      PreviewPaneView.xaml
      PreviewPaneView.xaml.cs
      PreviewPaneViewModel.cs
    /FileBrowser
      FileBrowserPaneView.xaml
      FileBrowserPaneView.xaml.cs
      FileBrowserPaneViewModel.cs
    PaneViewModelDataTemplateSelector.cs  # VM→View解決
  /Services
    IPhotoService.cs
    PhotoService.cs
    ISettingsService.cs
    SettingsService.cs
  /State
    WorkspaceState.cs           # ペイン間共有状態
  /ViewModels
    IPaneViewModel.cs           # Pane VMインターフェース
    PaneViewModelBase.cs        # Pane VM基底クラス
    BindableBase.cs             # INotifyPropertyChanged実装
    MainViewModel.cs            # MainWindow用VM（Shell管理のみ）
```

## Pane の作成方法

### 1. ViewModel を作成

`PaneViewModelBase` を継承し、`IPaneViewModel` を実装します。

```csharp
using System.Threading.Tasks;
using PhotoGeoExplorer.ViewModels;

namespace PhotoGeoExplorer.Panes.Map;

internal sealed class MapPaneViewModel : PaneViewModelBase
{
    public MapPaneViewModel()
    {
        Title = "Map";
    }

    protected override async Task OnInitializeAsync()
    {
        // 初期化処理（非同期）
        await Task.CompletedTask;
    }

    protected override void OnCleanup()
    {
        // クリーンアップ処理
    }

    protected override void OnActiveChanged()
    {
        // IsActiveが変更されたときの処理
    }
}
```

### 2. View (UserControl) を作成

**MapPaneView.xaml**

```xml
<UserControl
    x:Class="PhotoGeoExplorer.Panes.Map.MapPaneView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d">
    <Grid>
        <TextBlock Text="{Binding Title}" />
        <!-- 地図表示のUIコントロール -->
    </Grid>
</UserControl>
```

**MapPaneView.xaml.cs**

```csharp
using Microsoft.UI.Xaml.Controls;

namespace PhotoGeoExplorer.Panes.Map;

public sealed partial class MapPaneView : UserControl
{
    public MapPaneView()
    {
        InitializeComponent();
    }
}
```

### 3. MainWindow.xaml に DataTemplate を追加

```xml
<Window.Resources>
    <DataTemplate x:DataType="vm:MapPaneViewModel">
        <panes:MapPaneView DataContext="{Binding}" />
    </DataTemplate>
</Window.Resources>
```

### 4. MainWindow で Pane を表示

```xml
<ContentControl Content="{Binding CurrentPane}" />
```

## 命名規則

### ディレクトリとファイル

- **ディレクトリ名**: PascalCase（例: `/Panes/Map`, `/Panes/Preview`）
- **View**: `{機能名}PaneView.xaml` / `{機能名}PaneView.xaml.cs`
- **ViewModel**: `{機能名}PaneViewModel.cs`

### クラス名

- **View**: `{機能名}PaneView`（例: `MapPaneView`, `PreviewPaneView`）
- **ViewModel**: `{機能名}PaneViewModel`（例: `MapPaneViewModel`, `PreviewPaneViewModel`）

### プロパティとフィールド

- **public プロパティ**: `PascalCase`
- **private フィールド**: `_camelCase`（アンダースコアプレフィックス）

## ペイン間通信

### WorkspaceState を使用した状態共有

```csharp
// WorkspaceState はアプリ全体で共有される状態
public class WorkspaceState : BindableBase
{
    private string? _currentFolderPath;

    public string? CurrentFolderPath
    {
        get => _currentFolderPath;
        set => SetProperty(ref _currentFolderPath, value);
    }
}

// Pane ViewModel で使用
public class FileBrowserPaneViewModel : PaneViewModelBase
{
    private readonly WorkspaceState _workspaceState;

    public FileBrowserPaneViewModel(WorkspaceState workspaceState)
    {
        _workspaceState = workspaceState;
    }

    public void NavigateToFolder(string path)
    {
        // 状態を更新すると、他のPaneも反応できる
        _workspaceState.CurrentFolderPath = path;
    }
}
```

### Messenger パターン（将来的な拡張）

疎結合なイベント通信が必要な場合は、Messengerパターンを使用します（実装は将来的に追加）。

## テスト方針

### Unit Test（優先）

- **PaneViewModel のテスト**: 状態遷移、コマンド実行、例外処理
- **Service のテスト**: ビジネスロジック、I/O、外部API呼び出し

### Integration Test

- Service と ViewModel の連携テスト

### UI Test（最小限）

- 起動確認
- 主要Paneの表示確認

### E2E Test（オプション）

- 自動E2Eは「できたら追加」でOK（必須ではない）

## ガードレール（運用・レビュー基準）

### PRレビュー時のチェックリスト

すべてのPRは以下を満たす必要があります：

- [ ] MainWindowに新規ロジックを追加していない
  - 追加した場合は理由と代替案を明示
- [ ] 新機能は PaneVM/Service に入れた
- [ ] ペイン間の直接参照を追加していない
  - 共有State/Messenger経由で連携
- [ ] ロジック変更と構造変更を混ぜていない

### MainWindow 封鎖ルール

**「Shell以外のロジック」をMainWindowに追加するPRは原則Reject**

- 例外を認める場合は、チーム合意が必要
- 代わりに Pane/Service へ移動する計画を示す

## 移行計画

### フェーズ1: 土台構築（このドキュメントで完了）

- [x] `/Panes`, `/State` ディレクトリ作成
- [x] `IPaneViewModel`, `PaneViewModelBase` 定義
- [x] `WorkspaceState` 作成
- [x] アーキテクチャドキュメント整備

### フェーズ2: 標準形の確立（次のPR）

- [ ] 独立Paneを1つ移植（例: 設定Pane）
- [ ] 命名/構成/DIの標準を確立
- [ ] 「次からこの型で増やせば良い」を共有

### フェーズ3: 段階的移行（その後のPR群）

- [ ] Map Pane の移植
- [ ] Preview Pane の移植
- [ ] FileBrowser Pane の移植
- [ ] その他の機能を順次移行

## 参考資料

- [MVVM パターン（Microsoft Docs）](https://docs.microsoft.com/ja-jp/windows/uwp/data-binding/data-binding-and-mvvm)
- [WinUI 3 ドキュメント](https://docs.microsoft.com/ja-jp/windows/apps/winui/winui3/)
- ISSUE #70: MainWindowのShell化・Paneコンポーネント化による段階的リファクタ計画提案

## FAQ

### Q: なぜ MainWindow に直接ロジックを書いてはいけないのか？

**A**: MainWindow が肥大化し、以下の問題が発生するためです：

- **保守性の低下**: 4000行超のコードは変更が困難
- **テスト困難**: UIと密結合したロジックはテストしにくい
- **衝突リスク**: 複数人が同じファイルを編集すると競合が頻発
- **再利用不可**: 他の画面や機能で再利用できない

### Q: WorkspaceState と PaneViewModel の違いは？

**A**:

- **WorkspaceState**: アプリ全体で共有される状態（現在のフォルダ、選択状態など）
- **PaneViewModel**: 特定のPaneに閉じた状態と振る舞い

### Q: 既存の MainWindow のコードはどうするか？

**A**: 段階的に Pane へ移行します：

1. **独立度の高い機能から移行**（例: 設定、ヘルプ）
2. **Service を抽出してから VM へ移行**（I/Oロジックを分離）
3. **1PR = 1Pane or 1責務** を原則とし、レビュー可能な単位で進める

### Q: 既存テストは動き続けるか？

**A**: はい、既存テストは動き続けます：

- MainWindow の外部インターフェースは維持
- 内部実装を Pane へ移行するのみ
- 新しい Pane には新しいテストを追加

## 更新履歴

- **2026-01-31**: 初版作成（PR#1: 土台構築）
