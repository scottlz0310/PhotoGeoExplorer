# Pane System フェーズ3: 段階的移行

このフォルダには、Pane System フェーズ3（段階的移行）の ISSUE テンプレートが含まれています。

## 概要

フェーズ2で確立した標準形（構成・命名・DI・テスト）に従い、MainWindow の主要機能を独立した Pane へ段階的に移植します。

## 移行順序

移行は以下の順序で行うことを推奨します：

### 1. Map Pane（01-map-pane-*）
- **難易度**: 中
- **依存度**: WorkspaceState（選択写真の位置情報）
- **理由**: 地図機能は比較的独立しており、外部ライブラリ（Mapsui）との連携が主な責務

### 2. Preview Pane（02-preview-pane-*）
- **難易度**: 中〜高
- **依存度**: WorkspaceState（選択写真）
- **理由**: 画像表示、ズーム/パン、DPIスケーリングなど複雑なロジックの分離

### 3. FileBrowser Pane（03-filebrowser-pane-*）
- **難易度**: 高
- **依存度**: WorkspaceState（現在フォルダ、選択ファイル）、他の Pane との連携
- **理由**: MainWindow の中核機能であり、Map/Preview との連携が密接

## ファイル構成

```
issues/phase3/
├── README.md                      # このファイル
├── 01-map-pane-issue.md           # Map Pane の ISSUE テンプレート
├── 01-map-pane-pr.md              # Map Pane の PR テンプレート
├── 02-preview-pane-issue.md       # Preview Pane の ISSUE テンプレート
├── 02-preview-pane-pr.md          # Preview Pane の PR テンプレート
├── 03-filebrowser-pane-issue.md   # FileBrowser Pane の ISSUE テンプレート
└── 03-filebrowser-pane-pr.md      # FileBrowser Pane の PR テンプレート
```

## 使い方

1. GitHub Issues で新しい Issue を作成
2. 対応する `*-issue.md` の内容をコピー＆ペースト
3. Issue 番号を確認
4. ブランチを作成して作業
5. PR 作成時に対応する `*-pr.md` の内容をコピー＆ペースト
6. `<issue-number>` を実際の番号に置き換え

## 完了条件

フェーズ3が完了すると、以下の状態になります：

- MainWindow は Shell 専任（レイアウトと Pane 配置のみ）
- Map / Preview / FileBrowser が独立した Pane として動作
- WorkspaceState 経由で Pane 間連携が実現
- 各 Pane に対応するテストが存在

## 関連資料

- [docs/Architecture/PaneSystem.md](../../docs/Architecture/PaneSystem.md) - アーキテクチャガイド
- [issues/phase2/](../phase2/) - フェーズ2の参考
- [PhotoGeoExplorer/Panes/Settings/](../../PhotoGeoExplorer/Panes/Settings/) - 標準形の参照実装
