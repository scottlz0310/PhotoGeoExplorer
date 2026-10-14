# Windows アプリ認定キット (WACK) テスト結果

> [!NOTE]
> **この文書は v1.9.0 までの履歴で、以後は追記しない。** WACK は Release ワークフローが実行して合否を判定し
> （`.github/workflows/release.yml` の `WACK gate`、`scripts/Invoke-WackGate.ps1`）、報告書
> `wack-report-<版>.xml` を GitHub Release と artifact `wack-report` に残す。v1.8.6 の欠落は、記録が手書きだったことの名残で、遡って補わない。

最終更新: 2026-07-22 (v1.9.0 検証)

## テスト結果サマリー

- 結果: 合格 (Required 0)
- テスト総数: 24 (合格 23 / 失敗 1)
- 失敗項目: [88] ブロック済みの実行可能ファイル (Optional)
- アプリ名: PhotoGeoExplorer
- バージョン: 1.9.0.0
- レポート: `scripts\wack_reports\WACK-20260722-234219.xml`（ローカル手動実行）

## 過去の結果

| バージョン | 日付 | 結果 | Required 失敗 | レポート |
|-----------|------|------|--------------|---------|
| 1.9.0.0 | 2026-07-22 | 合格 | 0 | `WACK-20260722-234219.xml` |
| 1.8.5.0 | 2026-06-09 | 合格 | 0 | `WACK-20260609-151555.xml` |
| 1.8.0.0 | 2026-05-24 | 合格 | 0 | `WACK-20260524-202442.xml` |
| 1.7.2.0 | 2026-03-06 | 合格 | 0 | `WACK-20260306-111122.xml` |

## 実施手順

1. Store 向けパッケージ生成
   ```powershell
   .\scripts\DevInstall.ps1
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
