# 開発者ガイド (Developer Guide)

このドキュメントでは、PhotoGeoExplorer の開発環境のセットアップと、ローカルでのビルド・インストール手順について説明します。

## 🛠️ 前提条件 (Prerequisites)

開発およびローカルインストール（`DevInstall.ps1`）を実行するには、以下のツールが必要です。

### 1. オペレーティングシステム
- **Windows 10 バージョン 2004 (Build 19041)** 以降、または Windows 11
- **開発者モード** の有効化を推奨（必須ではありませんが、スムーズです）
  - 設定 > プライバシーとセキュリティ > 開発者向け > 開発者モード

### 2. .NET SDK
- **.NET 10 SDK** (Preview)
  - [Download .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

### 3. Visual Studio 2022 (v17.2 以降)
ビルド自体はコマンドラインでも可能ですが、MSIX パッケージ作成ツール (`MakeAppx.exe`, `SignTool.exe`) を入手するために、以下のコンポーネントのインストールが必要です。

Visual Studio Installer で以下を選択してください：

- ワークロード: **.NET デスクトップ開発**
  - オプション: **Windows App SDK C# テンプレート**
- ワークロード: **ユニバーサル Windows プラットフォーム開発**
  - **重要**: これを選択すると **Windows 10 SDK** がインストールされます（`MakeAppx.exe` 等が含まれます）。
  - 個別に [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-10-sdk/) をインストールしても構いません。

## 🚀 ローカルビルドとインストール

### 非パッケージ実行（開発・E2E）

```powershell
dotnet run --project PhotoGeoExplorer/PhotoGeoExplorer.csproj -c Release -p:Platform=x64
```

`WindowsPackageType=None` のビルドでは、参照する Windows App SDK のランタイムを出力先に同梱します。
Windows App Runtime の別途インストールやユーザーへの登録は不要です。
SDK の registration-free 自動初期化を使用し、外部ランタイムへの bootstrap は行いません。
.NET 自体を同梱する設定ではないため、対応する .NET の実行環境は必要です。

MSIX ビルドは従来どおり framework-dependent で、Store が宣言された依存ランタイムを解決します。
`WindowsAppSDKSelfContained=false` を明示した非パッケージビルドを使う場合は、参照 SDK に対応する Windows App Runtime が実行ユーザーに登録されている必要があります。
bootstrap の要求バージョンは SDK の `Microsoft.WindowsAppSDK.Release.MajorMinor` から取得します。

参考: [Windows App SDK の self-contained 配布](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)

リポジトリには、Store 提出用と同じ構成（MSIX）でビルドし、自己署名証明書で署名してインストールするスクリプトが含まれています。

### ワンステップ・インストール

PowerShell を開き、以下のコマンドを実行します。

```powershell
.\scripts\DevInstall.ps1
```

このコマンドは以下の処理を自動で行います：
1. **ビルド**: Store Upload モードでリリースビルドを実行し、`msixupload` を生成します。
2. **証明書作成**: ローカル開発用の自己署名証明書（`PhotoGeoExplorer Local Debug`）を作成します（初回のみ）。
3. **署名**: 生成されたパッケージを展開し、ローカル証明書で署名し直します。
4. **証明書インストール**: 証明書を信頼されたストアにインストールします。
   - ※ 初回のみ、**ユーザーアカウント制御 (UAC)** のプロンプトが表示されます。「はい」を選択してください。
   - WinUI 3 アプリ (`runFullTrust`) の要件として、証明書を `Local Machine` ストアに登録する必要があります。
5. **アプリインストール**: 署名済みパッケージをシステムにインストールします。

### 再インストール（ビルド再利用）

コードを変更せず、既存のビルド成果物を再利用してパッケージ化とインストールだけやり直したい場合（または署名エラーのデバッグ時など）は `-ReuseBuild` を指定します。

```powershell
.\scripts\DevInstall.ps1 -ReuseBuild
```

### クリーンアップ

一時ファイルや証明書を削除してリセットしたい場合：

```powershell
.\scripts\DevInstall.ps1 -Clean
```

## 🐛 トラブルシューティング

### "MakeAppx.exe not found" エラー
Windows SDK がインストールされていません。Visual Studio Installer から「ユニバーサル Windows プラットフォーム開発」ワークロードを追加するか、Windows SDK を単体でインストールしてください。

### エラー 0x800B0109 (証明書チェーンエラー)
証明書が正しく信頼されていません。
- スクリプトが管理者権限（UAC）を求めた際に「いいえ」を押していませんか？
- 手動で `scripts/certs/PhotoGeoExplorer_LocalDebug.cer` を「ローカルコンピューター」＞「信頼されたルート証明機関」および「信頼されたユーザー」にインストールしてみてください。

### ビルドエラー
- .NET 10 SDK が正しくパスに通っているか確認してください (`dotnet --version`)。
- ソリューションの復元を試してください: `dotnet restore PhotoGeoExplorer.sln`

## 🤖 CI / E2E 運用

- E2E ワークフロー（`.github/workflows/e2e.yml`）は `pull_request` / `push`（`main`, `develop`）で自動実行されます。
- E2E 実行時は、成功/失敗に関わらず以下を artifact として保存します。
  - テスト結果（TRX）
  - アプリログ（`%LocalAppData%\PhotoGeoExplorer\Logs`）
  - 失敗解析用スクリーンショット（`%TEMP%\PhotoGeoExplorerE2E\Diagnostics`）
- 運用が安定したら、`main` の Branch protection で `E2E / e2e` を Required check に設定します。
  - GitHub: `Settings` > `Branches` > `main` ルール > `Require status checks to pass before merging`

## 📁 スクリプト構成

- `scripts/DevInstall.ps1`: メインのインストールスクリプト
- `scripts/certs/`: 生成された自己署名証明書が格納されます（.gitignore 対象）
- `scripts/temp/`: 作業用の一時フォルダ（.gitignore 対象）
