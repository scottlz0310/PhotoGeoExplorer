# Microsoft Store 運用ガイド

このドキュメントでは、Microsoft Store への継続的なアップデートフローと、初回公開時の準備履歴をまとめています。

## 🔄 継続アップデートフロー

継続的なバージョンアップ、Store 提出、リスティング情報の更新手順です。

### 1. Store 提出用ビルドの作成

開発環境 (`scripts/DevInstall.ps1`) と同じコマンドで、Store 提出用の `msixupload` を生成します。

```powershell
.\scripts\DevInstall.ps1
```

既定で内部の `dotnet publish ... UapAppxPackageBuildMode=StoreUpload` が実行されます。

**生成物**:
- `PhotoGeoExplorer\AppPackages` 配下の `PhotoGeoExplorer_x.y.z.0_x64_bundle.msixupload`
  - これが **Partner Center に提出するファイル** です。
- 同時にローカルインストールも行われるため、動作確認を兼ねることができます。

### 2. WACK (Windows App Certification Kit) テスト

提出前にローカルで検証を行います。以下のスクリプトで、最新のバンドルを自動検出してテストを実行・分析します。

```powershell
.\scripts\RunWackTests.ps1
```

**手動手順**:
1. 生成された `msixbundle` を検出
2. `appcert.exe` で Store App プロファイルのテストを実行
3. 結果 (XML) を `scripts/wack_reports/` に保存し、合否を表示

結果サマリーを `docs/WACK-TestResults.md` に追記してください。

### 3. Store リスティングの準備 (Assets & Listing Data)

Release ワークフローは、リポジトリ内の Listing Data CSV を Partner Center の申請データへ反映します。
タグ作成前に、リリースノートと必要なアセットを更新してください。

#### アセットの準備
- `PhotoGeoExplorer/Assets/propose/` フォルダを作業用として使用
- 新機能の **スクリーンショット**（1920x1080 推奨）を配置
- 必要に応じてファイル名を連番などに整理

#### Listing Data (CSV) の更新
Partner Center から現在の情報をエクスポートし、リポジトリ内の CSV と差分を確認してからリポジトリ内の CSV を修正します。

1. **エクスポート**: Partner Center > アプリ > Store リスティング > エクスポートから `listingdata.csv` をダウンロード
2. **編集**: リポジトリ内の `PhotoGeoExplorer/Assets/propose/listingData-*.csv` を編集（VS Code 等の UTF-8 BOM 対応エディタ推奨）
   - `Description`: バージョンアップ内容を反映
   - `ReleaseNotes`: 今回の変更点
   - CSV フィールド内の改行と引用符を壊さない
3. **コミット**:
   - `propose` フォルダ内の CSV と必要な画像ファイルをリリース準備 PR に含める
   - UTF-8 BOM + CRLF を維持する

### 4. Release ワークフローと Partner Center

1. WACK 合格後に `vX.Y.Z` タグを push
2. `.github/workflows/release.yml` が MSIX Store Upload パッケージを生成
3. GitHub Release を作成し、`msixupload` を添付
4. `Submit-ToPartnerCenter.ps1` がパッケージと Listing Data を Partner Center の申請データへ送信
5. Partner Center でパッケージ、リスティング、審査ノートを確認
6. runFullTrust の用途を審査ノートに記載し、認定を手動で開始

ワークフローは既存の pending submission を自動削除しません。pending submission がある場合は失敗するため、内容を確認してから Partner Center で手動削除します。

---

## 📅 初回公開・準備履歴 (Reference)

以下は初回公開時 (v1.5.0頃) のチェックリストと設定記録です。

### 公開状況

- 公開済み: https://apps.microsoft.com/detail/9P0WNR54441B
- 公開バージョンは Partner Center で確認する（文書へ固定値を記録しない）
- Store ID: 9P0WNR54441B

### Partner Center 設定

#### アプリ登録

- [x] Microsoft Partner Center アカウント登録完了
- [x] アプリ名の予約完了（PhotoGeoExplorer）
- [x] Partner Center から Identity 情報を取得
  - [x] `Package/Identity/Name`: `scottlz0310.PhotoGeoExplorer`
  - [x] `Package/Identity/Publisher`: `CN=39FB3D39-1F1A-4B82-B081-47469FD12CA6`
  - [x] `Package/Properties/PublisherDisplayName`: `scottlz0310`
  - [x] PFN: `scottlz0310.PhotoGeoExplorer_r99jq8jxntmym`
  - [x] Store ID: `9P0WNR54441B`

### アプリ情報

- [x] カテゴリ選択（写真 & ビデオ）
- [x] 年齢制限の設定
- [x] プライバシーポリシー URL の準備

#### プライバシーポリシー（Cloudflare Pages）

- [x] `docs/privacy-policy.html` を作成（完了）
- [x] Cloudflare Pages プロジェクトを作成
  - [x] Project: `photogeoexplorer`
  - [x] Production branch: `main`
  - [x] デプロイは Cloudflare Pages の Git 連携で自動実行
  - 詳細は `docs/GitHubPagesSetup.md` を参照
- [x] 公開 URL を Partner Center の「プライバシーポリシー URL」に設定
  - 例: `https://photogeoexplorer.pages.dev/privacy-policy`
- [x] ルート URL でも到達できることを確認
  - `https://photogeoexplorer.pages.dev/` → `privacy-policy.html` に転送

