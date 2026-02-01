# Issue: Pane System フェーズ3-1（Map Pane の移植）

## 概要
Pane System のフェーズ3として、Map（地図表示）機能を MainWindow から独立した Pane へ移植します。
フェーズ2で確立した標準形（構成・命名・DI・テスト）に従い、MapPaneViewModel / MapPaneService を実装します。

## 背景
- PR#71 でフェーズ1（基盤構築）が完了
- PR でフェーズ2（設定Pane移植）が完了し、標準形が確立
- `docs/Architecture/PaneSystem.md` のフェーズ3に該当

## 目的
- Map 機能を独立した Pane として移植
- MainWindow から地図関連のロジックを分離
- 地図表示、マーカー管理、タイルキャッシュなどの責務を MapPaneViewModel / MapPaneService へ集約

## 対応方針
1. MapPaneViewModel を作成（地図状態管理、マーカー表示、ズーム制御）
2. MapPaneService を作成（タイルキャッシュ管理、座標計算などのI/O処理）
3. MainWindow から地図関連のロジックを段階的に分離
4. WorkspaceState との連携（選択された写真の位置情報を共有）
5. 単体テストを追加

## 作業項目
- [ ] MapPaneViewModel を作成
  - [ ] 地図の初期化処理
  - [ ] マーカー表示/更新ロジック
  - [ ] ズーム/パン制御
  - [ ] タイルソース切り替え
- [ ] MapPaneService を作成
  - [ ] タイルキャッシュ管理
  - [ ] 座標計算ユーティリティ
- [ ] MapPaneView.xaml を作成（DataTemplate）
- [ ] MainWindow から地図関連コードを移動
- [ ] WorkspaceState との連携を実装
- [ ] 単体テストを追加（MapPaneViewModelTests, MapPaneServiceTests）
- [ ] `docs/Architecture/PaneSystem.md` の移行計画を更新

## 受け入れ条件
- 地図関連の新規ロジックが MainWindow に追加されていない
- MapPane が独立して初期化/クリーンアップできる
- 既存の地図機能（マーカー表示、タイル切替、キャッシュ）が正常に動作する
- テストが追加されている
- WorkspaceState 経由で選択写真の位置情報を取得できる

## 技術的な注意点
- Mapsui の `MapControl` は UI スレッドでの操作が必要
- タイルキャッシュは `%LocalAppData%\PhotoGeoExplorer\MapCache` を使用
- 写真選択との連携は WorkspaceState 経由で行う

## 関連資料
- `docs/Architecture/PaneSystem.md`
- `PhotoGeoExplorer/Panes/Settings/` - フェーズ2の参照実装
- `PhotoGeoExplorer/MainWindow.xaml.cs` - 現在の地図実装
