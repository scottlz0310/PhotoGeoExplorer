# PhotoGeoExplorer

[![Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9P0WNR54441B)

PhotoGeoExplorer は、写真の位置情報を地図上に表示する Windows デスクトップアプリです。
Windows 10/11 を対象とし、WinUI 3 と Mapsui で地図を描画します。

## ステータス

- Microsoft Store で公開済み: https://apps.microsoft.com/detail/9P0WNR54441B
- GitHub リリース: v1.3.0

## 現在の機能

- ファイルブラウザ（フォルダ選択、検索/画像フィルタ、パンくず、表示切替）
- ファイル操作（新規フォルダ/移動/リネーム/削除、ドラッグ&ドロップ）
- EXIF/GPS 抽出、複数選択の地図マーカー表示と自動フィット
- **地図上での矩形選択**: Ctrl + ドラッグで複数の写真を一括選択
- 地図マーカークリック時のツールチップ（撮影日時、カメラ、ファイル名、解像度、Google Maps リンク）
- 衛星地図タイル（Esri WorldImagery）と OpenStreetMap の切り替え
- 画像プレビュー（ズーム/パン/最大化、前後ナビ）
- Mapsui による地図表示とオフラインタイルキャッシュ
- 設定の永続化、言語/テーマ切替、設定のエクスポート/インポート
- アップデート通知（GitHub Release 参照）
- `%LocalAppData%\\PhotoGeoExplorer\\Logs\\app.log` へのアプリログ出力

## 今後の方向性

- 長期的機能追加に向けた設計見直し
- パフォーマンス/非同期最適化
- Microsoft Store 版の改善とフィードバック反映

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

タグの push (例: `v1.3.0`) を契機に、`win-x64` 向けの MSI インストーラーを作成します。
リリース作業のチェックリストは `docs/ReleaseChecklist.md` を参照してください。

## Microsoft Store

Microsoft Store 版は公開済みです: https://apps.microsoft.com/detail/9P0WNR54441B
申請/運用メモは `docs/MicrosoftStore.md`、WACK 結果は `docs/WACK-TestResults.md` にまとめています。

## MSI インストール

配布済み MSI を実行してインストールしてください。

```powershell
msiexec /i PhotoGeoExplorer.msi
```

## MSIX (試験的)

MSIX を使う場合は、証明書の信頼登録が必要です。配布済み MSIX と CER を同じフォルダ（カレントディレクトリまたは `scripts/`）に置き、次のスクリプトで導入できます。

```powershell
./scripts/install.ps1
```

削除する場合は:

```powershell
./scripts/uninstall.ps1
```

管理者権限で LocalMachine にインポートする場合は `-Machine` を付けます。
署名付きテストパッケージの生成/導入手順は `wack/signed-test-package.md` を参照してください。

## ライセンス

このプロジェクトは MIT License の下でライセンスされています。詳細は [LICENSE](LICENSE) ファイルを参照してください。

## プライバシーポリシー

プライバシーポリシーは [こちら](https://scottlz0310.github.io/PhotoGeoExplorer/privacy-policy.html) で確認できます。
