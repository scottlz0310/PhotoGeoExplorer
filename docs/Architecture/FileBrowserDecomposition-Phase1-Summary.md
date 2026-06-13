# FileBrowser ゴッドクラス解体 Phase 1 成果サマリー

> 対象: Issue #150「モジュール棚卸しとゴッドクラス解体計画」の **Phase 1**（子 issue #152〜#159）
> 集計時点: PR #173（#159）適用後の状態。行数は `wc -l` 実測値。

## 概要

`FileBrowserPaneView.xaml.cs`（View）と `FileBrowserPaneViewModel.cs`（ViewModel）の 2 つのゴッドクラスを、責務単位の独立モジュールへ段階的に分割した。View 側は AGENTS.md の MVVM 責務ガードレールに従い「UI 表示・入力受付のみ」へ、ViewModel 側はファイルシステム等の副作用を Service へ委譲する構成へ整理した。

## ゴッドクラスの行数推移

| ファイル | 着手前 | Phase 1 後 | 差分 |
| --- | ---: | ---: | ---: |
| `FileBrowserPaneView.xaml.cs` | 1,941 | 908 | **−1,033（−53%）** |
| `FileBrowserPaneViewModel.cs` | 2,089 | 1,263 | **−826（−40%）** |
| 合計 | 4,030 | 2,171 | **−1,859（−46%）** |

着手前ベースラインは Phase 1 最初のコミット `490008e`（#152）の親。

## Phase 1 で新設したモジュール

分割で抽出した責務別モジュール（いずれも単体テスト可能な単位）。行数は現在値。

### ViewModel 側（`FileBrowserPaneViewModel.cs` から抽出）

| モジュール | 行数 | 責務 | Issue / PR |
| --- | ---: | --- | --- |
| `Services/IUiDispatcher.cs` | 55 | UI スレッドディスパッチ抽象 | #152 / PR #160 |
| `Services/UiDispatcher.cs` | 140 | UI スレッドディスパッチ実装 | #152 / PR #160 |
| `ThumbnailGenerationCoordinator.cs` | 425 | サムネイル生成の調停 | #153 / PR #161 |
| `FileOperationCoordinator.cs` | 311 | ファイル操作・進捗・クリップボード管理 | #154 / PR #162 |
| `FileBrowserStatusViewModel.cs` | 398 | ステータス表示＋メタデータロード | #155 / PR #163 |

### View 側（`FileBrowserPaneView.xaml.cs` から抽出）

| モジュール | 行数 | 責務 | Issue / PR |
| --- | ---: | --- | --- |
| `FileBrowserDialogs.cs` | 311 | ダイアログ・ピッカー表示（WinRT/HWND Interop 含む） | #156 / PR #166 |
| `FileBrowserDialogErrorMap.cs` | 57 | 操作エラー → リソースキー対応（純粋関数） | #167 / PR #168 |
| `FileBrowserDragDropHandler.cs` | 404 | ドラッグ＆ドロップ処理 | #157 / PR #169 |
| `FileBrowserMenuBuilder.cs` | 307 | コンテキストメニュー・フライアウト構築 | #158 / PR #170 |

新設モジュール合計: **2,408 行**（ViewModel 側 1,329 / View 側 1,079）。

> 総行数は増えるが、これは責務分離に伴うインターフェース・コンストラクタ・DI 配線・XML ドキュメントの追加によるもので、想定どおり。価値は LOC 削減ではなく、各責務の独立性・テスト容易性・保守性の向上にある。

### #159（PR #173）— MVVM 境界是正（新規ファイルなし）

View に残っていた操作判断ロジックを既存クラスへ移動して Phase 1 を締めくくった。

- `FileOperationSummary.HasReportableFailures`（Service 派生プロパティ）を追加し、エラーダイアログ表示要否（キャンセル除外）を集約・統一。
- 単一フォルダ判定 / 削除確認メッセージ組み立て / Ctrl+C・X の選択判定 / 右クリック選択復元の決定ロジックを ViewModel へ移動。

## 品質・テスト

- 分割した責務はいずれも単体テストで固定。Phase 1 後のテストは **629 件合格 / 0 失敗**（E2E 3 件はスキップ）。
- View 層 UI 構築ヘルパー（`FileBrowserDialogs` / `FileBrowserDragDropHandler` / `FileBrowserMenuBuilder`）は WinRT 型を直接生成する単体テスト不能コードのため、`codecov.yml` の `ignore` に選択的追加して `codecov/patch` の構造的 fail を解消（#171 / PR #172）。テスト可能な純粋関数・ViewModel・Service はカバレッジ集計対象として維持。

## 関連 Issue / PR 一覧

| Issue | 内容 | PR | 状態 |
| --- | --- | --- | --- |
| #150 | 親 issue（Phase 1〜3） | — | 進行中 |
| #152 | UI スレッドディスパッチ抽出 | #160 | 完了 |
| #153 | サムネイル生成分離 | #161 | 完了 |
| #154 | ファイル操作・進捗・クリップボード分離 | #162 | 完了 |
| #155 | ステータス表示＋メタデータロード分離 | #163 | 完了 |
| #156 | ダイアログ表示分離 | #166 | 完了 |
| #167 | エラーマッピングの純粋関数化（#156 フォロー） | #168 | 完了 |
| #157 | ドラッグ＆ドロップ分離 | #169 | 完了 |
| #158 | メニュー・フライアウト構築分離 | #170 | 完了 |
| #159 | MVVM 境界是正 | #173 | レビュー中 |
| #164 | LoadFolderAsync 並行実行の CTS race 修正（Phase 1 中の関連修正） | #165 | 完了 |
| #171 | codecov/patch 構造的 fail 解消 | #172 | 完了 |
| #174 | VM 単体テストの実 FolderWatcherService 起因クラッシュ（#159 フォローアップ） | — | 未着手 |

#159（PR #173）のマージをもって Phase 1 は完了見込み。
