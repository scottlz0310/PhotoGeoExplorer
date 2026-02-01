# Issue: Pane System フェーズ3-2（Preview Pane の移植）

## 概要
Pane System のフェーズ3として、Preview（画像プレビュー）機能を MainWindow から独立した Pane へ移植します。
フェーズ2で確立した標準形（構成・命名・DI・テスト）に従い、PreviewPaneViewModel / PreviewPaneService を実装します。

## 背景
- PR#71 でフェーズ1（基盤構築）が完了
- PR でフェーズ2（設定Pane移植）が完了し、標準形が確立
- `docs/Architecture/PaneSystem.md` のフェーズ3に該当

## 引き継ぎメモ（PR#82 後）
- **WorkspaceState 連携は未配線**（MapPaneViewModel は `UpdateMarkersFromSelectionAsync(...)` 経由で選択を受け取る設計）。Phase3-2 で PreviewPane を移植する際に、**選択写真の共有経路を統一する設計**を決めること。
- `UserAgentProvider` が追加済み。外部アクセス時の User-Agent は **同一プロバイダを再利用**すること。
- MapPane の初期化は **DispatcherQueue 未登録環境で安全にスキップ**する実装になっている。PreviewPane でも **テスト環境/ヘッドレス環境対策**を検討すること。
- テストは副作用を避けるため、**I/O を発生させるパスは注入可能にする方針**（MapPaneService でキャッシュルート注入済み）。PreviewPane の I/O も同様に設計すること。

## 目的
- Preview 機能を独立した Pane として移植
- MainWindow から画像プレビュー関連のロジックを分離
- 画像表示、ズーム/パン、最大化、ナビゲーションなどの責務を PreviewPaneViewModel / PreviewPaneService へ集約

## 対応方針
1. PreviewPaneViewModel を作成（画像表示状態管理、ズーム/パン制御、ナビゲーション）
2. PreviewPaneService を作成（画像ロード、フィッティング計算などの処理）
3. MainWindow から画像プレビュー関連のロジックを段階的に分離
4. WorkspaceState との連携（選択された写真情報を共有）
5. 単体テストを追加

## 作業項目
- [ ] PreviewPaneViewModel を作成
  - [ ] 画像の表示/非表示制御
  - [ ] ズーム/パン操作
  - [ ] 最大化/通常サイズ切替
  - [ ] 前後ナビゲーション
- [ ] PreviewPaneService を作成
  - [ ] 画像ロード処理
  - [ ] フィッティング計算
  - [ ] DPI スケーリング対応
- [ ] PreviewPaneView.xaml を作成（DataTemplate）
- [ ] MainWindow から画像プレビュー関連コードを移動
- [ ] WorkspaceState との連携を実装
- [ ] 単体テストを追加（PreviewPaneViewModelTests, PreviewPaneServiceTests）
- [ ] `docs/Architecture/PaneSystem.md` の移行計画を更新

## 受け入れ条件
- 画像プレビュー関連の新規ロジックが MainWindow に追加されていない
- PreviewPane が独立して初期化/クリーンアップできる
- 既存の画像プレビュー機能（ズーム、パン、最大化、ナビ）が正常に動作する
- マルチモニター環境での DPI スケーリングが正常に動作する
- テストが追加されている
- WorkspaceState 経由で選択写真を取得できる

## 技術的な注意点
- `ScrollViewer` のズーム/パン操作は UI スレッドでの操作が必要
- DPI スケーリング対応（`XamlRoot.Changed` イベントでの再フィット）
- 画像の非同期ロードとキャンセル処理

## 関連資料
- `docs/Architecture/PaneSystem.md`
- `PhotoGeoExplorer/Panes/Settings/` - フェーズ2の参照実装
- `PhotoGeoExplorer/MainWindow.xaml.cs` - 現在の画像プレビュー実装
