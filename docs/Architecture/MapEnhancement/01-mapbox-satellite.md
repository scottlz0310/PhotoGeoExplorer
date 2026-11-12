# 設計: Mapbox 航空写真（BYO Token）

- 状態: 設計（実装未着手）
- 追跡 Issue: #291（親: #290）
- 概要・依存関係: [README.md](README.md)

## 1. 目的と範囲

Esri World Imagery に加えて、Mapbox Satellite を背景地図として選べるようにする。アクセストークンはアプリに埋め込まず、利用者が自分の Mapbox アカウントで発行したものを設定画面で入力する（BYO Token）。

範囲外: Mapbox のベクタースタイル、Satellite Streets（ラベル入り）、ジオコーディングなど他の Mapbox API。

## 2. 現状

- 背景は `MapTileSourceType`（`OpenStreetMap` / `EsriWorldImagery`、`PhotoGeoExplorer.Core/Models/AppSettings.cs`）で選ぶ。
- `MapPaneService.CreateTileLayer` が BruTile の `HttpTileSource` と `FileCache`（`%LocalAppData%\PhotoGeoExplorer\Cache\Tiles\<ソース名>`、拡張子 `png` 固定）を組み立てる。
- `MapPaneViewModel.SwitchTileSource` が背景レイヤーを差し替える。
- 設定 UI は `SettingsPaneView.xaml` の `ComboBox`（`MapTileSourceIndex`）。

## 3. 設計判断

### 3.1 エンドポイント: Raster Tiles API

```
https://api.mapbox.com/v4/mapbox.satellite/{z}/{x}/{y}.jpg90?access_token={token}
```

- 欲しいのは航空写真そのもので、スタイルを合成する Static Tiles API（`/styles/v1/mapbox/satellite-v9/tiles/...`）である必要がない。
- 写真には JPEG が向く（PNG より転送量が小さい）。
- 第 1 段階は 256px タイルとし、既存の `GlobalSphericalMercator` スキーマをそのまま使う。`@2x`（512px）は高 DPI での鮮明さが必要になった時点で別途検討する。
- 料金体系と無料枠は API ごとに異なり変わりうるため、実装時に Mapbox の現行ドキュメントで確認し、ヘルプに案内を書く。

### 3.2 トークンの保存: 資格情報ロッカー

- `ISecretStore`（`Get` / `Set` / `Remove`）を `Services` に定義し、実装は WinRT の `Windows.Security.Credentials.PasswordVault` を使う。テストではインメモリ実装を注入する。
- `AppSettings`（`settings.json`）にはトークンを書かない。設定ファイルは不具合報告などで共有されうるため、機密と通常設定を分ける。
- DPAPI（`ProtectedData`）でも保護はできるが、暗号化済みの値を `settings.json` に置く形になり、ファイルの分離ができない。`PasswordVault` は追加の NuGet 依存が要らない。

### 3.3 トークンの検証

- 受け付けるのは公開トークン（`pk.` で始まるもの）だけにする。シークレットトークン（`sk.`）は入力時に拒否し、理由を表示する。クライアントアプリに置くべきでない強い権限のトークンを誤って保存させないため。
- 前後の空白は除去する。形式の検証は `SettingsPaneViewModel` ではなく、テスト可能な純粋関数（`MapboxAccessToken.TryParse`）として Core に置く。
- 「接続テスト」ボタンで 1 枚（`z=0`）を取得し、成功／認証エラー（401・403）／通信エラーを区別して表示する。

### 3.4 タイルのキャッシュ: 永続化しない（ハンドオフから変更）

ハンドオフは既存の `FileCache` による保存を想定していたが、**Mapbox ではディスクへの永続キャッシュを既定で行わない**。

- Mapbox の利用規約は、地図コンテンツの保存・キャッシュを制限している。OSM／Esri と同じ無期限のディスクキャッシュは、規約に反するおそれがある。
- Mapsui／BruTile のメモリキャッシュはそのまま使う（セッション内の再表示は速い）。
- 実装時に現行の規約を確認し、端末上の一時キャッシュが明確に許される範囲（保持期間など）が分かった場合だけ、その範囲に収まる形で有効化する。有効化する場合も、`FileCache` の拡張子をソースごとに指定できるようにする（現状は `png` 固定、Mapbox は `jpg`）。

### 3.5 ログのサニタイズ

- トークンは URL のクエリに載るため、URL を含む例外メッセージ経由でログに出うる。
- `AppLog` の書き込み直前に `access_token=<値>` を `access_token=***` に置換する処理を入れる（呼び出し側の注意に頼らない）。Mapsui／BruTile の内部ログをアプリのログへ流している経路がある場合は、そこも同じ置換を通す。
- クラッシュレポート（`CrashReportService`）にも同じ置換を適用する。

