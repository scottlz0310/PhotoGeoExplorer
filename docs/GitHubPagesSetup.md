# Cloudflare Pages セットアップガイド

このドキュメントでは、PhotoGeoExplorer の静的ページ（`privacy-policy` / `help`）を Cloudflare Pages へデプロイする運用手順を説明します。

## 前提条件

- Cloudflare Pages に `photogeoexplorer` プロジェクトを作成済みであること
- Production branch が `main` に設定されていること
- GitHub Actions Secrets に以下を設定済みであること
  - `CLOUDFLARE_API_TOKEN`
  - `CLOUDFLARE_ACCOUNT_ID`

## デプロイ方式

- デプロイは GitHub Actions の `cloudflare-pages-deploy.yml` で実行します。
- 以下のファイルが更新されたときのみデプロイを実行します。
  - `docs/index.html`
  - `docs/privacy-policy.html`
  - `PhotoGeoExplorer/wwwroot/help/index.html`
  - `PhotoGeoExplorer/wwwroot/help/index.en.html`

これにより、アプリ本体の通常変更では Cloudflare デプロイが発火しません。

## デプロイ内容

ワークフローはリポジトリ内の静的ファイルから一時的な配信用ディレクトリを組み立て、Cloudflare Pages へ反映します。

- `/` → `docs/index.html`
- `/privacy-policy` / `/privacy-policy.html` → `docs/privacy-policy.html`
- `/help/index.html` → `PhotoGeoExplorer/wwwroot/help/index.html`
- `/help/index.en.html` → `PhotoGeoExplorer/wwwroot/help/index.en.html`

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

### デプロイが失敗する

- `CLOUDFLARE_API_TOKEN` / `CLOUDFLARE_ACCOUNT_ID` の値を確認してください。
- Cloudflare 側プロジェクト名が `photogeoexplorer` であることを確認してください。

### プライバシーポリシー URL が 404 になる

- GitHub Actions の `Cloudflare Pages Deploy` 実行結果を確認してください。
- `docs/privacy-policy.html` が `main` に反映済みか確認してください。

### ヘルプが外部表示されない

- ネットワーク到達性を確認してください。
- アプリログに `External help page load failed. Falling back to local help content.` が出ていないか確認してください。
