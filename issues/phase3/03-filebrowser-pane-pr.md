# PR: Pane System フェーズ3-3（FileBrowser Pane の移植）

## 概要
フェーズ2で確立した標準形を使い、FileBrowser Pane の移植を行います。
MainWindow からファイルブラウザ関連のロジックを分離し、FileBrowserPaneViewModel / FileBrowserPaneService へ集約します。

## 変更内容
- FileBrowserPaneViewModel の作成（ファイル一覧状態管理、選択、検索、フィルタ）
- FileBrowserPaneService の作成（ファイルシステム操作、サムネイル生成、EXIF読み取り）
- FileBrowserPaneView.xaml の作成（DataTemplate）
- MainWindow からファイルブラウザ関連コードを移動
- WorkspaceState との連携を実装
- 単体テスト追加（FileBrowserPaneViewModelTests, FileBrowserPaneServiceTests）
- `docs/Architecture/PaneSystem.md` の移行計画を更新

## 目的 / 理由
- MainWindow を Shell 専任に保つ
- ファイルブラウザ機能の責務を明確化し、テスト可能にする
- WorkspaceState を通じて Map/Preview Pane と選択状態を連携

## テスト
```bash
# 単体テスト
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileBrowserPaneViewModelTests -c Release -p:Platform=x64
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileBrowserPaneServiceTests -c Release -p:Platform=x64

# 全テスト
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

## 動作確認
- [ ] ファイル一覧が正常に表示される
- [ ] フォルダナビゲーション（ダブルクリック、パンくず、戻る/進む）が動作する
- [ ] ファイル選択（単一/複数）が動作する
- [ ] 検索/フィルタが動作する
- [ ] 表示切替（リスト/グリッド/詳細）が動作する
- [ ] ファイル操作（新規フォルダ、移動、リネーム、削除）が動作する
- [ ] サムネイルが非同期で表示される
- [ ] 自然順ソートが正常に動作する

## 関連 Issue
（<issue-number> を実際の番号に置き換える）
- Closes #<issue-number>

## チェックリスト
- [ ] MainWindow に新規ロジックを追加していない
- [ ] 新機能は FileBrowserPaneViewModel / FileBrowserPaneService に入れた
- [ ] ペイン間の直接参照を追加していない（WorkspaceState 経由）
- [ ] ロジック変更と構造変更を混ぜていない
