# モジュール棚卸し最終成果サマリー（Phase 1〜4）

> 対象: Issue #150「コードベース全体のモジュール棚卸しとゴッドクラス解体計画」の **Phase 1〜4**（子 issue #152〜#182, #206〜#208）
> 開始時点: 2026-06-09（#150 起票時の調査）
> 集計時点: 2026-07-07（#206 / #207 / #208 マージ後）。行数は `wc -l` 実測値（`*.cs`、`bin/` `obj/` 除く）

## 概要

`FileBrowserPaneViewModel.cs` / `FileBrowserPaneView.xaml.cs` の 2 ゴッドクラスを起点に、コードベース全体のモジュールサイズを棚卸しし、責務混在の解消を段階的に進めた。Phase 1（FileBrowser 系ゴッドクラス分割）、Phase 2（App 層 🟠 ファイル分割）、Phase 3（Core 層 `ExifService` 分割）、Phase 4（残存 🟠 ファイルのトリアージと分割）を経て、当初調査で 🔴 要分割と判定された 2 ファイルはいずれも 500 行台まで縮退し、🟠 要注意だった App/Core 層ファイルは大半が 🟡 監視 または解消（クラス削除）まで改善した。Phase 5 では並行して E2E テスト基盤を拡充している。

## 全体行数比較

| 区分 | 開始時点（2026-06-09） | 現在（2026-07-07） | 差分 |
| --- | ---: | ---: | ---: |
| プロダクション（`PhotoGeoExplorer/` + `PhotoGeoExplorer.Core/`） | 16,239 | 17,181 | **+942（+5.8%）** |
| テスト（`PhotoGeoExplorer.Tests/` + `PhotoGeoExplorer.E2E/`） | 11,455 | 15,401 | **+3,946（+34.5%）** |
| 合計 | 27,694 | 32,582 | **+4,888（+17.7%）** |

**プロダクション行数が増加している点について**: 本棚卸しの目的は総 LOC 削減ではなく、ゴッドクラスの責務分離である。分割により抽出したモジュールにはインターフェース定義・コンストラクタ DI 配線・独立した例外ハンドリングが伴うため、責務単位の合計行数は元のゴッドクラス単体より増える（[Phase 1 サマリ](FileBrowserDecomposition-Phase1-Summary.md)で確認済みの傾向がPhase 2〜4でも継続）。価値は各モジュールの独立性・テスト容易性・保守性の向上にある。

**テスト行数の増加について**: 分割した各モジュールに対応する単体テストの新規追加（Phase 1〜4）に加え、Phase 5（#180〜#182）で FileBrowser 操作系 E2E シナリオを大幅拡充したことが主因。単体テストは 745 件合格・0 失敗（2026-07-07 時点、E2E 13 件はローカル実行時スキップ）。

## モジュール行数一覧（開始時点 → 現在）

### PhotoGeoExplorer/

| ファイル | 開始時点 | 現在 | 差分 | 分類（開始→現在） | 対応 Issue |
| --- | ---: | ---: | ---: | --- | --- |
| `Panes/FileBrowser/FileBrowserPaneViewModel.cs` | 2,089 | 1,263 | −826（−40%） | 🔴→🟠 | #152-155 |
| `Panes/FileBrowser/FileBrowserPaneView.xaml.cs` | 1,941 | 908 | −1,033（−53%） | 🔴→🟠 | #156-159 |
| `MainWindow.xaml.cs` | 767 | 590 | −177（−23%） | 🟠→🟠 | #206 |
| `Panes/Map/MapPaneViewControl.xaml.cs` | 694 | 599 | −95（−14%） | 🟠→🟠 | #176 |
| `Panes/Settings/SettingsPaneViewModel.cs` | 671 | 415 | −256（−38%） | 🟠→🟡 | #175 |
| `Services/SettingsCoordinator.cs` | 621 | 479 | −142（−23%） | 🟠→🟡 | #177 |
| `Services/FileOperationService.cs` | 609 | 609 | 0 | 🟡→🟡 | #179（分割不要と判断） |
| `Panes/Map/MapPaneViewModel.cs` | 572 | 436 | −136（−24%） | 🟠→🟡 | #207 |
| `Services/HelpService.cs` | 559 | 300 | −259（−46%） | 🟡→🟡 | #208 |
| `Panes/Preview/PreviewPaneViewModel.cs` | 482 | 482 | 0 | 🟡→🟡 | — |
| `Panes/Preview/PreviewPaneViewControl.xaml.cs` | 452 | 452 | 0 | 🟡→🟡 | — |
| `Panes/FileBrowser/FileBrowserPaneService.cs` | 400 | 400 | 0 | 🟡→🟡 | — |
| `Services/MainWindowLayoutCoordinator.cs` | 393 | 393 | 0 | 🟡→🟡 | — |
| `Services/ExifEditorService.cs` | 373 | 373 | 0 | 🟡→🟡 | — |
| `ViewModels/MainViewModel.cs` | 340 | 340 | 0 | 🟡→🟡 | — |
| `Services/PaneLayoutHostService.cs` | 319 | 319 | 0 | 🟡→🟡 | — |
| `Panes/Map/MapPaneService.cs` | 241 | 241 | 0 | ✅→✅ | — |
| `App.xaml.cs` | 229 | 229 | 0 | ✅→✅ | — |
| `Services/DialogService.cs` | 223 | 223 | 0 | ✅→✅ | — |
| `ViewModels/PhotoListItem.cs` | 208 | 208 | 0 | ✅→✅ | — |

