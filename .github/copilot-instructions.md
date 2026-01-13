# GitHub Copilot Instructions for PhotoGeoExplorer

このファイルは GitHub Copilot コーディングエージェントがこのリポジトリで作業する際のガイドラインです。

## 🌏 言語とコミュニケーション

**重要**: このリポジトリでは日本語を使用します。
- AIエージェントの返答は日本語で行う
- ドキュメントは日本語で記述
- コミットメッセージも日本語で記述
- 英語のドキュメントを発見した場合は日本語版を作成し、英語版を置き換える

## 📋 プロジェクト概要

PhotoGeoExplorer は、写真の位置情報（GPS/EXIF）を地図上に表示する Windows デスクトップアプリケーションです。

### 技術スタック
- **.NET 10 / C# 13**
- **WinUI 3** (Windows App SDK)
- **Mapsui** - 地図描画ライブラリ
- **MetadataExtractor** - EXIF データ抽出
- **SixLabors.ImageSharp** - 画像処理とサムネイル生成

### アーキテクチャパターン
- **MVVM (Model-View-ViewModel)** パターンを厳格に適用
- **サービス指向アーキテクチャ** - ビジネスロジックはサービス層に分離
- **非同期プログラミング** - `async`/`await` を使用し UI スレッドをブロックしない

## 📁 プロジェクト構成

```
PhotoGeoExplorer/
├── PhotoGeoExplorer/              # WinUI 3 メインアプリケーション
│   ├── MainWindow.xaml(.cs)       # メインウィンドウ (UI + コードビハインド)
│   ├── App.xaml(.cs)              # アプリエントリポイント、例外ハンドリング
│   ├── Program.cs                 # カスタムエントリポイント
│   ├── Models/                    # UI 層のモデル
│   ├── Services/                  # UI 層のサービス
│   └── ViewModels/                # MVVM ビューモデル
│
├── PhotoGeoExplorer.Core/         # プラットフォーム非依存コア
│   ├── Models/                    # データモデル (AppSettings, PhotoItem, etc.)
│   ├── Services/                  # コアサービス (ExifService, FileSystemService, etc.)
│   ├── ViewModels/                # 共有ビューモデル
│   └── AppLog.cs                  # ログユーティリティ
│
├── PhotoGeoExplorer.Tests/        # xUnit 単体テスト
├── PhotoGeoExplorer.E2E/          # E2E テスト
└── PhotoGeoExplorer.Installer/    # WiX MSI インストーラ
```

### 重要なサービス
- **FileSystemService**: ファイル/フォルダ操作
- **ExifService**: 写真メタデータ（GPS 情報）抽出
- **ThumbnailService**: サムネイル生成
- **SettingsService**: アプリ設定の永続化（JSON、`%LocalAppData%\PhotoGeoExplorer\settings.json`）
- **UpdateService**: GitHub Release による更新チェック
- **LocalizationService**: 多言語対応

### 地図統合
- `MainWindow.xaml.cs` 内で Mapsui の `MapControl` を初期化
- `InitializeMapAsync()`: 地図とオフラインタイルキャッシュを設定
- `UpdateMapFromSelectionAsync()`: 選択された写真のマーカーを地図上に表示
- タイルキャッシュ: `%LocalAppData%\PhotoGeoExplorer\MapCache`

### ログとエラーハンドリング
- `AppLog.Info()` / `AppLog.Error()` を使用
- ログファイル: `%LocalAppData%\PhotoGeoExplorer\Logs\app.log`
- `App.xaml.cs` でグローバル例外をキャッチ
- ログは起動時にリセットされる（`AppLog.Reset()`）

## 🔨 ビルド・実行・テストコマンド

### 依存関係の復元
```powershell
dotnet restore PhotoGeoExplorer.sln
```

### ビルド
```powershell
# リリースビルド (推奨)
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64

# デバッグビルド
dotnet build PhotoGeoExplorer.sln -c Debug -p:Platform=x64
```

### 実行
```powershell
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64
```

### テスト

```powershell
# 通常のテスト（単体テスト + インテグレーションテスト）
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64

# E2E テストを含む（環境変数設定が必要）
$env:PHOTO_GEO_EXPLORER_RUN_E2E="1"
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64

# 特定のテストクラスを実行
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileSystemServiceTests -c Release -p:Platform=x64

# 特定のテストメソッドを実行
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileSystemServiceTests.GetFiles_ReturnsFiles -c Release -p:Platform=x64
```