## 4. 変更箇所

| 層 | 変更 |
|---|---|
| Core `Models/AppSettings.cs` | `MapTileSourceType.MapboxSatellite = 2` を追加（既存値は変えない） |
| Core | `MapboxAccessToken`（形式検証）、`AppLog` のサニタイズ |
| `Services` | `ISecretStore` / `PasswordVaultSecretStore` |
| `Panes/Map/MapPaneService.cs` | `CreateMapboxSatelliteLayer`。トークンは引数で受け取る（Service が保存先を直接読まない） |
| `Panes/Map/MapPaneViewModel.cs` | `SwitchTileSource` にトークン未設定時の扱いを追加（§5） |
| `Panes/Settings` | トークン入力欄（`PasswordBox`）、取得方法へのリンク、接続テスト、削除ボタン。選択肢に「Mapbox 航空写真」 |
| ドキュメント | ヘルプ（トークンの取得手順・料金は利用者負担である旨）、プライバシーポリシー、`CHANGELOG.md` |

`IMapPaneService.CreateTileLayer(MapTileSourceType, string userAgent)` は、背景ごとの追加パラメータ（今回はトークン）を渡せるよう、引数をレコード（`MapBackgroundRequest`）にまとめる。ベクター地図（[02](02-vector-renderer.md)）でも同じ入口を使う。

## 5. 振る舞い

| 状況 | 振る舞い |
|---|---|
| トークン未設定 | 選択肢「Mapbox 航空写真」は無効表示（「要トークン設定」）。 |
| トークン設定済み | 選択できる。選択すると即時に背景を切り替える（既存と同じ）。 |
| 設定は Mapbox だがトークンがない（削除後・別 PC へ設定を移した等） | OSM で表示し、地図のステータスに「Mapbox のトークンが未設定のため OpenStreetMap を表示しています」と出す。保存済みの設定値は書き換えない。 |
| 認証エラー（401・403） | タイルは空のままにせず、ステータスに「トークンが無効、または権限がありません」と出す。自動で他の背景へ切り替えない。 |
| 通信エラー | 既存の背景と同じ扱い。 |

## 6. 帰属表示

- 文言: `© Mapbox © OpenStreetMap Improve this map`（リンクつき）。衛星画像の提供元表記が要る場合は Mapbox の現行ガイドに従う。
- Mapbox のワードマーク（ロゴ）の表示が求められるため、地図上と PNG 保存の両方に出す。ロゴ素材の利用条件を実装時に確認する。
- 既存の OSM／Esri は BruTile の `Attribution` に文言を持たせているだけなので、画面と PNG に帰属を描く共通の仕組み（Mapsui のウィジェット）をこの Issue で整え、既存の 2 ソースも同じ経路に乗せる。

## 7. テスト

- `MapboxAccessToken.TryParse`: `pk.` 受理、`sk.` 拒否、空・空白・前後空白をパラメータ化テストで検証。
- ログのサニタイズ: `access_token` を含む文字列・例外メッセージが置換されること。
- `MapPaneService`: Mapbox レイヤーの URL テンプレートと、永続キャッシュを付けないこと。
- `MapPaneViewModel`: トークンなしで Mapbox を指定したときに OSM で表示し、設定値を書き換えないこと。
- `SettingsPaneViewModel`: トークンの保存・削除で選択肢の有効／無効が変わること（`ISecretStore` はインメモリ）。
- 手動: 実トークンでの表示、無効トークンでのエラー表示、PNG 保存の帰属表示（`docs/ManualTestChecklist.md` に追加）。

## 8. 受け入れ条件

- [ ] 設定画面でトークンを保存・削除でき、`settings.json` にトークンが含まれない
- [ ] トークン設定済みのとき Mapbox 航空写真を背景に選べ、写真ピンの位置が他の背景と一致する
- [ ] `sk.` トークンを保存できない
- [ ] `app.log` とクラッシュレポートにトークンが出ない
- [ ] 画面と保存した PNG に Mapbox の帰属表示が出る
- [ ] ヘルプ・プライバシーポリシー・`CHANGELOG.md` が更新されている

## 9. 実装時に確認すること

- Mapbox の現行利用規約におけるキャッシュの扱い（§3.4）と帰属表示・ロゴの要件（§6）
- Raster Tiles API の料金・無料枠（ヘルプの記述用）
- Microsoft Store の審査上、外部サービスのトークン入力に追加の申告が要るか（`docs/MicrosoftStore.md`）
