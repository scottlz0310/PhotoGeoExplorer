# PR: Pane System フェーズ3-1（Map Pane の移植）

## 概要
フェーズ2で確立した標準形を使い、Map Pane の移植を行います。
MainWindow から地図表示関連のロジックを分離し、MapPaneViewModel / MapPaneService へ集約します。

## 変更内容
- MapPaneViewModel の作成（地図状態管理、マーカー表示、ズーム制御）
- MapPaneService の作成（タイルキャッシュ管理、座標計算）
- MapPaneView.xaml の作成（DataTemplate）
- MainWindow から地図関連コードを移動
- WorkspaceState との連携を実装
- 単体テスト追加（MapPaneViewModelTests, MapPaneServiceTests）
- `docs/Architecture/PaneSystem.md` の移行計画を更新

## 目的 / 理由
- MainWindow を Shell 専任に保つ
- 地図機能の責務を明確化し、テスト可能にする
- WorkspaceState を通じた Pane 間連携の実例を示す

## テスト
```bash
# 単体テスト
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~MapPaneViewModelTests -c Release -p:Platform=x64
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~MapPaneServiceTests -c Release -p:Platform=x64

# 全テスト
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

## 動作確認
- [ ] 地図が正常に表示される
- [ ] 写真選択時にマーカーが表示される
- [ ] マーカークリックでツールチップが表示される
- [ ] タイルソースの切り替えが動作する
- [ ] 地図のズーム/パンが動作する
- [ ] 矩形選択が動作する

## 関連 Issue
（<issue-number> を実際の番号に置き換える）
- Closes #<issue-number>

## チェックリスト
- [ ] MainWindow に新規ロジックを追加していない
- [ ] 新機能は MapPaneViewModel / MapPaneService に入れた
- [ ] ペイン間の直接参照を追加していない（WorkspaceState 経由）
- [ ] ロジック変更と構造変更を混ぜていない
