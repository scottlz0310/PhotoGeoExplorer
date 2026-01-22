# ファイル関連付け起動（FileActivation）検証ガイド

本ドキュメントは、PhotoGeoExplorer のファイル関連付け起動（FileActivation）に関する仕様と検証手順を記載します。

## 概要

PhotoGeoExplorer は、画像ファイルをダブルクリックまたはコンテキストメニューから「プログラムから開く」で起動した際に、そのファイルの親フォルダを自動的に開く機能を提供します。

## 配布形態別の起動経路

### MSIX パッケージ版（Microsoft Store 配布）

| 項目 | 説明 |
|------|------|
| 起動経路 | Windows Shell → FileActivation イベント |
| 引数の取得方法 | `AppInstance.GetCurrent().GetActivatedEventArgs()` |
| ActivationKind | `ExtendedActivationKind.File` |
| 前提条件 | Package.appxmanifest にファイル関連付け設定が必要 |

### MSI インストーラー版

| 項目 | 説明 |
|------|------|
| 起動経路 | Windows Shell → コマンドライン引数 |
| 引数の取得方法 | `Environment.GetCommandLineArgs()` |
| ActivationKind | FileActivation は発火しない |
| 前提条件 | レジストリにファイル関連付けが登録されている |

## 起動時の優先順位

1. **ファイルパス引数がある場合（最優先）**
   - 指定されたファイルの親フォルダを開く
   - 対象ファイルを自動選択する

2. **ファイルパス引数がない場合**
   - 前回終了時のフォルダ（`LastFolderPath`）を復元
   - フォルダが存在しない場合は親フォルダへフォールバック

3. **復元できるフォルダがない場合**
   - Pictures フォルダを開く

## 関連 Issue / PR

- #46: Folder Navigator の Move ボタン挙動修正
  - PR #49 で修正
- #47: 起動時のフォルダ復元処理がファイルパス指定起動より優先される問題
  - PR #50 で修正
- #51: 言語設定問題
  - PR #52 で修正
- #53: 回帰テスト追加

## 検証手順

### 1. MSIX 版での FileActivation 検証

```powershell
# 1. MSIX パッケージをビルド
dotnet publish PhotoGeoExplorer -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64

# 2. パッケージをインストール
# (Visual Studio からデバッグ実行またはストア版をインストール)

# 3. エクスプローラーで画像ファイルを右クリック
# 4. 「プログラムから開く」→「PhotoGeoExplorer」を選択
# 5. アプリが起動し、そのファイルの親フォルダが開かれることを確認
```

### 2. MSI 版でのコマンドライン引数検証

```powershell
# 1. MSI インストーラーからインストール
# 2. コマンドラインから起動
& "C:\Program Files\PhotoGeoExplorer\PhotoGeoExplorer.exe" "C:\Users\Pictures\sample.jpg"

# 3. アプリが起動し、C:\Users\Pictures が開かれることを確認
# 4. sample.jpg が自動選択されていることを確認
```

### 3. E2E テストでの検証

```powershell
# 環境変数を設定してテスト実行
$env:PHOTO_GEO_EXPLORER_RUN_E2E="1"
$env:PHOTO_GEO_EXPLORER_E2E_FOLDER="C:\TestImages"
dotnet test PhotoGeoExplorer.E2E -c Release -p:Platform=x64
```

## 単体テスト

`PhotoGeoExplorer.Tests` プロジェクトに以下の回帰テストが含まれています：

- `StartupPathResolutionTests.cs`: 起動時パス解決のテスト
- `MainViewModelTests.cs`: ナビゲーション関連のテスト
- `LastFolderPathRecoveryTests.cs`: フォルダパス復元のテスト

## 注意事項

1. **MSI 版では FileActivation は発火しない**
   - これは仕様です。代わりにコマンドライン引数が使用されます。

2. **ファイル関連付けの登録**
   - MSIX 版: Package.appxmanifest で自動登録
   - MSI 版: インストーラーでレジストリに登録

3. **テスト環境での注意**
   - CI/CD 環境では WinUI コンポーネントが利用できないため、
     一部のテストはスキップされます。

## 参考リンク

- [Windows App SDK: アクティブ化の概要](https://learn.microsoft.com/ja-jp/windows/apps/windows-app-sdk/applifecycle/applifecycle-rich-activation)
- [ファイル関連付けの設定](https://learn.microsoft.com/ja-jp/windows/uwp/launch-resume/handle-file-activation)
