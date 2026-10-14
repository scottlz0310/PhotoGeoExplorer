# Windows App SDK ランタイム整合タスク（#231）

関連: [Issue #231](https://github.com/scottlz0310/PhotoGeoExplorer/issues/231)

- [x] PR #230 の失敗ログとアーティファクトの取得可否を確認（ログ取得済み、アーティファクトは保存期限経過で取得不可）
- [x] 非パッケージビルドを Windows App SDK self-contained にし、SDK の自動初期化を有効化
- [x] 非同梱ビルドの bootstrap 要求バージョンを SDK 由来に変更
- [x] CI / E2E の外部ランタイム調達を撤去
- [x] 開発者ガイド・E2E 文書・CHANGELOG を更新
- [x] Release/x64 ビルド・単体テスト788件・同梱ランタイムの読み込みを検証する E2E を確認
- [x] Debug/x64 ビルド・format を検証
- [x] MSIX パッケージ生成と明示的な非同梱ビルド・起動を検証

ローカルの既存 `LaunchOpenFolderPreviewMetadataAndMap` は一覧の UIA 取得で失敗。
変更前 main の隔離 worktree でも同一失敗を確認したため、今回の修正の成功証跡には含めない。
CI の全 E2E と独立レビューの完了は PR 上で確認する。
MSIX 生成時はローカルの `mspdbcmf.exe` 不足による symbol packaging warning が1件発生した。

MSIX のランタイム同梱・.NET の self-contained 化・SDK のバージョン変更は対象外。
