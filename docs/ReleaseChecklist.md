# リリースチェックリスト

## 事前準備（タグ作成前）

- [ ] `PhotoGeoExplorer/PhotoGeoExplorer.csproj` の `Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` を更新
- [ ] `PhotoGeoExplorer/Package.appxmanifest` の `Identity Version` を更新（例: `1.2.3.0`）
- [ ] `CHANGELOG.md` に該当バージョンのセクションを追加
- [ ] `PhotoGeoExplorer/Assets/propose/listingData-*.csv` の日英 `ReleaseNotes` を更新
- [ ] リリースノートに含める内容を整理（関連する作業管理文書、docs の要点）
- [ ] `dotnet format --verify-no-changes PhotoGeoExplorer.sln` を実行
- [ ] `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64` を実行
- [ ] `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64` を実行

## リリース実行

> [!IMPORTANT]
> **WACK はタグ push・Partner Center 提出より前に実施すること。**
> リリース後に実行しても審査前の品質保証として意味をなさない。

- [ ] `.\scripts\DevInstall.ps1` でローカルビルド＆インストール
- [ ] `.\scripts\RunWackTests.ps1` で WACK を実行し合格を確認
- [ ] `docs/WACK-TestResults.md` に結果を追記
- [ ] `vX.Y.Z` タグを作成して push
- [ ] Release ワークフローが成功したことを確認
- [ ] GitHub Release に `msixupload` が添付され、CHANGELOG の該当セクションが掲載されていることを確認
- [ ] Partner Center にパッケージとリスティングが反映されたことを確認
- [ ] Partner Center の審査ノートに runFullTrust の用途を記載し、認定を手動で開始

## Microsoft Store リスティング更新

Release ワークフローは、リポジトリ内の `PhotoGeoExplorer/Assets/propose/listingData-*.csv` を Partner Center へ送信する。
説明文やスクリーンショットも更新する場合は、タグ作成前に CSV とアセットを更新する。

### listingData CSV の更新

1. **エクスポート**
   - Partner Center > アプリ > Store リスティング > エクスポート
   - エクスポートした `listingdata.csv` とリポジトリ内の CSV の差分を確認

2. **編集**
   - リポジトリ内の `PhotoGeoExplorer/Assets/propose/listingData-*.csv` を UTF-8 BOM + CRLF で編集
   - 主な編集対象:
     - `Description`: アプリの説明文（新機能、修正内容を反映）
     - `ReleaseNotes`: 今回のリリースノート
     - `Keywords`: 検索キーワード（SEO対策）
     - `Features`: 主要機能リスト
   - CSV フィールド内の改行と引用符を壊さない

3. **反映確認**
   - タグ push 後、Release ワークフローが CSV を申請データへ反映する
   - Partner Center のプレビューで内容を確認してから認定を開始

4. **スクリーンショット更新**（必要に応じて）
   - UI に変更がある場合は新しいスクリーンショットを用意
   - 推奨サイズ: 1920x1080 または 1366x768

## リリース後

- [ ] クリーン環境でインストール/起動を確認
- [ ] ランタイム導線（Windows App SDK Runtime）が機能するか確認
- [ ] tasks.md の進捗を更新
- [ ] Microsoft Store の公開/更新を確認
