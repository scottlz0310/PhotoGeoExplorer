# Pull Request テンプレート

## 概要

<!-- このPRで何を達成しようとしているか、簡潔に説明してください -->

## 変更の種類

<!-- 該当するものにチェックを入れてください -->

- [ ] Bug fix（バグ修正）
- [ ] New feature（新機能）
- [ ] Breaking change（既存機能の破壊的変更）
- [ ] Refactoring（リファクタリング）
- [ ] Documentation（ドキュメント更新）
- [ ] Dependencies（依存関係の更新）
- [ ] Other（その他）

## チェックリスト

### 基本事項

- [ ] コードがビルドできる
- [ ] 既存のテストが通る
- [ ] 新しいコードにテストを追加した（必要な場合）
- [ ] ドキュメントを更新した（必要な場合）

### アーキテクチャガードレール（必須）

> **注意**: 以下の項目は Pane System アーキテクチャを守るための必須チェックです。
> 詳細は [`docs/Architecture/PaneSystem.md`](docs/Architecture/PaneSystem.md) を参照してください。

- [ ] **MainWindowに新規ロジックを追加していない**
  - [ ] Shell以外のロジック（業務ロジック、状態管理、I/O、イベントハンドラの増殖）を追加していない
  - [ ] 追加した場合は、理由と代替案を「備考」に記載した

- [ ] **新機能は PaneVM/Service に配置した**
  - [ ] 新しいPaneを作成した場合、`PaneViewModelBase` を継承している
  - [ ] ビジネスロジックはServiceに配置した

- [ ] **ペイン間の直接参照を追加していない**
  - [ ] Pane間の連携は `WorkspaceState` または Messenger 経由で実装した
  - [ ] PaneAがPaneBを直接参照していない

- [ ] **ロジック変更と構造変更を混ぜていない**
  - [ ] 1PRで扱う変更は、ロジック変更または構造変更のどちらか一方

## テスト方法

<!-- このPRの変更をどのようにテストしたか説明してください -->

- [ ] ユニットテストを実行した
- [ ] 手動テストを実行した
- [ ] E2Eテストを実行した（該当する場合）

### テストコマンド

```bash
# ビルド
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64

# テスト実行
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64

# フォーマット検証
dotnet format --verify-no-changes PhotoGeoExplorer.sln
```

## スクリーンショット（UI変更の場合）

<!-- UI変更がある場合は、変更前後のスクリーンショットを添付してください -->

## 備考

<!-- 追加の説明、制限事項、今後の課題など -->

## 関連Issue

<!-- 関連するIssueがある場合はリンクしてください -->

Closes #
Relates to #

---

## レビュアーへの確認事項

<!-- レビュアーに特に確認してほしい点があれば記載してください -->

- [ ] アーキテクチャガードレールを満たしているか
- [ ] コードの品質と可読性
- [ ] テストの網羅性
- [ ] ドキュメントの更新
