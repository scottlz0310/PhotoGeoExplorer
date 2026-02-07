# AGENTS.md

> [!IMPORTANT]
> このファイルは `AgentGuidelineSource.md` を正本として `scripts/Sync-AgentDocs.ps1` で自動生成されています。
> 共通ルールの変更は `AgentGuidelineSource.md` を編集し、`pwsh ./scripts/Sync-AgentDocs.ps1` を実行してください。
> 手編集は `<!-- BEGIN TOOL-SPECIFIC -->` と `<!-- END TOOL-SPECIFIC -->` の間だけ許可します。

## 対象
- Codex CLI / Cline などの汎用 AI エージェント

## 共通ガイドライン（自動生成）
<!-- BEGIN SHARED -->
# AIエージェント共通ガイドライン（正本）

このファイルは、`AGENTS.md` / `CLAUDE.md` / `.github/copilot-instructions.md` の共通ルールを管理する正本（Single Source of Truth）です。

## 基本方針
- AI エージェントの指示は分散管理しません。共通ルールはこのファイルのみを更新します。
- 共通ルール更新後は `pwsh ./scripts/Sync-AgentDocs.ps1` で配布先ファイルを再生成します。
- 配布先ファイルで手編集を許可するのは「固有補足」ブロックのみです。

## 言語とコミュニケーション
- このリポジトリでは日本語を使用します。
- AI エージェントの返答は日本語で行います。
- ドキュメントは日本語で記述します。
- コミットメッセージは日本語で記述します。
- 英語ドキュメントを見つけた場合は、日本語版を作成して置き換えます。

## プロジェクト概要
- PhotoGeoExplorer は、写真の位置情報（GPS/EXIF）を地図上に表示する Windows デスクトップアプリです。
- 地図描画は Mapsui を使用します。
- WebView2 は主にヘルプ表示などの補助用途で使用します。
- 配布は Microsoft Store（MSIX）を基準とし、MSI インストーラー配布は廃止済みです。

## プロジェクト構成
- `PhotoGeoExplorer/`: WinUI 3 アプリ本体（XAML + code-behind）
- `PhotoGeoExplorer.Core/`: プラットフォーム非依存のコアロジック
- `PhotoGeoExplorer/Models`, `Services`, `ViewModels`: UI 層のドメインロジックと MVVM
- `PhotoGeoExplorer/Panes/Map`: Mapsui ベースの地図 UI/ロジック
- `PhotoGeoExplorer/wwwroot/`: WebView2 で読み込む静的ファイル（ヘルプ等）
- `PhotoGeoExplorer.Tests/`: xUnit テスト
- `PhotoGeoExplorer.E2E/`: E2E テスト
- `docs/`: ドキュメント

## ビルド・実行・テスト
- 通常ビルド: `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64`
- ローカル実行: `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64`
- テスト実行: `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`
- フォーマット: `dotnet format PhotoGeoExplorer.sln`

### パッケージ作成・インストール（MSIX）
- `.\\scripts\\DevInstall.ps1 -Build`: ビルド、署名、インストールを一括実行
- `.\\scripts\\DevInstall.ps1`: 既存ビルドの再インストール・署名のみ
- `.\\scripts\\DevInstall.ps1 -Clean`: アンインストールとクリーンアップ

### WACK テスト
- `.\\scripts\\RunWackTests.ps1`: ローカルインストールパッケージに対して WACK テストを実行

## 開発時の確認サイクル
1. コード変更
2. フォーマットとビルド確認: `dotnet format; dotnet build -c Release -p:Platform=x64`
3. クイック動作確認: `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64`
4. 実機インストール確認（リリース前）: `.\\scripts\\DevInstall.ps1 -Build`
5. `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` の確認

## コーディングスタイルと命名規則
- C# は 4 スペースインデント。既存の `.editorconfig` と既存実装に合わせます。
- 型と公開メンバーは `PascalCase`、ローカル/引数は `camelCase`、private フィールドは `_field` 形式です。
- public / internal / protected の識別子にアンダースコアは使いません（CA1707 対応）。
- 非同期メソッド名は `Async` で終えます（例: `InitializeMapAsync`）。
- XAML 要素名は `PascalCase` を使用します（例: `MapStatusText`）。
- 解析は厳格です（`AnalysisLevel=latest`、警告はエラー扱い）。

## テスト方針
- テストプロジェクトは `PhotoGeoExplorer.Tests`（xUnit）と `PhotoGeoExplorer.E2E` です。
- E2E を実行する場合は `PHOTO_GEO_EXPLORER_RUN_E2E=1` を指定します。

## コミット・PR ガイドライン
- コミットメッセージはコンベンショナルコミット形式を使います。
- 依存更新は `chore(deps): ... (#NN)`（Renovate 形式）を使います。
- PR には要約、理由、検証方法（コマンド/ログ）を含めます。
- UI 変更はスクリーンショットを添付し、関連 Issue があればリンクします。
- ソースコードまたはドキュメントなどプロジェクト成果物に変更を加えた場合は、必ず `CHANGELOG.md` を更新します。

## ブランチ運用
- 原則として `main` に直接コミットせず、サブブランチで作業して PR でマージします。
- 大きいタスクは先に Issue を作成して整理します。
- ドキュメント更新やリリース準備は `main` で直接作業しても構いません。

## セキュリティと設定
- ログは `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` に出力し、起動時にリセットします。
- Release ワークフローは `v1.2.0` のようなタグで `win-x64` 向け MSIX Bundle（`.msixupload`）を作成します。
- バージョン更新時のチェック項目は `docs/ReleaseChecklist.md` を参照します。

## バージョン調査の注意
- AI から見て不自然に新しいバージョンに感じても、勝手にバージョンダウンしないでください。
- 学習時期のタイムラグを前提に、必要に応じて Web で最新情報を確認します。
<!-- END SHARED -->

## 固有補足（手編集可）
<!-- BEGIN TOOL-SPECIFIC -->
- 固有指示が必要な場合のみ、このブロックに追記してください。
<!-- END TOOL-SPECIFIC -->
