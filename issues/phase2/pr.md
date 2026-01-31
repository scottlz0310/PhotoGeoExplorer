# PR: Pane System フェーズ2（設定 Pane 移植の標準化）

## 概要
Phase1で導入したPane基盤を使い、設定 Pane の移植を通して標準形（構成・命名・DI・テスト）を確立する。

## 変更内容
- SettingsPaneViewModel の状態管理とコマンド実装
- SettingsPaneService を追加
- MainWindow から設定ロジックを分離
- Pane切替と DataTemplate の統合
- 単体テスト追加（SettingsPaneViewModel）
- `docs/Architecture/PaneSystem.md` のフェーズ2完了更新

## 目的 / 理由
- MainWindow を Shell 専任に保つ
- 次のPane移植のテンプレートを確立する

## テスト
```bash
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~SettingsPaneViewModelTests -c Release -p:Platform=x64
```

## 関連 Issue
- (このPRで作成する Phase2 Issue)
