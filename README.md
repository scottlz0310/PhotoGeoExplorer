# PhotoGeoExplorer

[![Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9P0WNR54441B)

PhotoGeoExplorer は、写真の位置情報を地図上に表示する Windows デスクトップアプリです。
Windows 10/11 を対象とし、WinUI 3 と Mapsui で地図を描画します。

## ステータス

- Microsoft Store で公開済み: https://apps.microsoft.com/detail/9P0WNR54441B
- GitHub リリース: v1.5.1

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

## アーキテクチャ

PhotoGeoExplorer は **Shell + Pane** アーキテクチャを採用しています：

- **MainWindow (Shell)**: レイアウトとペイン配置のみを担当
- **Pane (UserControl)**: 機能単位のUI + ViewModel + Service

この設計により、MainWindow の肥大化を防ぎ、機能の独立性とテスト容易性を確保しています。

詳細は [`docs/Architecture/PaneSystem.md`](docs/Architecture/PaneSystem.md) を参照してください。

**サンプル実装:**
- [`PhotoGeoExplorer/Panes/Settings/`](PhotoGeoExplorer/Panes/Settings/) - 設定Paneのサンプル実装

## 前提条件

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2026 (WinUI 3 ワークロード。IDE で使う場合は任意)
- WebView2 Runtime

詳細は [Developer Guide](docs/DeveloperGuide.md) を参照してください。

## 開発インストール (Local Dev Install)

Store 提出用パッケージ相当をローカルでビルド・署名・インストールするには、以下のスクリプトを使用します（Windows SDK が必要です）。

```powershell
.\scripts\DevInstall.ps1 -Build
```

## ビルド (Core Build)

コマンドラインでの単純ビルド:

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

## テスト

```powershell
dotnet test PhotoGeoExplorer.sln -c Release -p:Platform=x64
```

E2E は通常スキップします。UIA (FlaUI) による WinUI 自動化は、デスクトップの対話セッションが必要で
環境依存の不安定さがあるため、日常の品質確認は手動テストを重視する方針です。
実行する場合は `PHOTO_GEO_EXPLORER_RUN_E2E=1` を指定して手動環境で実行してください。

## Microsoft Store

Microsoft Store 版は公開済みです: https://apps.microsoft.com/detail/9P0WNR54441B
申請/運用メモは `docs/MicrosoftStore.md`、WACK 結果は `docs/WACK-TestResults.md` にまとめています。

## MSI インストール

MSI インストーラーによる配布は v1.5.1 で終了しました。
以降は Microsoft Store または上記の手順でインストールしてください。

## MSIX (試験的)

MSIX を使う場合は、証明書の信頼登録が必要です。以下のスクリプトで、署名済みパッケージの生成とインストール（証明書含）を一括で行えます。

```powershell
./scripts/DevInstall.ps1
```

アンインストールする場合:

```powershell
./scripts/DevInstall.ps1 -Clean
```

管理者権限で動作し、証明書は LocalMachine にインポートされます。
ストア向けパッケージ生成やテストの詳細は `docs/DeveloperGuide.md` を参照してください。

## ライセンス

このプロジェクトは MIT License の下でライセンスされています。詳細は [LICENSE](LICENSE) ファイルを参照してください。

## プライバシーポリシー

プライバシーポリシーは [こちら](https://scottlz0310.github.io/PhotoGeoExplorer/privacy-policy.html) で確認できます。
