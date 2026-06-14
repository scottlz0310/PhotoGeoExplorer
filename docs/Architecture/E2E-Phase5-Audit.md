# E2E 現状フィット棚卸し（Phase 5 / #180）

> 親 Issue #150 Phase 5（テスト基盤・E2E 整備）の起点。Phase 1（#152〜#159）の `FileBrowserPaneView` 大規模分割後に、既存 E2E（`PhotoGeoExplorer.E2E`）が現状フィットしているかを再確認し、後続 P5-B（#181 操作系シナリオ追加）/ P5-C（#182 実行時間短縮）の前提を整える。

## 1. 既存 E2E の構成

- テスト（`AppE2ETests.cs`、FlaUI UIA3、3 本）:
  1. `LaunchOpenFolderPreviewMetadataAndMap` — 起動→フォルダ→プレビュー→メタデータ→地図
  2. `ExifEditorContextMenuAndDateToggleWorks` — EXIF エディタ コンテキストメニュー＋日付トグル
  3. `ExifEditorSaveAndReopenKeepsCoordinates` — EXIF エディタ 保存→再オープンで座標保持
- 実行ゲート: `[E2EFact]`（`PHOTO_GEO_EXPLORER_RUN_E2E=1` 未設定時は Skip）。
- CI（`.github/workflows/e2e.yml`）: PR/push（main・develop）。windows-latest、WindowsAppRuntime 1.8 導入、1920x1080 設定、**最大 2 回リトライ＋`--blame-hang-timeout 10m`**（flaky 前提）。
- テストデータ（`E2ETestData`）: temp に `sample.jpg`（Fujifilm/X100V、GPS・DateTimeOriginal 付与）1 枚＋空 `folder` 1 個。アプリは `--folder <temp>` 起動。終了は `TerminateApp`（Close→WaitForExit→Kill）→その後 temp 再帰削除。

## 2. automation ID 整合点検（既存 3 テスト参照分）

既存テストが参照する automation ID は **すべて分割後の現行コードに生存し、命名も一貫**している。整合性に問題なし。

| 参照 ID | 定義場所 | 種別 |
| --- | --- | --- |
| `FileListList` / `FileListIcon` / `FileListDetails` | `Panes/FileBrowser/FileBrowserPaneView.xaml`（395 / 418 / 442） | XAML |
| `FileBrowser.EditExifMenuItem` | `Panes/FileBrowser/FileBrowserMenuBuilder.cs` | コードビハインド |
| `ExifEditor.UpdateDateCheckBox` / `UpdateFileDateCheckBox` / `TakenAtDatePicker` / `TakenAtTimePicker` / `LatitudeTextBox` / `LongitudeTextBox` | `Services/ExifEditorService.cs`（202 / 211 / 223 / 229 / 259 / 273） | コード生成 |
| `MetadataSummaryText` / `PreviewImage` | `Panes/Preview/PreviewPaneViewControl.xaml`（31 / 100） | XAML |
| `MapStatusPanel` | `Panes/Map/MapPaneViewControl.xaml`（101） | XAML |
| `PrimaryButton` / `SecondaryButton` | `ContentDialog` 標準パーツ | 標準（`Save`/`Cancel` 名フォールバックあり） |

### 既定義だが E2E 未使用（P5-B で利用可能）

- `SearchTextBox` / `OpenFolderButton` / `ViewModeCombo` / `ShowImagesOnlyCheckBox`（FileBrowserPaneView.xaml）
- `MapControl` / `MapStatusTitle` / `MapStatusDescription`（MapPaneViewControl.xaml）
- `ExifEditor.GetLocationButton` / `ExifEditor.ClearLocationButton`（ExifEditorService.cs 284 / 298）

## 3. 操作系 ID 棚卸しとギャップ対応（本 PR で実施）

`FileBrowserMenuBuilder.BuildFileContextFlyout()` のコンテキストメニュー項目は、従来 `FileBrowser.EditExifMenuItem` の **1 項目のみ** ID 付与済みで、他は未付与だった。P5-B（#181）の操作系シナリオ（削除確認文面／右クリック選択復元／クリップボード／移動競合・キャンセル）は、メニュー項目を名前部分一致ではなく ID で一意特定する必要がある。

本 PR で全項目に `FileBrowser.*MenuItem` 形式（既存 `EditExif` に統一）の ID を付与した。

