# 署名付きテストパッケージ（ローカルインストール用）

## 方法 1: msixupload から生成（推奨）

Store 提出用の msixupload ファイルから自己署名済みパッケージを生成します。
**Store に提出するものと同じパッケージ** をローカルでテストできます。

### 生成

```powershell
.\wack\build-from-upload.ps1
```

- `PhotoGeoExplorer\AppPackages\` 配下の最新 msixupload を自動検索
- 初回は PFX のパスワードを尋ねられます
- 証明書を作り直す場合は `-ForceNewCertificate` を付けてください

生成物:

- `PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_SignedTest\PhotoGeoExplorer_*_signed.msix`
- `wack\certs\PhotoGeoExplorer_Test.pfx`
- `wack\certs\PhotoGeoExplorer_Test.cer`

### インストール

```powershell
.\wack\install-from-upload.ps1
```

インストール後に AppxManifest の Resources を確認するコマンドも表示されます。

---

## 方法 2: ソースから直接ビルド

開発中の変更をすぐにテストしたい場合はこちらを使用します。

### 生成

PowerShell で次を実行します。

```powershell
.\wack\build-signed-test.ps1
```

- 初回は PFX のパスワードを尋ねられます。
- 証明書を作り直す場合は `-ForceNewCertificate` を付けてください。

生成物:

- `PhotoGeoExplorer\AppPackages\PhotoGeoExplorer_*_Test\PhotoGeoExplorer_*.msix`
- `wack\certs\PhotoGeoExplorer_Test.pfx`
- `wack\certs\PhotoGeoExplorer_Test.cer`

### インストール

```powershell
.\wack\install-signed-test.ps1
```

---

## ルート証明書の信頼（必須）

`0x800B0109` が出る場合は、以下のスクリプトを実行して（管理者自己昇格） LocalMachine 側にも登録してください。

.\wack\import-cert-admin.ps1

パス指定でインストールする場合:

```powershell
Add-AppxPackage -Path <MSIXのパス>
```

## 補足

- `*.msixupload` は Partner Center へのアップロード専用で、ローカルインストールには使用できません。
- 証明書は `CurrentUser\TrustedPeople` に登録されます。
