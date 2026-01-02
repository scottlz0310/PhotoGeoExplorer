# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 言語とコミュニケーション

このリポジトリでは日本語を使用します。AIエージェントの返答、ドキュメント、コミット メッセージはすべて日本語で記述してください。

## プロジェクト概要

PhotoGeoExplorer は、写真の位置情報を地図上に表示する Windows デスクトップアプリです。WinUI 3 と Mapsui を使用し、.NET 10 / C# で開発されています。

## プロジェクト構成

### 主要プロジェクト

- **PhotoGeoExplorer**: WinUI 3 アプリケーション本体
  - `MainWindow.xaml` / `MainWindow.xaml.cs`: メインウィンドウ（UI とコードビハインド）
  - `Models/`, `Services/`, `ViewModels/`: MVVM アーキテクチャ
  - `App.xaml.cs`: アプリケーションエントリポイント、例外ハンドリング、スプラッシュウィンドウ
  - `Program.cs`: カスタムエントリポイント（`DISABLE_XAML_GENERATED_MAIN` により手動制御）

- **PhotoGeoExplorer.Core**: プラットフォーム非依存のビジネスロジック
  - `Models/`: データモデル（`AppSettings`, `PhotoItem`, `PhotoMetadata` など）
  - `Services/`: コアサービス（`ExifService`, `FileSystemService`, `SettingsService`, `ThumbnailService`, `UpdateService`）
  - `ViewModels/`: 共有ビューモデル要素
  - `AppLog.cs`: ログ記録ユーティリティ（`%LocalAppData%\PhotoGeoExplorer\Logs\app.log` に出力）

- **PhotoGeoExplorer.Tests**: xUnit 単体テストとインテグレーションテスト
  - PhotoGeoExplorer.Core の機能をテスト

- **PhotoGeoExplorer.E2E**: エンドツーエンドテスト
  - `PHOTO_GEO_EXPLORER_RUN_E2E=1` 環境変数を設定して実行

- **PhotoGeoExplorer.Installer**: WiX を使用した MSI インストーラプロジェクト

### 重要なアーキテクチャパターン

1. **MVVM パターン**: WinUI 3 アプリは厳格な MVVM を採用
   - `MainViewModel` がメイン UI の状態とロジックを管理
   - `BindableBase` を継承して `INotifyPropertyChanged` を実装
   - コードビハインド（`MainWindow.xaml.cs`）は UI イベントハンドリングと地図初期化を担当

2. **サービス層**: ビジネスロジックをサービスに分離
   - `FileSystemService`: ファイル/フォルダ操作
   - `ExifService`: 写真のメタデータ（GPS 情報）抽出
   - `ThumbnailService`: サムネイル生成（ImageSharp 使用）
   - `SettingsService`: アプリ設定の永続化（JSON、`%LocalAppData%\PhotoGeoExplorer\settings.json`）
   - `UpdateService`: GitHub Release による更新チェック
   - `LocalizationService`: 多言語対応（WinUI 3 リソースシステム）

3. **地図統合**: Mapsui ライブラリを使用
   - `MainWindow.xaml.cs` 内で `MapControl` を初期化
   - `InitializeMapAsync()`: 地図とオフラインタイルキャッシュを設定
   - `UpdateMapFromSelectionAsync()`: 選択された写真のマーカーを地図上に表示
   - タイルキャッシュは `%LocalAppData%\PhotoGeoExplorer\MapCache` に保存

4. **非同期処理**: `async`/`await` を使用し、UI スレッドのブロックを回避
   - 非同期メソッドは必ず `Async` サフィックスを付ける
   - `CancellationTokenSource` を活用してキャンセル処理を実装

5. **ログとエラーハンドリング**:
   - `AppLog.Info()` / `AppLog.Error()` を使用
   - `App.xaml.cs` でグローバル例外をキャッチ
   - ログは起動時にリセットされる（`AppLog.Reset()`）

## ビルド・実行・テストコマンド

### リストア・ビルド
```powershell
dotnet restore PhotoGeoExplorer.sln
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

### 実行
```powershell
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64
```

### テスト実行
```powershell
# 通常のテスト（単体テスト + インテグレーションテスト）
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64

# E2E テストを含む
PHOTO_GEO_EXPLORER_RUN_E2E=1 dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

### 特定のテストクラスやメソッドを実行
```powershell
# 特定のテストクラスを実行
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileSystemServiceTests -c Release -p:Platform=x64

# 特定のテストメソッドを実行
dotnet test PhotoGeoExplorer.Tests --filter FullyQualifiedName~FileSystemServiceTests.GetFiles_ReturnsFiles -c Release -p:Platform=x64
```

### フォーマット・品質チェック
```powershell
# フォーマット検証
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# 警告をエラー扱いでビルド
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64 -p:TreatWarningsAsErrors=true -p:AnalysisLevel=latest
```

### Git フック（lefthook）
```powershell
lefthook install
```

pre-commit: フォーマット検証 + ビルド
pre-push: テスト実行

## コーディング規約

### コードスタイル
- インデント: 4 スペース
- 型名・公開メンバー: `PascalCase`
- private フィールド: `_camelCase` (アンダースコアプレフィックス)
- ローカル変数・パラメータ: `camelCase`
- 非同期メソッド: 必ず `Async` サフィックス
- XAML 要素名: `PascalCase`

### 品質基準
- `Directory.Build.props` により全プロジェクトで厳格な解析設定を適用
- `AnalysisLevel=latest`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`
- 警告はビルドエラーとして扱われるため、コード品質を常に維持

### null 許容参照型
- すべてのプロジェクトで `<Nullable>enable</Nullable>` が設定されている
- null 許容性を明示的に扱う

## 開発サイクル

1. 実行中の `PhotoGeoExplorer.exe` プロセスを終了
2. `dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64` でビルド確認
3. `dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64` で起動
4. `%LocalAppData%\PhotoGeoExplorer\Logs\app.log` でログを確認

## リリース

- タグ（例: `v1.2.0`）を push すると、GitHub Actions で MSI インストーラが自動生成される
- リリース手順の詳細は `docs/ReleaseChecklist.md` を参照

## コミット規約

- コンベンショナルコミット形式を使用
- 例: `Fix: WebView2 startup and map status`, `Feat: 新機能の追加`, `chore(deps): update dependency (#NN)`
- コミットメッセージは日本語で記述

## バージョン確認

AI から見て不自然に新しいバージョンに感じたとしても、勝手にバージョンダウンしたりせずに web で最新情報を調査してください。大抵は学習時期のタイムラグが原因です。