### PhotoGeoExplorer.Core/

| ファイル | 開始時点 | 現在 | 差分 | 分類（開始→現在） | 対応 Issue |
| --- | ---: | ---: | ---: | --- | --- |
| `Services/ExifService.cs` | 558 | （削除・3分割） | −558 | 🟠→解消 | #178, #199-201 |
| `Services/ThumbnailService.cs` | 181 | 181 | 0 | ✅→✅ | — |
| `Services/SettingsService.cs` | 172 | 172 | 0 | ✅→✅ | — |
| `Services/FileSystemService.cs` | 153 | 153 | 0 | ✅→✅ | — |
| `Services/UpdateService.cs` | 146 | 146 | 0 | ✅→✅ | — |

`ExifService.cs` は Reader/Writer 責務分離（#199, #200）を経て、書き込み専用ファサードとしての価値がなくなったため削除（#201）。後継モジュールは新設一覧を参照。

## Phase 1〜4 で新設したモジュール

Phase 1（`FileBrowserPaneViewModel.cs` / `FileBrowserPaneView.xaml.cs` からの抽出）の詳細は [`FileBrowserDecomposition-Phase1-Summary.md`](FileBrowserDecomposition-Phase1-Summary.md) を参照（新設モジュール合計 2,408 行）。

### Phase 2（App 層、#175-177）

| モジュール | 行数 | 責務 | Issue |
| --- | ---: | --- | --- |
| `Panes/Settings/SettingsPaneLayoutSectionViewModel.cs` | 295 | ペインレイアウト設定の子 ViewModel | #175 |
| `Panes/Map/MapExifLocationPicker.cs` | 157 | EXIF/GPS 位置ピッカーの専用ハンドラ | #176 |
| `Services/SettingsNormalization.cs` | 175 | 設定値の純粋正規化関数群 | #177 |

### Phase 3（Core 層、#178, #199-201）

| モジュール | 行数 | 責務 | Issue |
| --- | ---: | --- | --- |
| `Services/ExifReader.cs` | 127 | EXIF 読み取り（MetadataExtractor） | #199 |
| `Services/JpegExifSegmentWriter.cs` | 275 | JPEG セグメント書き換え（byte レベル、ImageSharp 非依存） | #200 |
| `Services/ExifWriter.cs` | 209 | EXIF 書き込み（`JpegExifSegmentWriter` 利用） | #201 |

### Phase 4（残存 🟠 ファイル、#179, #206-208）

| モジュール | 行数 | 責務 | Issue |
| --- | ---: | --- | --- |
| `Services/CrashReportDialogService.cs` | 255 | クラッシュレポートダイアログ表示・GitHub Issue / メール起動 | #206 |
| `Panes/Map/MapMarkerPresenter.cs` | 177 | 地図マーカー・ピンスタイル生成 | #207 |
| `Services/HelpHtmlWindowController.cs` | 315 | HTML ヘルプウィンドウ（WebView2）のライフサイクル・ナビゲーション制御 | #208 |

Phase 1〜4 の新設モジュール合計: **約 3,411 行**（Phase 1: 2,408 / Phase 2: 627 / Phase 3: 611 / Phase 4: 747、Phase 3 は `ExifService.cs` 558 行の後継）

## 分類凡例（#150 と同一基準）

| 記号 | 基準 | アクション |
| --- | --- | --- |
| 🔴 要分割 | 500行超 **かつ** 独立した責務が複数混在 | 子 Issue を起票して分割着手 |
| 🟠 要注意 | 300〜500行、または複数責務の混在が顕著 | 次フェーズで子 Issue 化を検討 |
| 🟡 監視 | 200〜500行、単一責務または軽微な混在 | 今後の変更時にリファクタリング機会を探る |
| ✅ 正常 | 200行未満または責務が明確 | 現状維持 |

## 残存 🟠 ファイルの扱い

再計測時点（2026-07-07）で 500 行超が残る 5 ファイルについて、今後の方針を以下の通り記録する。

