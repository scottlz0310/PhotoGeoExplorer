# Microsoft Store 公開準備

このドキュメントは、Microsoft Store 公開に向けた準備項目のチェックリストです。

## Partner Center 設定

### アプリ登録

- [x] Microsoft Partner Center アカウント登録完了
- [x] アプリ名の予約完了（PhotoGeoExplorer）
- [ ] Partner Center から Identity 情報を取得
  - [ ] `Package/Identity/Name`（例: `12345PhotoGeoExplorer.PhotoGeoExplorer`）
  - [ ] `Package/Identity/Publisher`（例: `CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX`）

### アプリ情報

- [ ] カテゴリ選択（例: 写真 & ビデオ）
- [ ] 年齢制限の設定
- [ ] プライバシーポリシー URL の準備

#### プライバシーポリシー（GitHub Pages）

- [ ] `docs/privacy-policy.html` を公開（GitHub Pages）
  - [ ] GitHub の Settings → Pages → **Build and deployment**
    - Source: **Deploy from a branch**
    - Branch: `main`
    - Folder: `/docs`
  - [ ] 公開 URL を Partner Center の「プライバシーポリシー URL」に設定
    - 例: `https://scottlz0310.github.io/PhotoGeoExplorer/privacy-policy.html`
  - [ ] ルート URL でも到達できることを確認
    - `https://scottlz0310.github.io/PhotoGeoExplorer/` → `privacy-policy.html` に転送

## アセット準備

### アプリアイコン（MSIX パッケージ用）

- [x] Square44x44Logo.png（44×44 px）
- [x] Square71x71Logo.png（71×71 px）
- [x] Square150x150Logo.png（150×150 px）
- [ ] Square310x310Logo.png（310×310 px）**要作成**
- [x] Wide310x150Logo.png（310×150 px）
- [x] StoreLogo.png（50×50 px）
- [x] SplashScreen.png（1240×600 px）

### Store 掲載用アセット

- [x] スクリーンショット（最低1枚、推奨3-4枚）
  - [x] screenshot1.png（1186×793 px）
  - [x] screenshot2.png（1186×793 px）
  - [ ] screenshot3.png（推奨）
  - [ ] screenshot4.png（推奨）
- [ ] プロモーション用画像（オプション）
  - [ ] 1920×1080 px（ヒーロー画像）
  - [ ] 2400×1200 px（機能グラフィック）

## アプリパッケージ

### Package.appxmanifest 設定

- [x] Description を英語で記載
- [x] アセット参照を更新
- [ ] Partner Center の Identity 情報に更新
- [x] ShowNameOnTiles を設定

### MSIX ビルド

- [ ] ローカルでの MSIX パッケージビルドテスト
- [ ] コード署名証明書の取得（Store 経由 or 独自証明書）
- [ ] 署名済み MSIX パッケージの作成
- [ ] Windows App Cert Kit でのテスト

### CI/CD パイプライン

- [ ] GitHub Actions での MSIX ビルドワークフロー作成
- [ ] Partner Center への自動アップロード設定（オプション）

## Store 申請準備

### 申請情報

- [ ] アプリの説明（短い説明、詳細な説明）
  - [ ] 日本語
  - [ ] 英語
- [ ] 検索キーワード（最大7個）
- [ ] サポート連絡先情報
- [ ] Webサイト URL

### 法的文書

- [ ] プライバシーポリシー作成・公開
- [ ] 利用規約（オプション）
- [ ] サポートページ

### テスト

- [ ] クリーンな Windows 環境でのインストールテスト
- [ ] Windows App Cert Kit でのテスト合格
- [ ] 審査ノートの準備（テスト手順など）

## リリース後

- [ ] 審査承認の確認
- [ ] Store での公開確認
- [ ] README.md に Microsoft Store バッジを追加
- [ ] ユーザーフィードバックの監視
- [ ] 定期的なアップデート計画

## 参考リンク

- [Microsoft Partner Center](https://partner.microsoft.com/dashboard)
- [Windows アプリ認定キット](https://developer.microsoft.com/windows/downloads/windows-app-certification-kit/)
- [MSIX パッケージング](https://docs.microsoft.com/windows/msix/)
