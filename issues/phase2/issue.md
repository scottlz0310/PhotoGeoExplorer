# Issue: Pane System フェーズ2（設定 Pane 移植の標準化）

## 概要
Pane System のフェーズ2として、既存の設定UIを独立Paneへ移植し、標準的な構成・命名・DIの型を確立します。
Phase1で導入したベース（`IPaneViewModel` / `PaneViewModelBase` / `WorkspaceState`）を利用し、
「次からこの型で増やせば良い」状態を完成させることが目的です。

## 背景
- PR#71 で Phase1（基盤構築）が完了
- `docs/Architecture/PaneSystem.md` のフェーズ2に合わせて、標準形を決定する必要がある

## 目的
- 設定 Pane を実運用レベルへ移植
- MainWindow の責務をShellに限定
- Pane/Service/ViewModel の標準形を確定

## 対応方針
1. 設定 Pane の実装を追加（既存のSettingsメニューから分離）
2. ViewModel / Service の責務分離
3. DIの登録・解決パターンを明文化
4. テストを追加（SettingsPaneViewModelの単体テスト）

## 作業項目
- [ ] SettingsPaneViewModel を実装（状態管理とコマンド）
- [ ] SettingsPaneService を作成しI/Oを分離
- [ ] MainWindow から設定ロジックを分離
- [ ] DataTemplate/Pane切替をMainWindowへ統合
- [ ] 既存設定UIとの導線整理（Menu/Command）
- [ ] 単体テストの追加
- [ ] `docs/Architecture/PaneSystem.md` のフェーズ2完了更新

## 受け入れ条件
- 設定関連の新規ロジックがMainWindowに追加されていない
- 設定 Pane が独立して初期化/破棄できる
- テストが1件以上追加される
- `PaneSystem.md` のフェーズ2が完了状態になる

## 関連資料
- `docs/Architecture/PaneSystem.md`
- `PhotoGeoExplorer/Panes/Settings/`
