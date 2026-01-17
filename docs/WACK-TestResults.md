# Windows アプリ認定キット (WACK) テスト結果

最終更新: 2026-01-17 (v1.5.0 検証)

## テスト結果サマリー

- 結果: 合格 (Required 0)
- オプション: [88] ブロック済みの実行可能ファイル
- アプリ名: PhotoGeoExplorer
- バージョン: 1.5.0.0
- 実行パッケージ: PhotoGeoExplorer_1.5.0.0_x64.msixbundle

## 実施手順

1. Store 向けパッケージ生成
   ```powershell
   dotnet publish .\PhotoGeoExplorer\PhotoGeoExplorer.csproj -c Release -p:Platform=x64 `
     -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never `
     -p:UapAppxPackageBuildMode=StoreUpload -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false
   ```
2. WACK 実行
   ```powershell
   .\wack\run-wack.ps1
   ```
3. レポート解析
   ```powershell
   .\wack\analyze-wack.ps1
   ```

## メモ

- runFullTrust は WinUI 3 デスクトップアプリで必須のため、Partner Center の審査ノートに用途を明記します。
- Optional [88] は依存ライブラリ由来の警告が要因で、オンライン審査は通過済みです。
- WACK レポートでは `PhotoGeoExplorer.exe` に `shell32.dll!ShellExecuteW` が検出されます（外部ブラウザ/エクスプローラー起動の `Launcher.LaunchUriAsync` / `LaunchFolderPathAsync` 起因）。
- `Microsoft.WindowsAppRuntime.Bootstrap.dll` 側にも `ShellExecuteExW` 参照があり、ランタイム同梱分の影響が残ります。
- UX 維持のため現状は許容し、Required になった場合のみ削減を検討します。