### フォーマット・品質チェック

```powershell
# フォーマット検証
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# フォーマット自動修正
dotnet format PhotoGeoExplorer.sln

# 警告をエラー扱いでビルド
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64 -p:TreatWarningsAsErrors=true -p:AnalysisLevel=latest
```

### Git フック (Lefthook)

```powershell
# Git フックのインストール
lefthook install
```

- **pre-commit**: フォーマット検証 + ビルド
- **pre-push**: テスト実行

## 📝 コーディング規約

### コードスタイル

#### インデントとフォーマット
- **インデント**: 4 スペース（タブは使用しない）
- 既存の `.editorconfig` に従う

#### 命名規則
- **型名・公開メンバー**: `PascalCase`
  - 例: `PhotoItem`, `MainViewModel`, `GetPhotosAsync()`
- **private フィールド**: `_camelCase` (アンダースコアプレフィックス)
  - 例: `_fileSystemService`, `_cancellationTokenSource`
- **ローカル変数・パラメータ**: `camelCase`
  - 例: `photoItems`, `folderPath`
- **非同期メソッド**: 必ず `Async` サフィックスを付ける
  - 例: `InitializeMapAsync()`, `LoadPhotosAsync()`
- **XAML 要素名**: `PascalCase`
  - 例: `MapStatusText`, `PhotoListView`

#### null 許容参照型
- すべてのプロジェクトで `<Nullable>enable</Nullable>` が設定されている
- null 許容性を明示的に扱う
- `string?` と `string` を適切に使い分ける

### 品質基準

#### コード解析
- `Directory.Build.props` により全プロジェクトで厳格な解析設定を適用:
  - `AnalysisLevel=latest`
  - `TreatWarningsAsErrors=true`
  - `EnforceCodeStyleInBuild=true`
- **警告はビルドエラーとして扱われる** ため、コード品質を常に維持する

#### 非同期処理のベストプラクティス
- UI 操作は必ず UI スレッドで実行
- 長時間処理は `Task.Run()` でバックグラウンドスレッドへ
- `CancellationTokenSource` を活用してキャンセル処理を実装
- デッドロックを避けるため `ConfigureAwait(false)` を適切に使用（UI スレッドが不要な場合）

#### MVVM パターンの遵守
- ビューモデルは `BindableBase` を継承し `INotifyPropertyChanged` を実装
- コードビハインドは UI イベントハンドリングに限定
- ビジネスロジックはサービス層に配置

## 🔄 開発ワークフロー

### 標準開発サイクル

1. **既存プロセスの終了**: 実行中の `PhotoGeoExplorer.exe` プロセスを終了
2. **ビルド**: `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64`
3. **実行**: `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64`
4. **ログ確認**: `%LocalAppData%\PhotoGeoExplorer\Logs\app.log` でエラーや警告を確認

### コード変更時の確認事項

1. **フォーマット**: `dotnet format --verify-no-changes PhotoGeoExplorer.sln`
2. **ビルド**: エラーや警告がないことを確認
3. **テスト**: 関連するテストを実行して動作を検証
4. **手動テスト**: UI やアプリの動作を実際に確認
5. **ログ確認**: エラーログが出力されていないことを確認

### 変更の検証

#### UI 変更の場合
- スクリーンショットを撮影して視覚的に確認
- 異なるウィンドウサイズで表示を確認
- テーマ（Light/Dark）両方で確認

#### 機能追加の場合
- 単体テストを追加
- インテグレーションテストを追加（必要に応じて）
- ログ出力を適切に追加

## 🧪 テスト戦略

### テストの種類

#### 単体テスト (`PhotoGeoExplorer.Tests`)
- xUnit フレームワークを使用
- サービス層のロジックをテスト
- モックは必要に応じて使用

#### E2E テスト (`PhotoGeoExplorer.E2E`)
- 環境変数 `PHOTO_GEO_EXPLORER_RUN_E2E=1` で有効化
- アプリ全体の統合動作をテスト
- CI/CD では通常スキップ（ローカル実行推奨）

### テストの実行方針

