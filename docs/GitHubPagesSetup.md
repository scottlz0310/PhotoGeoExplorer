# Cloudflare Pages セットアップガイド

このドキュメントでは、PhotoGeoExplorer の静的ページ（`privacy-policy` / `help`）を Cloudflare Pages へ配信する手順を説明します。

## 前提条件

- Cloudflare Pages に `photogeoexplorer` プロジェクトを作成済みであること
- Production branch が `main` に設定されていること
- Root directory が `docs` に設定されていること

## デプロイ方式

- GitHub Actions は使用せず、Cloudflare Pages の Git 連携による自動デプロイを使用します。
- `main` への push（または merge）で Cloudflare 側のデプロイが実行されます。

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

### URL が 404 になる

- Cloudflare Pages の最新デプロイが成功しているか確認してください。
- `docs/privacy-policy.html` / `docs/help/index*.html` が `main` に反映済みか確認してください。

### ヘルプが外部表示されない

- ネットワーク到達性を確認してください。
- アプリログに `External help page load failed. Falling back to local help content.` が出ていないか確認してください。
