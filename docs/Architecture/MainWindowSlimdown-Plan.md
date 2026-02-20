# MainWindow スリム化（第2期）実装計画書

> 関連 ISSUE: [#95](https://github.com/scottlz0310/PhotoGeoExplorer/issues/95)

## 1. 現状分析

### MainWindow.xaml.cs の現行規模

- **総行数**: 2,055 行
- **剥がし対象メソッド合計**: 約 1,382 行（全体の 67%）

| カテゴリ | 概算行数 | 優先度 | リスク |
|---------|---------|-------|-------|
| A. メニュー/ショートカット中継 | 83 行 | 高 | 低 |
| B. EXIF 編集フロー | 294 行 | 高 | 中 |
| C. Map→FileBrowser 橋渡し | 23 行 | 中 | 中 |
| D. 設定コーディネーション | 269 行 | 中 | 中 |
| E. ヘルプ関連 | 315 行 | 中 | 低 |
| F. WorkspaceState 監視 | 113 行 | 低 | 低 |
| G. スタートアップ | 285 行 | 低 | 中 |

### 既存基盤

- **Pane アーキテクチャ**: Map / FileBrowser / Preview / Settings の 4 ペインが稼働中
- **FileBrowserPaneViewModel**: NavigateBack/Forward/Up/Home/Refresh/ToggleSort/ResetFilters の 7 コマンドが定義済み
- **テストフレームワーク**: xUnit + 実ファイルシステム統合テスト（Moq 不使用）
- **設計文書**: `PaneSystem.md` / `MainWindow-Orchestration-Review.md`

## 2. 設計方針

### 原則

1. **Shell に徹する**: MainWindow はレイアウト配置と最小限のウィンドウイベント処理のみ
2. **Pane 自律**: 各 Pane は ViewModel + Service で完結し、MainWindow を経由しない
3. **段階的移行**: 各 PR はビルド可・動作確認可の独立単位
4. **テスト容易性**: 新設サービスはインターフェースを定義し、将来のモック化に備える

### 共通パターン

- **Command 直結**: XAML の `Click` → `Command="{Binding}"` に置換
- **Service 抽出**: ビジネスロジックを `I*Service` + 実装に分離
- **イベント/メッセージ**: Pane 間通信は `WorkspaceState` または イベントで疎結合化

## 3. 実施フェーズ

### Phase 1: 低リスク・高効果（PR-1 ～ PR-3）

| PR | 内容 | 削減見込 | 依存 |
|----|------|---------|------|
| PR-1 | メニュー中継の撤去（A） | ～83 行 | なし |
| PR-2 | EXIF 編集フロー移管（B） | ～294 行 | PR-1 推奨 |
| PR-3 | Map→FileBrowser 橋渡し撤去（C） | ～23 行 | なし |

### Phase 2: 中規模移管（PR-4 ～ PR-5）

| PR | 内容 | 削減見込 | 依存 |
|----|------|---------|------|
| PR-4 | ヘルプ機能の IHelpService 化（E） | ～315 行 | なし |
| PR-5 | 設定コーディネーション移管（D） | ～269 行 | PR-1 推奨 |

### Phase 3: 仕上げ（PR-6 ～ PR-8）

| PR | 内容 | 削減見込 | 依存 |
|----|------|---------|------|
| PR-6 | WorkspaceState 監視の Pane 自己購読化（F） | ～113 行 | PR-3 |
| PR-7 | スタートアップ処理の Coordinator 化（G） | ～285 行 | PR-5 |
| PR-8 | 最終クリーンアップ・文書更新 | - | 全 PR |

## 4. 品質基準

### 各 PR 共通

- `dotnet build -c Release -p:Platform=x64` 成功
- `dotnet test -c Release -p:Platform=x64` 全テスト通過
- `dotnet format` 差分なし
- 新設サービスに対する基本テスト追加

### 最終確認

- 主要ユースケースの手動動作確認（`ManualTestChecklist.md` 参照）
- MainWindow の行数が目標（～800 行以下）に到達していること
- アーキテクチャ文書（`PaneSystem.md` 等）の更新

## 5. リスクと対策

| リスク | 影響 | 対策 |
|-------|------|------|
| XamlRoot 依存のダイアログ | EXIF・ヘルプの ContentDialog が MainWindow の XamlRoot に依存 | `IDialogService` で XamlRoot 取得を抽象化 |
| メニューバインディング不整合 | XAML の Command バインドパスの誤り | PR-1 で段階的に置換し、各ステップで動作確認 |
| 設定保存のタイミング競合 | デバウンス処理の移管時にタイミングずれ | SettingsCoordinator でデバウンスを一元管理 |
| Pane 間通信の経路変更 | Map→FileBrowser の選択連動が壊れる | PR-3 で WorkspaceState 経由に統一し、E2E で検証 |

## 6. 参考文書

- `docs/Architecture/PaneSystem.md` — Pane アーキテクチャの原則
- `docs/Architecture/MainWindow-Orchestration-Review.md` — 現状の責務分析
- `docs/Architecture/MainWindowSlimdown-Tasks.md` — タスク細分文書（本文書の実行単位）
- `docs/ManualTestChecklist.md` — 手動テストチェックリスト

## 7. 実施結果サマリー（2026-02-20, ISSUE #102 / PR-8）

- PR-1 ～ PR-8 を完了し、`MainWindow.xaml.cs` は **619 行**まで縮小（目標: 800 行以下）を達成。
- 最終クリーンアップとして残存不要 `using` を整理し、MainWindow の責務を Shell 中心に維持。
- `docs/Architecture/PaneSystem.md` と `docs/Architecture/MainWindow-Orchestration-Review.md` を完了状態へ更新。
- `CHANGELOG.md` に PR-8（ISSUE #102）の履歴を追記。
- 自動検証として以下を通過。
  - `dotnet format PhotoGeoExplorer.sln --verify-no-changes`
  - `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64`
  - `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`
