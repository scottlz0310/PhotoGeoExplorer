# Cloudflare Pages セットアップガイド

このドキュメントでは、PhotoGeoExplorer の静的ページ（`privacy-policy` / `help`）を Cloudflare Pages へ配信する手順を説明します。

## 前提条件

- Cloudflare Pages に `photogeoexplorer` プロジェクトを作成済みであること
- Production branch が `main` に設定されていること
- Root directory が `docs` に設定されていること

## デプロイ方式

- デプロイに GitHub Actions は使用せず、Cloudflare Pages の Git 連携による自動デプロイを使用します。
- `main` への push（または merge）で Cloudflare 側のデプロイが実行されます。
- GitHub Actions は、公開内容の監視（読み取りのみ）にだけ使います（下記「公開内容の監視」）。

## 公開内容の監視

Cloudflare Pages の Git 連携は GitHub の外で動くため、push の通知が届かなくなってもチェックが赤くならず、古い内容が公開され続けます（2026-02-27 から 2026-11-14 まで、約 8 か月気付きませんでした）。

- `.github/workflows/published-docs.yml` が、公開サイトの配信対象（`/`、`/privacy-policy`、`/help/index.html`、`/help/index.en.html`）を取得し、`main` の `docs` と比較します（改行コード・BOM・末尾の空白は無視）。一致しなければワークフローが失敗します。
- 実行のタイミング: `main` への `docs/**` の push（デプロイ待ちのため最大 10 分・1 分おきに再試行）と、毎日 03:17（JST）の定期実行（Cloudflare 側の変化を、`docs` を変えなくても検知）。
- 手元での確認: `pwsh ./scripts/Test-PublishedDocs.ps1`。
- 失敗したら、Cloudflare の Deployments で、最新のデプロイの有無と成否を確認してください。

## 配信対象

`docs` 配下の以下ファイルが配信対象です。

- `/` → `docs/index.html`
- `/privacy-policy` → `docs/privacy-policy/index.html`
- `/privacy-policy.html` → `docs/privacy-policy.html`
- `/help/index.html` → `docs/help/index.html`
- `/help/index.en.html` → `docs/help/index.en.html`

> [!NOTE]
> `docs/help/index*.html` はアプリ同梱ヘルプ（`PhotoGeoExplorer/wwwroot/help/index*.html`）と同じ内容を維持してください。

## 確認 URL

- ルート: `https://photogeoexplorer.pages.dev/`
- プライバシーポリシー: `https://photogeoexplorer.pages.dev/privacy-policy`
- ヘルプ（日本語）: `https://photogeoexplorer.pages.dev/help/index.html`
- ヘルプ（英語）: `https://photogeoexplorer.pages.dev/help/index.en.html`

## アプリ側フォールバック方針

- アプリのヘルプ表示は外部 URL を優先します。
- 外部ヘルプの読み込みに失敗した場合は、ローカル同梱の `wwwroot/help/index*.html` へ自動フォールバックします。
- フォールバック時は `%LocalAppData%\PhotoGeoExplorer\Logs\app.log` に情報ログを出力します。

## トラブルシューティング

### `main` に push しても自動デプロイが始まらない

- 監視ワークフロー（上記）が失敗します。Cloudflare 側の Settings → Builds で、Git リポジトリの接続と Production branch を確認してください。
- 以前のアカウントに、同じリポジトリを接続した GitHub アプリ（Cloudflare Workers and Pages）が残っていると、新しい接続が「使用中」になり、push の通知も届かなくなることがあります。古いアカウント側でアプリをアンインストールしてから、プロジェクトを作り直します（2026-11-14 に発生）。
- 作り直すときは、Workers 側の作成フローではなく、Pages（「Pages に続行」）から作成してください。Workers 側では `*.pages.dev` の URL が得られません。

### PR に Cloudflare Bot のコメントが付いてレビューが止まる

- Cloudflare Pages の連携は、PR にコメント（`cloudflare-workers-and-pages[bot]`）を投稿します。reviewer 側の信頼リストにないと、コメント取得が拒否されてレビューが止まります。
- 信頼リストは、サーバー側（Mcp-Docker の `REVIEW_RAVEN_TRUSTED_COMMENT_AUTHORS`）で管理します。リポジトリ内には置きません。

### URL が 404 になる

- Cloudflare Pages の最新デプロイが成功しているか確認してください。
- `docs/privacy-policy.html` / `docs/help/index*.html` が `main` に反映済みか確認してください。

### ヘルプが外部表示されない

- ネットワーク到達性を確認してください。
- アプリログに `External help page load failed. Falling back to local help content.` が出ていないか確認してください。