| メニュー項目 | 付与した AutomationId | P5-B 用途 |
| --- | --- | --- |
| 新規フォルダ | `FileBrowser.NewFolderMenuItem` | |
| エクスプローラーで開く | `FileBrowser.OpenInExplorerMenuItem` | |
| フォルダをエクスプローラーで開く | `FileBrowser.OpenFolderInExplorerMenuItem` | |
| パスをコピー | `FileBrowser.CopyPathMenuItem` | クリップボード検証 |
| Google マップで開く | `FileBrowser.OpenInGoogleMapsMenuItem` | |
| リネーム | `FileBrowser.RenameMenuItem` | リネーム |
| 移動 | `FileBrowser.MoveMenuItem` | 移動競合・キャンセル |
| 親フォルダへ移動 | `FileBrowser.MoveToParentMenuItem` | 移動 |
| コピー | `FileBrowser.CopyMenuItem` | クリップボード／コピー |
| 削除 | `FileBrowser.DeleteMenuItem` | 削除確認文面 |
| EXIF 編集 | `FileBrowser.EditExifMenuItem`（既存） | |

> 補足: ID 付与は production アセンブリに入るが、UI 表示・挙動には影響しないテスト用フックである。名前ベースの脆い検索（現状 `OpenExifMenuForItemName` の `"EXIF"` 部分一致等）を ID 直引きへ置換でき、メニュー特定起因の flaky 低減にも寄与する。

## 4. テストデータ／ヘルパー現状確認

現状の `E2ETestData` は単一画像＋空フォルダのみで、操作系シナリオには不足。P5-B（#181）で以下の fixture 拡張が必要（本 Issue ではスコープ外、要件のみ整理）:

- 複数画像（選択範囲・一括操作・カウント検証用）
- 同名衝突を起こすための移動/コピー先フォルダ＋同名ファイル
- 削除確認・キャンセル後の状態検証用の安定したファイル集合

ヘルパー面では、`OpenExifMenuForItemName` がコンテキストメニュー汎用化されておらず EXIF 専用名で固定のため、P5-B では「項目 ID を指定してコンテキストメニューを開くヘルパー」への一般化が望ましい（本 PR の ID 付与がその前提）。

## 5. #174 との関連と E2E 安定化方針

### 切り分け結論：#174 と E2E flaky は別系統

- **#174**（PR #183 で解消済み）は、テストホスト**プロセス内**（`PhotoGeoExplorer.Tests`）で実 `FolderWatcherService`（`FileSystemWatcher` + `System.Threading.Timer`）が動き、temp 削除と競合してテストホストごと非決定的にクラッシュする問題。
- **E2E** は **別プロセスのアプリ**を起動して UIA 操作する。実 `FolderWatcherService` が動くのは production アプリ側として正しい挙動。E2E は `TerminateApp` でアプリを先に終了させてから temp を再帰削除する順序のため、watcher と temp 削除の競合はアプリプロセス内に閉じ、テストホストには波及しない。
- よって E2E の flaky（リトライ 2 回・blame-hang 10m 前提）の主因は #174 とは別。主因は UIA 描画遅延・要素出現タイミング・コンテキストメニュー開閉の不安定さ（`OpenExifMenuForItemName` が AppsKey→右クリック×2→リスト右クリック→Shift+F10 と 6 通りのフォールバックを持つこと自体が証左）。

### 安定化方針（実装は P5-B / #182）

1. **コンテキストメニュー項目 ID 付与（本 PR で完了）** — 名前部分一致検索を ID 直引きへ置換し、メニュー特定 flaky を低減。
2. **テスト用起動フラグでの watcher デバウンス短縮／無効化の検討** — アプリプロセス側 Timer 起因の遅延・終了時競合を抑制。production への手当てが必要なため P5-B 以降で評価。
3. **temp 削除前のアプリ完全終了の確実化** — 現状 `TerminateApp` で対処済み。残存 `IOException` はログのうえ握り潰し。
4. **リトライ／hang-timeout の最適化** — #182 で実行時間計測とあわせて見直し。

## 6. P5-B（#181）着手前提チェック

- [x] automation ID 整合点検（既存参照分はすべて生存・命名一貫）
- [x] 操作系メニュー項目の ID ギャップを解消（全項目に付与）
- [x] #174 との関連と E2E 安定化方針を整理
- [ ] テストデータ／ヘルパー拡張（要件を本書 §4 に整理。実装は #181 スコープ）

→ ID・方針の前提が整い、#181（操作系シナリオ追加）に着手可能。
