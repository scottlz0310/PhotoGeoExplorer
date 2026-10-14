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

> [!NOTE]
> **PR の段階でも、WACK の結果に影響しうる変更では `WACK` ワークフロー（`.github/workflows/wack.yml`）が同じ判定を実行する**（Windows App SDK の更新、`Package.appxmanifest`・`PhotoGeoExplorer.csproj`・`app.manifest`・`Assets/`（`Assets/propose/` を除く）・`Directory.Build.props`・`global.json`・WACK 関連スクリプトの変更）。
> ビルドは `scripts/Build-MsixUpload.ps1`、判定は `scripts/Invoke-WackGate.ps1` / `scripts/WackGate.psm1` を Release と共有する。対象外の PR では実行されない。
> PR 時の合格は、タグ時の合格を保証しない（対象外の変更や、ランナーの更新で結果が変わり得る）。**Release ワークフローの `WACK gate` が、提出前の最終的なゲートである。**
> 手動で実行する場合は、Actions の `WACK` ワークフローを `workflow_dispatch` で実行する。

- [ ] `vX.Y.Z` タグを作成して push
- [ ] Release ワークフローが成功したことを確認（`WACK gate` ステップが成功し、artifact `wack-report` と GitHub Release に `wack-report-X.Y.Z.0.xml` が添付されている）
- [ ] GitHub Release に `msixupload` が添付され、CHANGELOG の該当セクションが掲載されていることを確認
- [ ] `Submit to Partner Center` ステップが成功したことを確認（commit 後は認定へ自動で進む。**認定を手動で開始する必要はない**）
- [ ] Partner Center で申請が認定へ進行していること（`PreProcessing` → `Certification`）と、パッケージ・リスティングが反映されていることを確認

### 審査ノートの確認（変更時のみ）

`runFullTrust` の用途を記載した審査ノートは、初回の submission で設定済みで、以降の submission へ引き継がれる。毎回の作業ではなく、次のときに見直す。

- 新しい capability を追加したとき
- Partner Center 側で審査ノートがクリアされたとき
- 審査で差し戻され、説明の追記を求められたとき

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
   - タグ push 後、Release ワークフローが CSV を申請データへ反映し、commit まで自動で行う（commit 後は認定へ自動で進む）
   - 内容の確認は、タグを push する前に済ませる。push 後に Partner Center で確認できるのは反映結果と認定の進行状況

4. **スクリーンショット更新**（必要に応じて）
   - UI に変更がある場合は新しいスクリーンショットを用意
   - 推奨サイズ: 1920x1080 または 1366x768

## リリース後

- [ ] クリーン環境でインストール/起動を確認
- [ ] ランタイム導線（Windows App SDK Runtime）が機能するか確認
- [ ] tasks.md の進捗を更新
- [ ] Microsoft Store の公開/更新を確認
