# MainWindow オーケストレーション残留ロジック調査（更新: 2026-02-06）

## 目的
MainWindow を「Shell（オーケストレーション専任）」に寄せるため、Pane 固有ロジックの残存箇所を整理し、移管状況を明確化する。

## 実施結果サマリー
- Map Pane 固有ロジックを `MainWindow` から `MapPaneViewControl` へ移管完了。
- `MainWindow` は Map と FileBrowser の橋渡し（イベント連携）のみ保持。
- `App.xaml` の `MapPaneView`（ResourceDictionary）を廃止し、Map の UI 構造を一本化。
- Preview Pane の DPI 変更通知を `PreviewPaneViewControl` へ移管。

## ペイン別状況

### Map Pane（完了）
**移管先**: `PhotoGeoExplorer/Panes/Map/MapPaneViewControl.xaml(.cs)`

`MainWindow` から移したロジック:
- マップ UI イベント処理
  - PointerPressed / Moved / Released / CaptureLost
  - Ctrl + ドラッグ矩形選択
  - EXIF 位置選択時のクリック/キャンセル判定
- 矩形選択レイヤー制御
  - `UpdateRectangleSelectionLayer`
  - `ClearRectangleSelectionLayer`
  - `LockMapPan / RestoreMapPanLock`
- マーカークリック/Flyout/外部リンク
  - ヒットテスト
  - Flyout の表示内容構築
  - Google Maps URL 起動
- マップステータス表示の反映
  - `MapPaneViewModel` のステータス値を View に反映

`MainWindow` に残した責務:
- `MapPaneControl` のイベント受信
  - フォーカス要求: `Map -> FileBrowser` の橋渡し
  - 矩形選択結果: `PhotoItem[] -> PhotoListItem[]` への変換と選択反映
  - 通知要求: `InfoBar` 表示
- 設定メニュー経由の制御
  - タイルソース切替
  - 既定ズームレベル変更

### FileBrowser Pane（残）
**対象ファイル**: `PhotoGeoExplorer/MainWindow.xaml.cs`

残っているロジック:
- メニューバー起点の操作ハンドラ
  - `OnNavigateHome/Back/Forward/Up/Refresh`
  - `OnOpenFolderClicked`
  - `OnToggleImagesOnlyClicked`
  - `OnViewModeMenuClicked`
  - `OnResetFiltersClicked`
  - `OnCreateFolderClicked / OnRenameClicked / OnMoveClicked / OnDeleteClicked / OnMoveToParentClicked`

評価:
- いずれも「グローバルメニュー入力を FileBrowserPane に委譲する」Shell 的な橋渡しで、密結合ロジックは小さい。
- 追加改善するなら `FileBrowserPaneView` 側にコマンド集約し、MainWindow のイベントハンドラ数をさらに減らせる。

### Preview Pane（残）
**対象ファイル**: `PhotoGeoExplorer/MainWindow.xaml.cs`

残っているロジック:
- レイアウト制御
  - `TogglePreviewMaximize`
  - `OnMainSplitterDragDelta`
  - `OnMapSplitterDragDelta`

評価:
- レイアウトは Window 全体責務のため MainWindow に残す判断は妥当。
- DPI 通知は `PreviewPaneViewControl` 側へ移管済み。

## その他（Pane 外）
- EXIF 編集ダイアログ生成 (`ShowExifEditDialogAsync`) はまだ MainWindow に残る。
- Help/診断系、設定ロード/保存、言語/テーマ適用はアプリ全体責務として MainWindow 維持で妥当。

## まとめ
Map 領域は「UIイベント + 描画 + EXIFピック + Flyout」を Pane 側へ移し、MainWindow はオーケストレーション専任に近づいた。  
現時点の残タスクは、FileBrowser メニュー操作のさらなるコマンド化、設定メニューの導線整理、EXIF 編集ダイアログの配置整理が中心。

## 次回作業予定（2026-02-07 以降）

### 1. FileBrowser メニュー操作のコマンド化
作業内容:
- `MainWindow` の FileBrowser 系メニューハンドラを `FileBrowserPaneViewModel`（または Pane 側コマンド）へ段階的に移管する。
- まずは副作用の小さい操作（`Refresh` / `NavigateUp` / `ResetFilters`）から着手する。

完了条件:
- `MainWindow.xaml.cs` から対象メソッドを削減できている。
- 既存の操作フロー（メニュー操作 -> 一覧更新）が回帰していない。

### 2. EXIF 編集ダイアログ責務の分離
作業内容:
- `ShowExifEditDialogAsync` と関連状態管理を `MainWindow` から分離する。
- 候補は `PreviewPane` 側の UI オーケストレーションか、専用の Dialog サービス層。

完了条件:
- `MainWindow` から EXIF ダイアログ構築コードが減り、責務が明確化されている。
- EXIF 更新成功/失敗通知の既存挙動を維持している。

### 3. 設定メニューの宙ぶらりん状態の解消
作業内容:
- `Settings (development)` 入口と、MainWindow 直下の個別設定項目（言語/テーマ/地図/Export/Import）が混在している状態を整理する。
- 「設定変更の実行責務」を `SettingsPaneViewModel` 側と MainWindow 側で再定義し、重複実装を解消する。
- 設定メニューのユーザー導線を一本化し、暫定表記（development）の扱いを確定する。

完了条件:
- 設定の操作入口がユーザー視点で一貫している。
- 設定変更処理の責務境界がコードとドキュメントで一致している。

### 4. 境界回帰テストの拡充
作業内容:
- Pane 間連携の回帰テスト（`Map -> FileBrowser` 選択連携、通知連携）を追加する。
- `MapPaneViewControl` のライフサイクル（Loaded/Unloaded）起点の回帰を重点確認する。

完了条件:
- 主要なオーケストレーション境界に対するテストが追加されている。
- `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64` が成功する。

### 5. ドキュメント同期
作業内容:
- 本ドキュメントと `docs/Architecture/PaneSystem.md` の記載整合を更新する。
- PR チェックリストの「アーキテクチャガードレール」観点を反映する。

完了条件:
- 実装完了内容がアーキテクチャ文書に反映され、差分理由を追跡できる。
