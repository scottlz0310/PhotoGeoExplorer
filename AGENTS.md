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

## ビルド・テスト・開発コマンド
- `dotnet restore PhotoGeoExplorer.sln` で NuGet を復元します。
- `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64` で解析込みビルドを行います。
- `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64` でローカル実行します。
- `dotnet format --verify-no-changes PhotoGeoExplorer.sln` でフォーマット確認（CI + lefthook）。
- `lefthook install` で pre-commit/pre-push を有効化します。

## 開発時の確認サイクル
- 実行中の `PhotoGeoExplorer` プロセスを終了する
- `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64` でビルド確認
- `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64` で起動確認
- `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` に失敗がないことを確認

## コーディングスタイルと命名規則
- C# は 4 スペースインデント。`MainWindow.xaml` と `*.cs` の既存ルールに合わせます。
- 型と公開メンバーは `PascalCase`、ローカル/フィールドは `camelCase`。private フィールドは `_field` 形式。
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

## セキュリティと設定の注意
- ログは `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` に書き出し、起動時にリセットします。
- Release ワークフローは `v1.2.0` のようなタグで `win-x64` 向け MSI を作成します。
