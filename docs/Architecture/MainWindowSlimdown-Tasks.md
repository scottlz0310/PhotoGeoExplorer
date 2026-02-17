# MainWindow スリム化（第2期）タスク細分文書

> 関連 ISSUE: [#95](https://github.com/scottlz0310/PhotoGeoExplorer/issues/95)
> 実装計画書: [MainWindowSlimdown-Plan.md](./MainWindowSlimdown-Plan.md)

各 PR の具体的な作業項目をチェックリスト形式で記載します。

---

## PR-1: メニュー中継の撤去（Click → Command 直結）

**目的**: MainWindow の `On*Clicked` ハンドラを削除し、XAML から直接 ViewModel の Command にバインドする。

**対象メソッド（MainWindow.xaml.cs）**:
- `OnNavigateHomeClicked` (615-618)
- `OnNavigateBackClicked` (620-646)
- `OnNavigateForwardClicked` (648-674)
- `OnNavigateUpClicked` (676-679)
- `OnRefreshClicked` (681-684)
- `OnOpenFolderClicked` (686-689)
- `OnResetFiltersClicked` — 存在する場合
- `OnToggleImagesOnlyClicked`
- `OnCreateFolderClicked` (1280-1283)
- `OnRenameClicked` (1285-1288)
- `OnMoveClicked` (1290-1293)
- `OnDeleteClicked` (1295-1298)
- `OnMoveToParentClicked` — 存在する場合
- `OnViewModeMenuClicked`

**タスク**:

- [ ] FileBrowserPaneViewModel に不足しているコマンドを追加
  - [ ] `OpenFolderCommand` — フォルダ選択ダイアログ → ナビゲーション
  - [ ] `ToggleImagesOnlyCommand` — 画像フィルタ切替
  - [ ] `CreateFolderCommand` — フォルダ作成
  - [ ] `RenameCommand` — リネーム
  - [ ] `MoveCommand` — 移動
  - [ ] `DeleteCommand` — 削除
  - [ ] `MoveToParentCommand` — 親フォルダへ移動
  - [ ] `ViewModeCommand` — 表示モード切替
- [ ] MainWindow.xaml のメニュー/ツールバー項目を Command バインディングに変更
  - [ ] ナビゲーション系（Home/Back/Forward/Up/Refresh）
  - [ ] ファイル操作系（Create/Rename/Move/Delete）
  - [ ] フィルタ/表示系（ImagesOnly/ViewMode）
  - [ ] フォルダ選択（OpenFolder）
- [ ] MainWindow.xaml.cs から対象 `On*Clicked` ハンドラを削除
- [ ] 各コマンドで例外処理・通知を Pane VM 内に集約
- [ ] ビルド・テスト確認
- [ ] 手動動作確認（ナビゲーション・ファイル操作・フィルタ）

**削減見込**: ～83 行

---

## PR-2: EXIF 編集フロー移管

**目的**: EXIF 編集の検証・ダイアログ・保存・更新通知を専用サービスと ViewModel に移管する。

**対象メソッド（MainWindow.xaml.cs）**:
- `OnEditExifRequested` (1698-1701)
- `EditExifAsync` (1703-1810)
- `ShowExifEditDialogAsync` (1812-1967)
- `PickExifLocationAsync` (1969-1994)

**新規ファイル**:
- `PhotoGeoExplorer/Services/IDialogService.cs`
- `PhotoGeoExplorer/Services/DialogService.cs`
- `PhotoGeoExplorer/Services/IExifEditorService.cs`
- `PhotoGeoExplorer/Services/ExifEditorService.cs`

**タスク**:

- [ ] `IDialogService` インターフェース定義
  - [ ] `ShowContentDialogAsync` — XamlRoot 取得を内包
  - [ ] `ShowMessageDialogAsync` — 簡易メッセージ表示
  - [ ] `ShowFilePickerAsync` — ファイル選択
- [ ] `DialogService` 実装（XamlRoot 待機ロジックを `EnsureXamlRootAsync` から移管）
- [ ] `IExifEditorService` インターフェース定義
  - [ ] `EditExifAsync(PhotoListItem)` — 編集フロー全体
  - [ ] `ValidateExifEditableAsync` — 編集可否チェック
- [ ] `ExifEditorService` 実装
  - [ ] `EditExifAsync` の本体ロジック移管
  - [ ] `ShowExifEditDialogAsync` のダイアログ構築移管
  - [ ] `PickExifLocationAsync` の位置ピックロジック移管
- [ ] FileBrowserPaneViewModel に `EditExifCommand` を追加
- [ ] MainWindow.xaml.cs から EXIF 関連メソッドを削除
- [ ] テスト追加
  - [ ] `ExifEditorServiceTests.cs` — バリデーション・フロー分岐
- [ ] ビルド・テスト確認
- [ ] 手動動作確認（EXIF 編集→保存→失敗通知）

**削減見込**: ～294 行

---

## PR-3: Map→FileBrowser 橋渡し撤去

**目的**: 矩形選択結果とフォーカス転送を MainWindow 経由から WorkspaceState/イベント経由に変更する。

**対象メソッド（MainWindow.xaml.cs）**:
- `OnMapPanePhotoFocusRequested` (2018-2021)
- `OnMapPaneRectangleSelectionCompleted` (2023-2037)
- `OnMapPaneNotificationRequested` (2039-2042)

**タスク**:

- [ ] WorkspaceState に選択コマンド用プロパティ/イベントを追加
  - [ ] `SelectionRequest` — Map → FileBrowser への選択要求
  - [ ] `FocusRequest` — Map → FileBrowser へのフォーカス要求
- [ ] MapPaneViewModel から WorkspaceState 経由で選択/フォーカスを発行
- [ ] FileBrowserPaneViewModel で WorkspaceState の選択/フォーカス要求を購読
- [ ] 型変換ロジック（filePaths → PhotoListItem 選択）を FileBrowserPaneService に移管
- [ ] MainWindow.xaml.cs から橋渡しメソッドを削除
- [ ] MainWindow.xaml.cs のイベントハンドラ登録（`PhotoFocusRequested` 等）を削除
- [ ] テスト追加
  - [ ] 選択要求の発行・購読の統合テスト
- [ ] ビルド・テスト確認
- [ ] 手動動作確認（Map 矩形選択 → FileBrowser 連動）

**削減見込**: ～23 行

---

## PR-4: ヘルプ機能の IHelpService 化

**目的**: ヘルプダイアログ・HTML 表示・外部リンク処理を専用サービスに集約する。

**対象メソッド（MainWindow.xaml.cs）**: 19 メソッド、計 ～315 行
- `OnHelpGettingStartedClicked` / `OnHelpBasicsClicked` / `OnHelpHtmlWindowClicked` / `OnAboutClicked`
- `ShowHelpDialogAsync` / `OpenHelpHtmlWindowAsync` / `CreateHelpHtmlWebView`
- `TryGetHelpHtmlUri` / `GetHelpHtmlFileName` / `ShowHelpHtmlMissingDialogAsync`
- `CleanupHelpHtmlWindow` / `CloseHelpHtmlWindow` / `CloseHelpHtmlWebView`
- `OnHelpWebViewInitialized` / `OnHelpWebViewNewWindowRequested` / `OnHelpWebViewNavigationStarting`
- `TryGetExternalUri` / `OpenExternalUriAsync` / `TryResizeHelpWindow` / `GetAppWindow`

**新規ファイル**:
- `PhotoGeoExplorer/Services/IHelpService.cs`
- `PhotoGeoExplorer/Services/HelpService.cs`

**タスク**:

- [ ] `IHelpService` インターフェース定義
  - [ ] `ShowGettingStartedAsync`
  - [ ] `ShowBasicsAsync`
  - [ ] `ShowHelpHtmlWindowAsync`
  - [ ] `ShowAboutAsync`
  - [ ] `ShowQuickStartIfNeededAsync`
  - [ ] `CloseHelpWindow`
- [ ] `HelpService` 実装
  - [ ] ヘルプダイアログ構築ロジック移管
  - [ ] WebView2 寿命管理移管
  - [ ] 外部リンク処理移管
  - [ ] 別窓サイズ制御移管
- [ ] MainWindow.xaml のヘルプメニューを Command バインディングに変更（ShellVM 経由）
- [ ] MainWindow.xaml.cs からヘルプ関連メソッドを削除
- [ ] `IDialogService`（PR-2 で追加済み想定）との連携
- [ ] テスト追加
  - [ ] `HelpServiceTests.cs` — URI 生成・ファイル名取得のテスト
- [ ] ビルド・テスト確認
- [ ] 手動動作確認（ヘルプダイアログ・HTML 窓・外部リンク）

**削減見込**: ～315 行

---

## PR-5: 設定コーディネーション移管

**目的**: 言語/テーマ/地図/保存の設定ロジックを SettingsCoordinator に集約し、メニューの IsChecked を ShellVM バインドに置換する。

**対象メソッド（MainWindow.xaml.cs）**: 16 メソッド、計 ～269 行

**新規ファイル**:
- `PhotoGeoExplorer/Services/ISettingsCoordinator.cs`
- `PhotoGeoExplorer/Services/SettingsCoordinator.cs`

**タスク**:

- [ ] `ISettingsCoordinator` インターフェース定義
  - [ ] `LoadAsync` / `ApplyAsync` / `SaveAsync`
  - [ ] `ChangeLanguageAsync` / `ChangeTheme` / `ChangeMapZoom` / `ChangeMapTileSource`
  - [ ] `ExportSettingsAsync` / `ImportSettingsAsync`
  - [ ] デバウンス付き自動保存
- [ ] `SettingsCoordinator` 実装
  - [ ] `LoadSettingsAsync` / `ApplySettingsAsync` / `BuildSettingsSnapshot` ロジック移管
  - [ ] `SaveSettingsAsync` / `ScheduleSettingsSave` / `SaveSettingsDelayedAsync` 移管
  - [ ] 言語/テーマ/地図設定の適用ロジック移管
- [ ] MainViewModel（ShellVM）に設定状態プロパティを追加
  - [ ] `CurrentLanguage` / `CurrentTheme` / `CurrentMapZoom` / `CurrentMapTileSource`
  - [ ] メニュー IsChecked バインディング用プロパティ
- [ ] MainWindow.xaml のメニュー IsChecked を ShellVM プロパティにバインド
- [ ] MainWindow.xaml.cs から設定関連メソッドを削除
- [ ] テスト追加
  - [ ] `SettingsCoordinatorTests.cs` — ロード/保存/デバウンス
- [ ] ビルド・テスト確認
- [ ] 手動動作確認（言語/テーマ/地図設定の変更・保存・復元）

**削減見込**: ～269 行

---

## PR-6: WorkspaceState 監視の Pane 自己購読化

**目的**: MainWindow が仲介する WorkspaceState の変更通知を、各 Pane が自分で購読する形に変更する。

**対象メソッド（MainWindow.xaml.cs）**:
- `OnWorkspaceStatePropertyChanged` (516-523)
- `OnFileBrowserPanePropertyChanged` (504-514)
- 関連する `TogglePreviewMaximize` / スプリッタ系は確定値保存のみ Command 化

**タスク**:

- [ ] MapPaneViewModel で WorkspaceState.SelectedPhotos の変更を自己購読
- [ ] PreviewPaneViewModel で必要な WorkspaceState プロパティを自己購読
- [ ] MainWindow.xaml.cs から `OnWorkspaceStatePropertyChanged` を削除/縮小
- [ ] スプリッタ確定値保存を Command 化（SettingsCoordinator 経由）
- [ ] テスト追加
- [ ] ビルド・テスト確認

**削減見込**: ～113 行

---

## PR-7: スタートアップ処理の Coordinator 化

**目的**: 起動時のフォルダ決定・ファイルアクティベーション・初期設定適用を専用 Coordinator に集約する。

**対象メソッド（MainWindow.xaml.cs）**: 計 ～285 行

**新規ファイル**:
- `PhotoGeoExplorer/Services/IStartupCoordinator.cs`
- `PhotoGeoExplorer/Services/StartupCoordinator.cs`

**タスク**:

- [ ] `IStartupCoordinator` インターフェース定義
  - [ ] `InitializeAsync` — 起動シーケンス全体
  - [ ] `GetStartupFolder` — コマンドライン/アクティベーション解析
  - [ ] `ApplyStartupFileActivationAsync` — ファイル起動対応
- [ ] `StartupCoordinator` 実装
  - [ ] `GetStartupFolderOverride` / `TryGetOptionValue` 移管
  - [ ] `SetStartupFilePath` 移管
  - [ ] `ApplyStartupFolderOverrideAsync` / `ApplyStartupFileActivationAsync` 移管
  - [ ] `FindValidAncestorPath` 移管
- [ ] MainWindow の `OnActivated` を Coordinator 呼び出しに簡素化
- [ ] テスト追加
  - [ ] `StartupCoordinatorTests.cs` — 引数解析・パス探索
- [ ] ビルド・テスト確認

**削減見込**: ～285 行

---

## PR-8: 最終クリーンアップ・文書更新

**目的**: 残存する不要コード削除、アーキテクチャ文書の更新。

**タスク**:

- [ ] MainWindow.xaml.cs の残存不要 `using` / フィールド / ヘルパー削除
- [ ] MainWindow の最終行数確認（目標: ～800 行以下）
- [ ] `docs/Architecture/PaneSystem.md` 更新
- [ ] `docs/Architecture/MainWindow-Orchestration-Review.md` 更新（完了マーク）
- [ ] `docs/Architecture/MainWindowSlimdown-Plan.md` に結果サマリー追記
- [ ] CHANGELOG.md 更新
- [ ] 全テスト通過確認
- [ ] 手動テスト全項目確認（`ManualTestChecklist.md`）
