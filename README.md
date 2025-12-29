# PhotoGeoExplorer

PhotoGeoExplorer は、写真の位置情報を地図上に表示する Windows デスクトップアプリです。
Windows 10/11 を対象とし、WinUI 3 と Mapsui で地図を描画します。

## ステータス

このリポジトリは機能実装済みの段階で、品質・リリース整備を進めています。

## 現在の機能

- ファイルブラウザ（フォルダ選択、検索/画像フィルタ、パンくず、表示切替）
- ファイル操作（新規フォルダ/移動/リネーム/削除、ドラッグ&ドロップ）
- EXIF/GPS 抽出、複数選択の地図マーカー表示と自動フィット
- 画像プレビュー（ズーム/パン/最大化、前後ナビ）
- Mapsui による地図表示とオフラインタイルキャッシュ
- 設定の永続化、言語/テーマ切替、設定のエクスポート/インポート
- `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` へのアプリログ出力

## 予定している機能

- ドキュメント整備（README/コメント）
- テストスイート追加と CI 組み込み
- リリースワークフローの完成
- アップデート機能

## 技術スタック

- .NET 10 / C# (WinUI 3, Windows App SDK)
- Mapsui による地図描画
- EXIF 抽出に MetadataExtractor
- サムネイル/画像処理に SixLabors.ImageSharp

## 前提条件

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2026 (WinUI 3 ワークロード。IDE で使う場合は任意)
- WebView2 Runtime

## ビルド

```powershell
dotnet restore PhotoGeoExplorer.sln
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

## 実行

```powershell
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64
```

## フォーマット/品質チェック

```powershell
dotnet format --verify-no-changes PhotoGeoExplorer.sln
dotnet build PhotoGeoExplorer.sln -c Release -p:Platform=x64 -p:TreatWarningsAsErrors=true -p:AnalysisLevel=latest
```

任意のフック:

```powershell
lefthook install
```

## リリース成果物

タグの push (例: `v0.1.0`) を契機に、`win-x64` 向けの未署名 MSIX インストーラーを作成します。

## ライセンス

`LICENSE` が追加されている場合はそちらを参照してください。未追加の場合は現状未ライセンスです。
