# 署名付きテストパッケージ（ローカルインストール用）

## 生成

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

## インストール

```powershell
.\wack\install-signed-test.ps1
```

### ルート証明書の信頼（必須）

`0x800B0109` が出る場合は、管理者 PowerShell で次を実行して LocalMachine 側にも登録してください。

```powershell
Import-Certificate -FilePath .\wack\certs\PhotoGeoExplorer_test.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\wack\certs\PhotoGeoExplorer_test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

パス指定でインストールする場合:

```powershell
Add-AppxPackage -Path <MSIXのパス>
```

## 補足

- `*.msixupload` は Partner Center へのアップロード専用で、ローカルインストールには使用できません。
- 証明書は `CurrentUser\TrustedPeople` に登録されます。
