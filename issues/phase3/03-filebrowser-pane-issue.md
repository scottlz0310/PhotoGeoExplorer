# Issue: Pane System フェーズ3-3（FileBrowser Pane の移植）

## 概要
Pane System のフェーズ3として、FileBrowser（ファイルブラウザ）機能を MainWindow から独立した Pane へ移植します。
フェーズ2で確立した標準形（構成・命名・DI・テスト）に従い、FileBrowserPaneViewModel / FileBrowserPaneService を実装します。

## 背景
- PR#71 でフェーズ1（基盤構築）が完了
- PR でフェーズ2（設定Pane移植）が完了し、標準形が確立
- `docs/Architecture/PaneSystem.md` のフェーズ3に該当

## 目的
- FileBrowser 機能を独立した Pane として移植
- MainWindow からファイルブラウザ関連のロジックを分離
- フォルダナビゲーション、ファイル一覧、検索、ファイル操作などの責務を FileBrowserPaneViewModel / FileBrowserPaneService へ集約

## 対応方針
1. FileBrowserPaneViewModel を作成（ファイル一覧状態管理、選択管理、検索、フィルタ）
2. FileBrowserPaneService を作成（ファイルシステム操作、サムネイル生成、EXIF読み取り）
3. MainWindow からファイルブラウザ関連のロジックを段階的に分離
4. WorkspaceState との連携（現在のフォルダパス、選択ファイルを共有）
5. 単体テストを追加

## 作業項目
- [ ] FileBrowserPaneViewModel を作成
  - [ ] ファイル一覧の取得と表示
  - [ ] フォルダナビゲーション（パンくず、戻る/進む）
  - [ ] ファイル選択（単一/複数）
  - [ ] 検索/フィルタ機能
  - [ ] 表示切替（リスト/グリッド/詳細）
- [ ] FileBrowserPaneService を作成
  - [ ] ファイル一覧取得
  - [ ] サムネイル生成
  - [ ] EXIF/GPS 情報読み取り
  - [ ] ファイル操作（新規フォルダ、移動、リネーム、削除）
- [ ] FileBrowserPaneView.xaml を作成（DataTemplate）
- [ ] MainWindow からファイルブラウザ関連コードを移動
- [ ] WorkspaceState との連携を実装
- [ ] 単体テストを追加（FileBrowserPaneViewModelTests, FileBrowserPaneServiceTests）
- [ ] `docs/Architecture/PaneSystem.md` の移行計画を更新

## 受け入れ条件
- ファイルブラウザ関連の新規ロジックが MainWindow に追加されていない
- FileBrowserPane が独立して初期化/クリーンアップできる
- 既存のファイルブラウザ機能（ナビゲーション、検索、ファイル操作）が正常に動作する
- 自然順ソート（Natural Sort）が正常に動作する
- テストが追加されている
- WorkspaceState 経由で選択ファイルを Map/Preview Pane と共有できる

## 技術的な注意点
- サムネイル生成は非同期で行い、プレースホルダーを先に表示
- ファイル操作はキャンセル可能にする
- 自然順ソート（`NaturalStringComparer`）の使用
- 大量ファイル時のパフォーマンス考慮（仮想化、遅延ロード）

## 関連資料
- `docs/Architecture/PaneSystem.md`
- `PhotoGeoExplorer/Panes/Settings/` - フェーズ2の参照実装
- `PhotoGeoExplorer/MainWindow.xaml.cs` - 現在のファイルブラウザ実装
- `PhotoGeoExplorer.Core/Services/FileSystemService.cs` - ファイルシステム操作
- `PhotoGeoExplorer.Core/Services/ThumbnailService.cs` - サムネイル生成
