# MainWindow オーケストレーション残留ロジック調査（更新: 2026-02-20）

## 目的
MainWindow を「Shell（オーケストレーション専任）」に収束させる取り組み（ISSUE #95）の最終状態を記録する。

## 実施結果サマリー
- MainWindow スリム化第2期（PR-1〜PR-8）を完了。
- `MainWindow.xaml.cs` は **619 行**（目標: 800 行以下）を達成。
- メニュー操作、設定、ヘルプ、EXIF 編集、起動処理は Pane / Service / Coordinator へ移管済み。
- MainWindow はレイアウト制御・Window ライフサイクル・Pane 間の最小イベント橋渡しに限定。

## ペイン別状況

### Map Pane（完了）
**主担当**: `PhotoGeoExplorer/Panes/Map/MapPaneViewControl.xaml(.cs)` / `MapPaneViewModel.cs`

- Map UI イベント、矩形選択、Flyout、EXIF 位置選択は Pane 側で完結。
- MainWindow は `WorkspaceState.PhotoSelectionRequested` / `NotificationRequested` の受信と UI 反映のみ保持。

### FileBrowser Pane（完了）
**主担当**: `PhotoGeoExplorer/Panes/FileBrowser/FileBrowserPaneView.xaml(.cs)` / `FileBrowserPaneViewModel.cs`

- File / View / Settings メニューの主要操作は `FileBrowserPaneViewModel` の Command バインディングで実行。
- MainWindow から FileBrowser 専用の Click リレー群は撤去済み。

### Preview Pane（完了）
**主担当**: `PhotoGeoExplorer/Panes/Preview/PreviewPaneViewControl.xaml(.cs)` / `PreviewPaneViewModel.cs`

- プレビュー表示・Fit・DPI 連動は Pane 側へ移管済み。
- MainWindow に残る `TogglePreviewMaximize` / スプリッタ処理はウィンドウ全体レイアウト責務として維持。

## その他責務の整理状況
- **設定**: `SettingsCoordinator` に集約済み。
- **ヘルプ**: `HelpService` に集約済み。
- **起動処理**: `StartupCoordinator` に集約済み。
- **EXIF 編集**: `ExifEditorService` に集約済み。

## 完了判定
以下を満たしたため、MainWindow スリム化（第2期）は完了と判定する。

- MainWindow は Shell 責務中心で、Pane 固有ロジックを保持しない。
- 行数目標（800 行以下）を達成（実測 619 行）。
- 関連文書（PaneSystem / MainWindowSlimdown-Plan / CHANGELOG）を同期済み。
