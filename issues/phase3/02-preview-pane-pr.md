# PR: Pane System フェーズ3-2（Preview Pane の移植）

## 概要
フェーズ2で確立した標準形を使い、Preview Pane の移植を行います。
MainWindow から画像プレビュー関連のロジックを分離し、PreviewPaneViewModel / PreviewPaneService へ集約します。

## 変更内容
- PreviewPaneViewModel の作成（画像表示状態管理、ズーム/パン制御、ナビゲーション）
- PreviewPaneService の作成（画像ロード、フィッティング計算）
- PreviewPaneView.xaml の作成（DataTemplate）
- MainWindow から画像プレビュー関連コードを移動
- WorkspaceState との連携を実装
- 単体テスト追加（PreviewPaneViewModelTests, PreviewPaneServiceTests）
- `docs/Architecture/PaneSystem.md` の移行計画を更新

## 目的 / 理由
- MainWindow を Shell 専任に保つ
- 画像プレビュー機能の責務を明確化し、テスト可能にする
- DPI スケーリング対応などの複雑なロジックを分離

## テスト
```bash
# 単体テスト
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~PreviewPaneViewModelTests -c Release -p:Platform=x64
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~PreviewPaneServiceTests -c Release -p:Platform=x64

# 全テスト
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

## 動作確認
- [ ] 画像プレビューが正常に表示される
- [ ] ズーム/パンが動作する
- [ ] 最大化/通常サイズ切替が動作する
- [ ] 前後ナビゲーションが動作する
- [ ] マルチモニター環境でのDPIスケーリングが正常

## 関連 Issue
（<issue-number> を実際の番号に置き換える）
- Closes #<issue-number>

## チェックリスト
- [ ] MainWindow に新規ロジックを追加していない
- [ ] 新機能は PreviewPaneViewModel / PreviewPaneService に入れた
- [ ] ペイン間の直接参照を追加していない（WorkspaceState 経由）
- [ ] ロジック変更と構造変更を混ぜていない
