# Phase 2 監査結果

> 監査日: 2026-04-11
> 監査対象コミット: `4ee32cfc43293b952ab7b16da387242113a043e2`
> 監査基準: [phase1_ideal_architecture.md](./phase1_ideal_architecture.md)
> 監査ガイド: [phase2_repository_audit.md](./phase2_repository_audit.md)

---

## A. サマリ

- 監査対象ファイル数: 147
- 監査方法: リポジトリ全体を `rg` で横断検索し、重点領域（`MainWindow`、`Panes/*`、`Services/*`、`State/*`、`PhotoGeoExplorer.Core/*`）を実読して判定
- 除外: 画像・アイコン・HTML・`.resw`・マニフェストなど、MVVM 境界判定に直接関与しない静的成果物

## カテゴリ別件数

| カテゴリ | 件数 |
|---|---:|
| MVVM 境界違反 | 5 |
| テスト不能要因 | 1 |
| 非同期不整合 | 1 |
| 構造的問題 | 2 |

## 重大度別件数

| 重大度 | 件数 |
|---|---:|
| Critical | 1 |
| High | 6 |
| Medium | 2 |
| Low | 0 |

## 問題の大きい領域 Top 5

1. `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs`
2. `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs`
3. `PhotoGeoExplorer/MainWindow.xaml.cs`
4. `PhotoGeoExplorer/Panes/Map/MapPaneViewModel.cs`
5. `PhotoGeoExplorer/Panes/Preview/PreviewPaneViewModel.cs`

---

## B. 詳細リスト