- **変更前**: 既存テストが通ることを確認（回帰テスト）
- **変更後**: 新規・修正したコードに対応するテストを追加/更新
- **コミット前**: 最低限関連するテストを実行
- **プッシュ前**: 全テストを実行（Git フックで自動化）

### テストのベストプラクティス

- テストメソッド名は動作を明確に表現: `GetFiles_ReturnsFiles`
- AAA パターン (Arrange-Act-Assert) に従う
- テストは独立して実行可能にする
- 外部依存（ファイルシステム、ネットワーク）は最小限に

## 📦 リリースプロセス

### バージョニング

- セマンティックバージョニング: `v{major}.{minor}.{patch}`
- 例: `v1.3.0`, `v1.3.1`

### リリース手順

1. バージョン番号を更新
2. CHANGELOG.md を更新
3. タグを作成: `git tag v1.3.0`
4. タグをプッシュ: `git push origin v1.3.0`
5. GitHub Actions が自動的に MSI インストーラーを生成

詳細は `docs/ReleaseChecklist.md` を参照。

### 成果物

- **MSI インストーラー**: `win-x64` 向け
- **Microsoft Store**: 別途申請プロセスあり（`docs/MicrosoftStore.md` 参照）

## 💬 コミット・PR ガイドライン

### コミットメッセージ

コンベンショナルコミット形式を使用（日本語）:

```
<type>: <説明>

<詳細（オプション）>
```

**Type の例**:
- `Fix`: バグ修正
- `Feat`: 新機能追加
- `Docs`: ドキュメント変更
- `Style`: コードスタイル修正（動作変更なし）
- `Refactor`: リファクタリング
- `Test`: テスト追加・修正
- `chore`: ビルドプロセスや補助ツールの変更
- `chore(deps)`: 依存関係更新（Renovate 形式）

**例**:
```
Fix: WebView2 起動時の地図ステータス表示
Feat: 衛星地図タイルの切り替え機能
chore(deps): update dependency Mapsui to v5.0.0 (#123)
```

### Pull Request

PR には以下を含める:
1. **要約**: 変更内容の概要
2. **理由**: なぜこの変更が必要か
3. **検証方法**: どのようにテストしたか（コマンド/ログ）
4. **UI 変更**: スクリーンショットを添付
5. **関連 Issue**: Issue 番号をリンク

## ⚠️ セキュリティと注意事項

### セキュリティ

- ログに機密情報を出力しない
- ユーザー入力は適切にバリデーション
- ファイルパスは検証してから使用

### 互換性

- Windows 10/11 をサポート
- .NET 10 Runtime が必要
- WebView2 Runtime が必要

### パフォーマンス

- 大量の画像を扱うため、非同期処理を徹底
- メモリリークに注意（特に画像処理）
- UI スレッドをブロックしない

## 🔍 トラブルシューティング

### ビルドエラー

1. `dotnet restore` を再実行
2. `bin/` と `obj/` フォルダを削除して再ビルド
3. .NET SDK のバージョンを確認

### テストエラー

1. テストプロジェクトを個別にビルド
2. E2E テストは環境変数を確認
3. ログファイルで詳細を確認

### 実行エラー

1. `%LocalAppData%\PhotoGeoExplorer\Logs\app.log` を確認
2. WebView2 Runtime がインストールされているか確認
3. .NET 10 Runtime がインストールされているか確認

## 📚 参考ドキュメント

- `README.md`: プロジェクト概要
- `AGENTS.md`: AI エージェント向けガイドライン
- `CLAUDE.md`: Claude Code 向け詳細ガイド
- `CHANGELOG.md`: 変更履歴
- `docs/ReleaseChecklist.md`: リリース手順
- `docs/MicrosoftStore.md`: Microsoft Store 申請メモ

## 🤖 AI エージェント利用時の注意

### バージョン確認
AI から見て不自然に新しいバージョンに感じたとしても、勝手にバージョンダウンしたりせずに web で最新情報を調査してください。大抵は学習時期のタイムラグが原因です。

### 既存コードの尊重
- 既存のコードスタイルに従う
- 動作しているコードは不用意に変更しない
- リファクタリングは慎重に（テストでカバー）

### 最小限の変更
- タスクに必要な最小限の変更に留める
- 無関係なコードの修正は避ける
- 一度に複数の機能を実装しない
