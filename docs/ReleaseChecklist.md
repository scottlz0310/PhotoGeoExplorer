# リリースチェックリスト

## 事前準備（タグ作成前）

- [ ] `PhotoGeoExplorer/PhotoGeoExplorer.csproj` の `Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` を更新
- [ ] `PhotoGeoExplorer/Package.appxmanifest` の `Identity Version` を更新（例: `1.2.3.0`）
- [ ] `CHANGELOG.md` に該当バージョンのセクションを追加
- [ ] リリースノートに含める内容を整理（tasks.md の完了/未完了、docs の要点）
- [ ] Store 向けドキュメントを更新（`docs/MicrosoftStore.md`, `docs/WACK-TestResults.md`）

## リリース実行

> [!IMPORTANT]
> **WACK はタグ push・Partner Center 提出より前に実施すること。**
> リリース後に実行しても審査前の品質保証として意味をなさない。

- [ ] `.\scripts\DevInstall.ps1` でローカルビルド＆インストール
- [ ] `.\scripts\RunWackTests.ps1` で WACK を実行し合格を確認
- [ ] `docs/WACK-TestResults.md` に結果を追記
- [ ] `vX.Y.Z` タグを作成して push
- [ ] GitHub Release が自動作成されていることを確認
- [ ] MSIX Bundle アーティファクトが添付されていることを確認
- [ ] リリースノートを手動編集（必要に応じて）
- [ ] Partner Center に提出し、runFullTrust の用途を審査ノートに記載

## Microsoft Store リスティング更新

Partner Center でアプリの説明文やスクリーンショットを更新する場合の手順。

### listingdata.csv のダウンロードと編集

1. **ダウンロード**
   - Partner Center > アプリ > Store リスティング > エクスポート
   - `listingdata.csv` をダウンロード

2. **編集**
   - UTF-8 BOM 付きで保存できるエディタ（VSCode, Excel等）で編集
   - 主な編集対象:
     - `Description`: アプリの説明文（新機能、修正内容を反映）
     - `ReleaseNotes`: 今回のリリースノート
     - `Keywords`: 検索キーワード（SEO対策）
     - `Features`: 主要機能リスト
   - 注意: 改行は `&#x0D;&#x0A;` でエスケープする

3. **アップロード**
   - Partner Center > アプリ > Store リスティング > インポート
   - 編集した `listingdata.csv` をアップロード
   - プレビューで内容を確認

4. **スクリーンショット更新**（必要に応じて）
   - UI に変更がある場合は新しいスクリーンショットを用意
   - 推奨サイズ: 1920x1080 または 1366x768

## リリース後

- [ ] クリーン環境でインストール/起動を確認
- [ ] ランタイム導線（Windows App SDK Runtime）が機能するか確認
- [ ] tasks.md の進捗を更新
- [ ] Microsoft Store の公開/更新を確認