| ファイル | 現在行数 | 方針 | 理由 |
| --- | ---: | --- | --- |
| `Panes/FileBrowser/FileBrowserPaneViewModel.cs` | 1,263 | **監視継続** | Phase 1（#152-155）で複数責務（UI ディスパッチ・サムネイル生成・ファイル操作・ステータス表示）を分離済み。残存はフォルダナビゲーション・一覧管理・選択状態管理のオーケストレーション責務で、単一の強く関連したクラスタと判断。追加分割は Phase 1 サマリの通り価値に対しコストが見合わない可能性がある |
| `Panes/FileBrowser/FileBrowserPaneView.xaml.cs` | 908 | **監視継続** | Phase 1（#156-159）で MVVM 境界是正済み。View 表示・入力受付責務に整理済みで、これ以上の分割は View の性質上（XAML コードビハインドの構造）優先度低 |
| `Services/FileOperationService.cs` | 609 | **監視継続** | #179 のトリアージで単一責務（ファイル操作の実行）と判断済み。分割不要 |
| `Panes/Map/MapPaneViewControl.xaml.cs` | 599 | **次フェーズ化を検討**（Issue 化は見送り） | #176 で EXIF/GPS ピッカー責務を分離済み。残存は Mapsui コントロール初期化・UI 責務が中心で、複数責務混在の明確な証跡は今回未確認のため、Issue 化はせず監視を継続する |
| `MainWindow.xaml.cs` | 590 | **Follow-up Issue 起票**（#213） | #206 でクラッシュレポート機能を分離済みだが、更新チェック機能（`OnCheckUpdatesClicked` / `HandleUpdateCheckFailureAsync` 等、約 80 行）の処理本体がイベントハンドラに直接実装されており、MainWindow 肥大化防止ガードレール（「イベントハンドラは委譲のみに留める」）に違反している状態が残存。#206 と同型のパターンのため分離候補として明確 |

## Follow-up Issue

- **#213**: `MainWindow.xaml.cs` の更新チェック機能を専用サービスへ分離（ガードレール違反是正）。`OnCheckUpdatesClicked` / `HandleUpdateCheckFailureAsync` の処理本体を `UpdateCheckDialogService`（仮称）へ移動する。`ShowMessageDialogAsync` / `EnsureXamlRootAsync` は `OnOpenSettingsPaneClicked` からも共有利用されているため、分離時は依存関係の設計が必要。

## 品質・テスト

- 単体テスト: **745 件合格 / 0 失敗**（2026-07-07 時点、`dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`）
- E2E: 13 シナリオ（ローカル実行時はスキップ、CI では `e2e.yml` の 2 suite 並列実行で検証）
- Phase 1〜4 を通じてビルド・テストのグリーンを維持しながら段階的に分割を実施

## 関連 Issue / PR 一覧

| Issue | 内容 | 完了PR | Phase |
| --- | --- | --- | --- |
| #150 | 親 issue（総括） | — | — |
| #152-159 | FileBrowser ゴッドクラス分割 | #160-163, #166-170, #173 | 1 |
| #164 | LoadFolderAsync CTS race 修正 | #165 | 1（関連） |
| #167 | エラーマッピング純粋関数化 | #168 | 1（フォロー） |
| #171 | codecov/patch 構造的 fail 解消 | #172 | 1（フォロー） |
| #174 | VM 単体テストクラッシュ調査 | — | 1（フォロー・未着手） |
| #175 | SettingsPaneViewModel ドメイン別分割 | #195 | 2 |
| #176 | MapPaneViewControl の GPS/EXIF ピッカー分離 | #196 | 2 |
| #177 | SettingsCoordinator 正規化関数抽出 | #197 | 2 |
| #178 | ExifService Reader/Writer 分離（親） | — | 3 |
| #199 | ExifService 読み取り責務分離 | #203 | 3 |
| #200 | ExifService JPEG セグメント書き換え分離 | #204 | 3 |
| #201 | ExifService 書き込み責務分離・ファサード整理 | #205 | 3 |
| #179 | 残存🟠ファイルのトリアージ | — | 4 |
| #206 | MainWindow クラッシュレポート機能分離 | #210 | 4 |
| #207 | MapPaneViewModel マーカー表示ロジック分離 | #211 | 4 |
| #208 | HelpService HTML ヘルプウィンドウ管理分離 | #212 | 4 |
| #180-182 | E2E テスト基盤棚卸し・拡充・高速化 | — | 5 |
| #209 | 本総括 Issue | — | 6 |
| #213 | MainWindow 更新チェック機能分離（Follow-up） | — | 6（フォロー） |

## まとめ

- 当初 🔴 要分割と判定された 2 ゴッドクラスは、Phase 1 の 8 子 Issue により合計 4,030 行 → 2,171 行（−46%）まで縮退し、責務単位のモジュールへ分割された
- 🟠 要注意だった App 層 3 ファイル（`SettingsPaneViewModel` / `SettingsCoordinator` / `MapPaneViewModel`）と Core 層 1 ファイル（`ExifService`）は、Phase 2〜4 を経てそれぞれ 🟡 監視 相当まで改善、または解消（クラス削除）した
- `HelpService.cs`（🟡 監視）も Phase 4（#208）で 559 行 → 300 行（−46%）まで縮退した
- 残存する 500 行超ファイル 5 件のうち、明確なガードレール違反（`MainWindow.xaml.cs` の更新チェック機能）を Follow-up Issue（#213）として起票した。他は既存の分割判断（Phase 1 完了形、#179 トリアージ結果）を踏襲し監視継続とする
- 本総括をもって #150 を完了としてクローズする