| ID | カテゴリ | 重大度 | ファイル | クラス/メソッド | 違反内容 | 根拠コード | 影響範囲 | 修正難易度 | 優先度スコア | 推奨是正方針 |
|---|---|---|---|---|---|---|---|---|---:|---|
| A-01 | MVVM 境界違反 | Critical | `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml.cs` | `FileBrowserPaneView.CreateFolderAsyncCore` / `RenameSelectionAsyncCore` / `MoveItemsToFolderAsync` / `DeleteItemsAsync` | View がファイル作成・移動・削除・リネーム、件数ループ、パス検証を持っており、Phase 1 の View 責務を逸脱している。 | 660-679 行の `Directory.CreateDirectory`、743-760 行の `Directory.Move` / `File.Move`、1144-1280 行の `foreach` と `Directory.Delete` / `File.Delete`、1203-1220 行の `IsSamePath` / `IsDescendantPath` | ファイル操作 UX 全体。単体テスト不能なまま View に業務ロジックが滞留し、Phase 3 の分割も不安定。 | High | 25 | View をダイアログ表示専用に縮退し、`CreateFolderUseCase` / `RenameItemUseCase` / `MoveItemsUseCase` / `DeleteItemsUseCase` の Application Service と、I/O を担う Infrastructure Service に分離する。 |
| A-02 | MVVM 境界違反 | High | `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs` | `CanNavigateUp` / `CanMoveToParentSelection` / `LoadFolderAsync` / `IsJpegFile` | ViewModel が `System.IO` を直接使用し、パス解析と存在確認を自前で行っている。 | 520-527 行の `Directory.GetParent`、586-593 行の `Directory.Exists`、1232-1234 行の `Path.GetExtension` | File Browser の主要ナビゲーションと EXIF 編集可否判定。Service 抽出前に ViewModel テストがファイルシステム前提になる。 | Medium | 15 | パス判定・存在確認・拡張子判定を Service へ移し、ViewModel は抽象結果だけを受け取る。 |
| A-03 | テスト不能要因 | High | `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs` | `ConfigureUiActionHandlers` / `OpenFolderCommand` ほか | ViewModel が `Func<Task>` の UI コールバック注入を前提にしており、ファイル操作コマンドの本体が View に逃げている。hidden dependency である。 | 79-84 行の UI action フィールド、145-162 行の各 Command、558-573 行の `ConfigureUiActionHandlers`、1074-1077 行の `ExecuteUiActionAsync` | Open/Create/Rename/Move/Delete の全コマンド。ViewModel 単体でユースケースを完結できず、テスト対象が分断される。 | Medium | 12 | `Func<Task>` ベースの UI callback を廃止し、ViewModel から Application Service を直接呼ぶ構造へ変更する。View には UI 入出力だけを残す。 |
| A-04 | 非同期不整合 | High | `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneViewModel.cs` | `OnSelectedItemChanged` / `StartBackgroundThumbnailGeneration` | fire-and-forget が残っており、完了待機と失敗観測ができない。 | 1104 行の `_ = LoadMetadataAsync(SelectedItem);`、1293-1299 行の `_ = Task.Run(async () => ...)` | メタデータ表示とサムネイル更新。キャンセルや例外がテストから追えず、選択変更時の競合再現が難しい。 | Medium | 12 | fire-and-forget を段階的に排除し、メタデータ取得とサムネイル生成を明示的な非同期パイプラインへ再編する。 |
| A-05 | MVVM 境界違反 | High | `PhotoGeoExplorer/Panes/Map/MapPaneViewModel.cs` | `MapPaneViewModel` / `OnInitializeAsync` / `TryCreatePinStyle` | ViewModel が `Mapsui.Map`、`Visibility`、`Symbol`、`DispatcherQueue` などの UI 型と、ピン画像のファイル I/O を直接持つ。 | 40-50 行の UI 状態フィールド、73-122 行の `Map` / `Visibility` 公開、151-200 行の `DispatcherQueue` 初期化、502-552 行の `File.Exists` / `Path.Combine` | Map Pane 全体。ヘッドレスな ViewModel 生成ができず、地図ロジックの単体テストが困難。 | High | 15 | 地図描画モデルを UI 非依存 DTO に分離し、MapControl 側で `Mapsui.Map` に変換する。ピン画像解決は Service に移す。 |
| A-06 | MVVM 境界違反 | High | `PhotoGeoExplorer/Panes/Preview/PreviewPaneViewModel.cs` | `PreviewPaneViewModel` / `LoadSelectedPhotoAsync` | ViewModel が `BitmapImage`、`Visibility`、`DispatcherQueue` に依存し、さらに static `ExifService` を直接呼んでいる。 | 32-36 行の UI 型フィールド、63-66 行の `InitializeDispatcherQueue`、285-299 行の `async void` イベント処理、357-385 行の `ExifService.GetMetadataAsync` と UI スレッド更新 | Preview Pane 全体。画像表示状態とメタデータ取得が UI スレッド依存になり、テストで差し替え不能。 | High | 15 | 画像ロード結果とメタデータ要約を Service インターフェース経由にし、ViewModel から `BitmapImage` と `DispatcherQueue` を排除する。 |
| A-07 | 構造的問題 | Medium | `PhotoGeoExplorer/Panes/Settings/SettingsPaneViewModel.cs` | `SettingsPaneViewModel` | Settings Pane が `FileBrowserPaneViewModel` と `MainViewModel` の具象に直接依存し、Pane 間の疎結合を崩している。 | 19-21 行の具象依存、39-46 行のコンストラクタ、316-318 行と 405-415 行の相互参照 | 設定変更時の各 Pane 連携。将来の Pane 分割や設定テストで差し替えが難しい。 | Medium | 6 | `ISettingsSnapshotProvider` や `IFileBrowserSettingsPort` のような抽象ポートに置き換え、Pane 間依存を切る。 |
| A-08 | MVVM 境界違反 | High | `PhotoGeoExplorer/MainWindow.xaml.cs` | `OnOpenSettingsPaneClicked` / `OnOpenLogFolderClicked` / `OnCheckUpdatesClicked` / `ShowMessageDialogAsync` | MainWindow が Shell を超えて、設定 Pane の組み立て、ログフォルダ操作、更新確認、ダイアログ生成まで担っている。 | 313-356 行の `SettingsPaneViewModel` 生成と `ContentDialog` 構築、438-455 行のログフォルダ作成と起動、476-529 行の `UpdateService.CheckForUpdatesAsync`、532-551 行のメッセージダイアログ生成 | Shell 全体。MainWindow の責務が増え続け、Phase 1 の Shell 制約に反する。 | Medium | 9 | 設定ダイアログ起動、ログフォルダ起動、更新確認を Service または MainViewModel の抽象コマンドに移管する。 |
| A-09 | 構造的問題 | Medium | `PhotoGeoExplorer/ViewModels/MainViewModel.cs` | `MainViewModel` / `LoadFolderAsync` / `UpdatePreview` | Pane アーキテクチャ移行後も、旧 MainViewModel にファイル一覧・プレビュー・サムネイル・メタデータ責務が残存している。 | 24-38 行の File Browser/Thumbnail 状態、166-172 行の `Items` / `BreadcrumbItems` / `WorkspaceState`、743-822 行の `LoadFolderAsync`、1033 行の `new BitmapImage(...)`、1692 行の `_ = Task.Run(...)` | 将来の改修で旧経路と新経路が乖離するリスク。責務の重複が Phase 3 の見積りをぶらす。 | Medium | 6 | MainViewModel を Shell 状態専用に縮退し、残存ロジックを Pane ViewModel / Service に移す。 |

---

## C. スコアリング

