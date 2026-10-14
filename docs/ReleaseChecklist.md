# リリースチェックリスト

## 事前準備（タグ作成前）

- [ ] `PhotoGeoExplorer/PhotoGeoExplorer.csproj` の `Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` を更新
- [ ] `PhotoGeoExplorer/Package.appxmanifest` の `Identity Version` を更新（例: `1.2.3.0`）
- [ ] `CHANGELOG.md` に該当バージョンのセクションを追加
- [ ] `PhotoGeoExplorer/Assets/propose/listingData-*.csv` の日英 `ReleaseNotes` を更新（タグの版 `vX.Y.Z` を含めること。Release ワークフローが検証する）
- [ ] リリースノートに含める内容を整理（関連する作業管理文書、docs の要点）
- [ ] `dotnet format --verify-no-changes PhotoGeoExplorer.sln` を実行
- [ ] `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64` を実行
- [ ] `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64` を実行

## リリース実行

> [!IMPORTANT]
> **WACK は Release ワークフローが自動で実行し、合格しなければ Release の作成と Partner Center への提出へ進まない。**
> 判定は報告書の `OVERALL_RESULT` が `PASS`、`PARTIAL_RUN` が `FALSE`、`APP_VERSION` が `Package.appxmanifest` の版と一致すること。
> Optional テスト（既知の `[88] Blocked executables`）の失敗は、`OVERALL_RESULT` に影響しない限り許容する。
> 検査するのは、提出する `.msixupload` と同じ MSIX の中身（Publisher と署名だけ差し替えたコピー）で、提出ファイルそのものではない。

- [ ] `vX.Y.Z` タグを作成して push
- [ ] Release ワークフローが成功したことを確認（`WACK gate` ステップが成功し、artifact `wack-report` と GitHub Release に `wack-report-X.Y.Z.0.xml` が添付されている）
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
   - `ReleaseNotes` は、空、またはタグの版（`vX.Y.Z`）を含まないと Release ワークフローの `Validate release version` で失敗する（パッケージのビルド・提出へ進まない。タグだけが残る）。提出は空の値を上書きしないため、更新漏れだと前回の文が Store に残る。内容が正しいかは検査しない

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
