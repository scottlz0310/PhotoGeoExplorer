# MainWindow オーケストレーション残留ロジック調査

## 目的
MainWindow が「オーケストレーションに徹する」方針に対して、各ペイン由来の動作ロジックが残っている箇所を洗い出す。

## 結論サマリー
MainWindow には、**Map / FileBrowser / Preview の各ペインに固有の振る舞い**が複数残っている。特に **Map 関連は UI イベント処理・マップレイヤー操作・EXIF 位置選択・矩形選択**まで含んでおり、ペイン側へ移管すべきロジックが多い。

## ペイン別の残留ロジック

### Map Pane
**対象ファイル**: `PhotoGeoExplorer/MainWindow.xaml.cs`

残っているロジック例:
- **マップ UI イベント処理**
  - `OnMapPointerPressed / Moved / Released / CaptureLost`
  - Ctrl + ドラッグによる矩形選択開始・終了
  - EXIF 位置選択時のクリック/キャンセル判定
- **矩形選択の描画とマップレイヤー管理**
  - `UpdateRectangleSelectionLayer`
  - `ClearRectangleSelectionLayer`
  - `LockMapPan / RestoreMapPanLock`
- **地図上のマーカー関連**
  - `OnMapInfoReceived`（ヒットテスト）
  - `ShowMarkerFlyout`（Flyout 内容の組み立て）
  - `GenerateGoogleMapsUrl / OnGoogleMapsLinkClicked`
- **EXIF 位置選択フロー**
  - `PickExifLocationAsync / CompleteExifLocationPick / CancelExifLocationPick`
- **マップ設定とメニュー制御**
  - `OnMapTileSourceMenuClicked / UpdateMapTileSourceMenuChecks`
  - `OnMapZoomMenuClicked / UpdateMapZoomMenuChecks`
- **ステータスオーバレイの表示制御**
  - `UpdateMapStatusFromViewModel`（MapPaneViewModel の状態 → MainWindow の UI 反映）

移管候補:
- Map 操作系: `MapPaneView` の code-behind or `MapPaneViewModel`
- 矩形選択の計算/描画: `MapPaneService` or 新規 `MapSelectionService`
- Flyout 表示/外部リンク: `MapPaneView` 側の UI 専任コード
- EXIF 位置選択フロー: `MapPaneViewModel` + `IMapPaneService`

メモ:
- `App.xaml` には `MapPaneView` が ResourceDictionary として存在するが、実際の UI は `MainWindow.xaml` 側で直接 MapControl とオーバレイを持っている。**重複構造**が残っているため、将来的な整理余地が大きい。

### FileBrowser Pane
**対象ファイル**: `PhotoGeoExplorer/MainWindow.xaml.cs`

残っているロジック例:
- **ナビゲーション系操作**
  - `OnNavigateHome/Back/Forward/Up/Refresh`
  - `OnOpenFolderClicked`
- **表示設定/フィルター操作**
  - `OnToggleImagesOnlyClicked`
  - `OnViewModeMenuClicked`
  - `OnResetFiltersClicked`
- **ファイル操作**
  - `OnCreateFolderClicked / OnRenameClicked / OnMoveClicked / OnDeleteClicked`
  - `OnMoveToParentClicked`

移管候補:
- `FileBrowserPaneView` へ UI イベントを集約
- ViewModel に `ICommand` を持たせ、MainWindow からの直接呼び出しを削減

### Preview Pane
**対象ファイル**: `PhotoGeoExplorer/MainWindow.xaml.cs`

残っているロジック例:
- **レイアウト制御**
  - `TogglePreviewMaximize`
  - `OnMainSplitterDragDelta`
- **DPI 変更に伴うズーム補正**
  - `OnXamlRootChanged`（PreviewPaneViewModel の `OnRasterizationScaleChanged` を呼ぶ）

移管候補:
- レイアウト制御は `PreviewPaneViewControl` 側に寄せるか、専用の `LayoutService` に切り出す
- DPI 変更は View 側のイベントとして扱い、MainWindow を経由しない形に

## その他（ペイン外だがロジックが多い領域）
ペイン固有ではないが、MainWindow に実装が多い箇所。

- **EXIF 編集ダイアログの UI 組み立て**
  - `ShowExifEditDialogAsync` の中で UI を動的構築している
  - ここは `ExifEditorDialog` の専用コントロール化が妥当
- **Help/診断系**
  - HTML ヘルプの WebView2 管理（`OpenHelpHtmlWindowAsync` ほか）
  - ログフォルダ表示、更新チェック
- **設定のロード/保存・言語/テーマ**
  - `LoadSettingsAsync / SaveSettingsAsync / ApplyLanguageSettingAsync` など
  - これは “アプリ全体” の責務として MainWindow に残っていても妥当

## まとめ（Issue に貼るための短縮版）
MainWindow に Map/FileBrowser/Preview の動作ロジックが残存している。特に Map は UI イベント、矩形選択描画、EXIF 位置選択、マーカー Flyout まで担っており移管余地が大きい。FileBrowser はナビゲーションやファイル操作系イベントを ViewModel へ集約可能。Preview は最大化/レイアウト・DPI 補正が MainWindow 経由で残っている。  
整理方針: **UI イベントを各 Pane View に寄せる + ViewModel へ Command 化 + Map の選択/描画は専用サービス化** が妥当。

