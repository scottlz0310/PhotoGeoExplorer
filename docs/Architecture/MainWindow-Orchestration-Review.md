# MainWindow オーケストレーション残留ロジック調査（更新: 2026-02-06）

## 目的
MainWindow を「Shell（オーケストレーション専任）」に寄せるため、Pane 固有ロジックの残存箇所を整理し、移管状況を明確化する。

## 実施結果サマリー
- Map Pane 固有ロジックを `MainWindow` から `MapPaneViewControl` へ移管完了。
- `MainWindow` は Map と FileBrowser の橋渡し（イベント連携）のみ保持。
- `App.xaml` の `MapPaneView`（ResourceDictionary）を廃止し、Map の UI 構造を一本化。

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
- DPI 変更通知
  - `OnXamlRootChanged` から `PreviewPaneViewModel.OnRasterizationScaleChanged` を呼び出し

評価:
- レイアウトは Window 全体責務のため MainWindow に残す判断は妥当。
- DPI 通知は `PreviewPaneViewControl` 側へ寄せる余地あり。

## その他（Pane 外）
- EXIF 編集ダイアログ生成 (`ShowExifEditDialogAsync`) はまだ MainWindow に残る。
- Help/診断系、設定ロード/保存、言語/テーマ適用はアプリ全体責務として MainWindow 維持で妥当。

## まとめ
Map 領域は「UIイベント + 描画 + EXIFピック + Flyout」を Pane 側へ移し、MainWindow はオーケストレーション専任に近づいた。  
現時点の残タスクは、FileBrowser メニュー操作のさらなるコマンド化と、Preview の DPI 通知責務の再配置が中心。
