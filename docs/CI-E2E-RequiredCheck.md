# E2E Required Check 化の運用手順

この文書は、`E2E` ワークフローを常時実行した後に、Branch protection へ Required check として追加するための手順をまとめたものです。

## 目的

- PR で E2E の回帰を確実に検知する。
- 不安定な期間に Required 化して開発を止めることを避ける。

## Required 化の判断基準

- 直近 2 週間で `E2E` が安定していること。
- `main` 向け PR での `E2E` が連続 20 回以上成功していること。
- 失敗の主因がテストフレークではなく、実際の不具合であることを確認できる状態であること。

## 適用手順（GitHub UI）

1. `Settings` → `Branches` → 対象ブランチ（`main`）の Branch protection rule を開く。
2. `Require status checks to pass before merging` を有効化する。
3. Required status checks に `E2E / e2e` を追加する。
4. 必要に応じて `Require branches to be up to date before merging` も有効化する。
5. 設定保存後、テスト用PRで Required check が機能することを確認する。

## ロールバック手順

1. 同じ Branch protection rule を開く。
2. Required status checks から `E2E / e2e` を一時的に外す。
3. 失敗ログ（artifact）を元に原因を修正し、安定後に再度追加する。
