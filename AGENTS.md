# リポジトリガイドライン

## AIエージェントは日本語使用を推奨
- 本リポジトリでは、AIコーディング（例: CodexCli,ClaudeCode）を活用した開発を推奨しています。
- AIエージェントの返答は日本語で行います。
- ドキュメントも基本的に日本語で記述します。
- 英語のドキュメントを発見した場合は、日本語版を作成し、英語版を置き換えてください。

## プロジェクト構成とモジュール構成
- `PhotoGeoExplorer/` に WinUI 3 アプリ本体（XAML + code-behind）があります。
- `PhotoGeoExplorer/Models`, `Services`, `ViewModels` がドメインロジックと MVVM を担います。
- `PhotoGeoExplorer/wwwroot/` は WebView2 で読み込む Leaflet の HTML/CSS/JS を配置します。
- `docs/` は必須でないドキュメント置き場です。トップレベルのドキュメントは最小限にします。
- `Archives/` は参照用素材の置き場です。本番成果物には含めません。

## ビルド・テスト・開発コマンド (Dev Workflow)
- **通常ビルド**: `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64`
- **ローカル実行**: `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64`
- **パッケージ作成・インストール (MSIX)**:
  - `.\\scripts\\DevInstall.ps1 -Build` : ビルド、署名、インストールを一括実行
  - `.\\scripts\\DevInstall.ps1` : 既存ビルドの再インストール・署名のみ
  - `.\\scripts\\DevInstall.ps1 -Clean` : アンインストールとクリーンアップ
- **WACK (Store審査) テスト**:
  - `.\\scripts\\RunWackTests.ps1` : ローカルインストールパッケージに対してテスト実行
- **テスト実行**: `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`
- **フォーマット**: `dotnet format PhotoGeoExplorer.sln`

## 開発時の確認サイクル
1. コード変更
2. フォーマット＆ビルド確認: `dotnet format; dotnet build -c Release -p:Platform=x64`
3. クイック動作確認: `dotnet run ...`
4. 実機インストール確認 (リリース前): `.\\scripts\\DevInstall.ps1 -Build`
5. `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` の確認

## コーディングスタイルと命名規則
- C# は 4 スペースインデント。`MainWindow.xaml` と `*.cs` の既存ルールに合わせます。
- 型と公開メンバーは `PascalCase`、ローカル/フィールドは `camelCase`。private フィールドは `_field` 形式。
- public / internal / protected の識別子（特にテストメソッド名）にアンダースコアは使わない（CA1707 対応）。private フィールド先頭の `_` は例外。
- 非同期メソッドは `Async` で終える（例: `InitializeMapAsync`）。
- XAML 名は `PascalCase`（例: `MapStatusText`）。
- 解析は厳格です（`Directory.Build.props` で `AnalysisLevel=latest`、警告はエラー扱い）。

## テスト指針
- `PhotoGeoExplorer.Tests`（xUnit）と `PhotoGeoExplorer.E2E` が存在します。
- すべてのテストは次で実行します:
  `dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64`.
- E2E を実行する場合は `PHOTO_GEO_EXPLORER_RUN_E2E=1` を指定します。

## コミット・PR ガイドライン
- コミットメッセージはコンベンショナルコミット（例: `Fix: WebView2 startup and map status`）。
- 依存更新は `chore(deps): ... (#NN)`（Renovate 形式）。
- PR には要約、理由、検証方法（コマンド/ログ）を含めます。
- UI 変更はスクリーンショットを添付し、関連 Issue があればリンクします。
- AIから見て不自然に新しいバージョンに感じたとしても勝手にバージョンダウンしたりせずに、webで最新情報を調査してください。大抵あなたの学習時期のタイムラグが原因です。

## ブランチ運用
- 原則として main で直接作業せず、サブブランチで作業して PR でマージします。
- 大きいタスクになりそうな場合は、先に Issue を作成して整理します。
- ドキュメント更新やリリース準備は main で直接作業しても構いません。

## セキュリティと設定の注意
- ログは `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` に書き出し、起動時にリセットします。
- Release ワークフローは `v1.2.0` のようなタグで `win-x64` 向け MSIX Bundle (`.msixupload`) を作成します。
- バージョン更新のチェック項目は `docs/ReleaseChecklist.md` を参照します。
- MSI インストーラーによる配布は廃止されました（Store 配布へ移行）。

## 今回の作業ファイル
- `tasks.md`: 次回リリースの作業内容を整理
- `docs/2026-01-05_user_feedback.md`: フィードバック起点の改善点
- `docs/NextRelease-Discussion.md`: 方針が必要な項目の整理
