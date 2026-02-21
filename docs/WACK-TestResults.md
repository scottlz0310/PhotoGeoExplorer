# Windows アプリ認定キット (WACK) テスト結果

最終更新: 2026-02-21 (v1.7.0 検証)

## テスト結果サマリー

- 結果: 合格 (Required 0)
- テスト総数: 24 (合格 23 / 失敗 1)
- 失敗項目: [88] ブロック済みの実行可能ファイル (Optional)
- アプリ名: PhotoGeoExplorer
- バージョン: 1.7.0.0
- レポート: `scripts\wack_reports\WACK-20260221-150622.xml`

## 実施手順

1. Store 向けパッケージ生成
   ```powershell
   .\scripts\DevInstall.ps1 -Build
   ```
2. WACK 実行
   ```powershell
   .\scripts\RunWackTests.ps1
   ```
3. レポート解析
   ```powershell
   .\scripts\AnalyzeWackReport.ps1
   ```

## メモ

- [88] は Optional 項目であり、Required failure は 0 のため提出可と判断。
- runFullTrust は WinUI 3 デスクトップアプリで必須のため、Partner Center の審査ノートに用途を明記する。