| ViewModel | スコア | 主な減点理由 | 最初に着手すべき改善 |
|---|---:|---|---|
| `FileBrowserPaneViewModel` | 5 | UI 型依存、hidden dependency、fire-and-forget、直接 I/O | ファイル操作コマンドを View から剥がし、UI 依存のないユースケースへ再配置する |
| `MapPaneViewModel` | 25 | UI 型依存、直接 I/O、UI スレッド依存初期化 | `Mapsui.Map` を View 側へ押し戻し、ViewModel はマーカー・状態 DTO のみを返す |
| `PreviewPaneViewModel` | 30 | UI 型依存、static 外部依存、`async void` イベント処理 | 画像/メタデータ取得を Service 抽象へ移し、UI 反映は View に寄せる |
| `MainViewModel` | 20 | UI 型依存、直接 I/O、fire-and-forget、責務重複 | Shell 用状態に責務を縮退し、旧ファイルブラウザ責務を削除する |
| `SettingsPaneViewModel` | 70 | 具象 ViewModel 依存、Pane 間結合 | 他 Pane との連携を抽象ポート経由へ変更する |

### テスト起点監査メモ

| ViewModel | 書けない/書きづらい理由 |
|---|---|
| `FileBrowserPaneViewModel` | UI 依存、依存注入不可、非同期制御不可、副作用未分離 |
| `MapPaneViewModel` | UI 依存、依存注入不可 |
| `PreviewPaneViewModel` | UI 依存、依存注入不可 |
| `MainViewModel` | UI 依存、副作用未分離、非同期制御不可 |
| `SettingsPaneViewModel` | 依存注入は可能だが、他 ViewModel の具象依存により孤立テストが重い |

---

## Phase 3 に向けた優先順位

1. `A-01` をユースケース単位で分割し、View からファイル操作本体を段階的に撤去する
2. `A-03` を先行して処理し、`Func<Task>` ベースの hidden dependency を除去する
3. `A-02` を処理し、FileBrowser ViewModel の `System.IO` とパス判定を抽象化する
4. `A-04` を処理し、fire-and-forget と非同期競合を整理する
5. `A-05` `MapPaneViewModel` の `Mapsui` / ファイル I/O 依存を外へ出す
6. `A-06` `PreviewPaneViewModel` の `BitmapImage` / `ExifService` 依存を抽象化する
7. `A-08` `A-09` を通じて MainWindow / MainViewModel を Shell 専用へ縮退する
8. `A-07` Settings Pane の Pane 間結合を最後に分離する

### Phase 3 Issue 種別

- View 整理 Issue
- Application Service 導入 Issue
- Infrastructure 抽象化 Issue
- 非同期パイプライン整理 Issue

### Phase 3 Issue 分解案

- `ISSUE-P3-01a`: `IsSamePath` / `IsDescendantPath` を Service へ抽出する
- `ISSUE-P3-01b`: `CreateFolder` を `CreateFolderUseCase` + Infrastructure Service へ移す
- `ISSUE-P3-01c`: `Rename` を `RenameItemUseCase` + Infrastructure Service へ移す
- `ISSUE-P3-01d`: `Move` を `MoveItemsUseCase` + Infrastructure Service へ移す
- `ISSUE-P3-01e`: `Delete` を `DeleteItemsUseCase` + Infrastructure Service へ移す
- `ISSUE-P3-01f`: View から `foreach` ループと件数集約を排除する
- `ISSUE-P3-02`: FileBrowser ViewModel の hidden dependency を除去し、UI callback を廃止する
- `ISSUE-P3-03`: FileBrowser ViewModel の `System.IO` 判定を `IFileSystemService` へ抽象化する
- `ISSUE-P3-04a`: FileBrowser の fire-and-forget を排除する
- `ISSUE-P3-04b`: FileBrowser のメタデータ取得とサムネイル生成を追跡可能な非同期パイプラインへ整理する
- `ISSUE-P3-05`: MapPaneViewModel から `Mapsui.Map` とピン画像解決を分離する
- `ISSUE-P3-06`: PreviewPaneViewModel から `BitmapImage` と EXIF 取得依存を分離する
- `ISSUE-P3-07`: MainWindow の設定ダイアログ、更新確認、ログフォルダ操作を Shell 外へ移す
- `ISSUE-P3-08`: MainViewModel の残存 FileBrowser / Preview 責務を削除する
- `ISSUE-P3-09`: SettingsPaneViewModel の concrete ViewModel 依存をポート化する

### FileBrowser の目標構造

```text
View
  ↓
ViewModel
  ↓
Application Service
  ↓
Infrastructure Service
```

`FileBrowserPaneView` は現在この境界を破っており、実質的に Application Service の役割を担っている。Phase 3 ではまずこの状態を解消する。

---

## 結論

現在のリポジトリは、Pane 分割自体は進んでいるが、`FileBrowserPaneView` と `FileBrowserPaneViewModel` に実処理と UI 依存が集中しており、ここが最優先のボトルネックである。

特に `FileBrowserPaneView.xaml.cs` は View でありながら実質的に Application Service として振る舞っている。ここをユースケース単位で細かく分割して外へ出せない限り、Phase 1 の理想アーキテクチャと Phase 3 の安定した Issue 分割は成立しない。