## アセット準備

### アプリアイコン（MSIX パッケージ用）

- [x] Square44x44Logo.png（44×44 px）
- [x] Square71x71Logo.png（71×71 px）
- [x] Square150x150Logo.png（150×150 px）
- [x] Square310x310Logo.png（310×310 px）
- [x] Wide310x150Logo.png（310×150 px）
- [x] StoreLogo.png（50×50 px）
- [x] SplashScreen.png（620×300 px）

### Store 掲載用アセット

- 提出用アセットと CSV は `PhotoGeoExplorer/Assets/propose/` に集約する
- [x] スクリーンショット（最低1枚、推奨3-4枚）
  - [x] screenshot1.png（1186×793 px）
  - [x] screenshot2.png（1186×793 px）
  - [ ] screenshot3.png（推奨）
  - [ ] screenshot4.png（推奨）
- [ ] プロモーション用画像（オプション）
  - [ ] 1920×1080 px（ヒーロー画像）
  - [ ] 2400×1200 px（機能グラフィック）

#### listingData.csv インポート手順

- 新規アセットを含む場合は「フォルダー単位のインポート」を使う（CSV単体では画像参照が失敗する）。
- ルートフォルダー名を含む相対パスで記載する（例: `propose/screenshot1.png`）。
- フォルダー直下に `listingData.csv` と画像を配置し、`propose` を丸ごと選択してアップロードする。
- `listingData.csv` は UTF-8 (BOM あり) + CRLF で保存する（LF だと行が連結されて無効扱いになる）。
- Excel で開いたまま再保存しない（改行やエンコーディングが崩れることがある）。

## アプリパッケージ

### Package.appxmanifest 設定

- [x] Description を英語で記載
- [x] アセット参照を更新
- [x] Partner Center の Identity 情報に更新
- [x] ShowNameOnTiles を設定
- [x] DPI awareness (perMonitorV2) を設定

### MSIX ビルド

- [x] ローカルでの MSIX パッケージビルドテスト
- [x] コード署名証明書の取得（テスト用の自己署名を利用）
- [x] 署名済み MSIX パッケージの作成
- [x] Windows App Cert Kit でのテスト

#### Store 提出用パッケージビルド

以下のコマンドで WACK テスト用の msixbundle と Store 提出用の msixupload を同時に生成します。

```powershell
dotnet publish .\PhotoGeoExplorer\PhotoGeoExplorer.csproj -c Release -p:Platform=x64 `
  -p:WindowsPackageType=MSIX `
  -p:GenerateAppxPackageOnBuild=true `
  -p:UapAppxPackageBuildMode=StoreUpload `
  -p:AppxBundle=Always `
  -p:AppxBundlePlatforms=x64 `
  -p:AppxPackageSigningEnabled=false `
  -p:AppxSymbolPackageEnabled=false
```

生成物（`PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_<version>_Test\` 配下）:

| ファイル | 用途 |
|---------|------|
| `PhotoGeoExplorer_<version>_x64.msixbundle` | WACK テスト用 |
| `PhotoGeoExplorer_<version>_x64_bundle.msixupload` | Partner Center 提出用 |

#### WACK テスト実行

Windows App Cert Kit を起動し、生成された `msixbundle` を選択してテストを実行します。

結果サマリーは `docs/WACK-TestResults.md` に記録します。

#### ローカル動作確認用（任意）

Store提出用のパッケージをローカルでサイドローディングして動作確認するには、以下のコマンドを使用します。
ビルドから署名、インストールまでを自動化しています。

```powershell
.\scripts\DevInstall.ps1
```

既存の `msixupload` を再利用する場合は、`-ReuseBuild` を指定します。

### CI/CD パイプライン

- [x] GitHub Actions での MSIX ビルドと GitHub Release 作成
- [x] Partner Center へのパッケージ・Listing Data 送信
- [ ] Partner Center での内容確認と認定開始（手動）

## Store 申請準備

### 申請情報

- [x] アプリの説明（短い説明、詳細な説明）
  - [x] 日本語
  - [x] 英語
- [ ] 依存関係の説明文を追記
  - このアプリを実行するには .NET Desktop Runtime が必要です。Microsoft Store からのインストール時に自動的に提供されます。
- [x] 検索キーワード（最大7個）
- [x] サポート連絡先情報
- [x] Webサイト URL

### 法的文書

- [x] プライバシーポリシー作成（完了）
- [x] プライバシーポリシー公開（Cloudflare Pages 連携が必要）
- [ ] 利用規約（オプション）
- [x] サポートページ

### テスト

- [x] クリーンな Windows 環境でのインストールテスト
- [x] Windows App Cert Kit でのテスト合格
- [x] 審査ノートの準備（テスト手順など）

## リリース後

- [x] 審査承認の確認
- [x] Store での公開確認
- [x] README.md に Microsoft Store バッジを追加
- [x] ユーザーフィードバックの監視を開始
- [ ] 定期的なアップデート計画

## 参考リンク

- [Microsoft Partner Center](https://partner.microsoft.com/dashboard)
- [Microsoft Store - PhotoGeoExplorer](https://apps.microsoft.com/detail/9P0WNR54441B)
- [Windows アプリ認定キット](https://developer.microsoft.com/windows/downloads/windows-app-certification-kit/)
- [MSIX パッケージング](https://docs.microsoft.com/windows/msix/)
