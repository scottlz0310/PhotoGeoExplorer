# dotnet-format から組み込み dotnet format への移行

## 背景

単体ツールの `dotnet-format` は .NET 6.0 以降で非推奨となり、.NET SDK に組み込みコマンドとして統合されました。
本リポジトリでは、非推奨の単体ツールではなく組み込みの `dotnet format` を使用するよう更新しています。

## 実施内容

### 1. スタンドアロンツールの依存削除
- **対象ファイル**: `.config/dotnet-tools.json`
- **変更**: `dotnet-format` ツールエントリ（version 5.1.250801）を削除
- **理由**: 単体ツールは非推奨となり不要

### 2. pre-commit フックの更新
- **対象ファイル**: `lefthook.yml`
- **変更前**: `dotnet tool restore && dotnet tool run dotnet-format --check PhotoGeoExplorer.sln`
- **変更後**: `dotnet format --verify-no-changes PhotoGeoExplorer.sln`
- **効果**: ツール復元が不要になり実行が高速化、SDK 組み込みコマンドに統一

### 3. CI/CD ワークフローの更新
- **対象ファイル**: `.github/workflows/quality-check.yml`
- **変更内容**:
  - "Restore dotnet tools" ステップを削除
  - フォーマット確認コマンドを `dotnet format --verify-no-changes PhotoGeoExplorer.sln` に変更
- **効果**: ワークフローの簡素化と CI 実行時間の短縮

## コマンド一覧

### 旧コマンド（非推奨）
```bash
dotnet tool restore
dotnet tool run dotnet-format --check PhotoGeoExplorer.sln
```

### 新コマンド（組み込み）
```bash
# 変更を加えずにフォーマットを確認
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# フォーマットを適用
dotnet format PhotoGeoExplorer.sln
```

## 主な違い

| 項目 | 旧 (dotnet-format) | 新 (dotnet format) |
|---------|---------------------|---------------------|
| インストール | `.config/dotnet-tools.json` 経由の個別ツール | .NET SDK に標準搭載 |
| チェックモード | `--check` | `--verify-no-changes` |
| 実行方法 | `dotnet tool run dotnet-format` | `dotnet format` |
| 復元の必要性 | あり (`dotnet tool restore`) | なし |

## 移行のメリット

1. **個別ツールのインストール不要**: .NET SDK に標準搭載
2. **実行が高速**: ツール復元が不要
3. **統合性向上**: SDK ツールチェーンと統一
4. **将来性**: SDK として継続的にメンテナンス
5. **構成の簡素化**: ツールマニフェストの削減

## テスト

移行が正しく動作するか確認するには以下を実行してください。

```bash
# フォーマット確認（差分があればエラー終了）
dotnet format --verify-no-changes PhotoGeoExplorer.sln

# フォーマットを適用
dotnet format PhotoGeoExplorer.sln
```

## 参考資料

- [dotnet format ドキュメント](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
- [コードスタイル分析の概要](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- [.NET SDK 組み込みツール](https://learn.microsoft.com/en-us/dotnet/core/tools/)
