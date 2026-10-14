<#
.SYNOPSIS
    Release ワークフロー用の WACK ゲート。合格しなければ失敗して、GitHub Release の作成と Store 提出へ進ませない。

.DESCRIPTION
    ビルド済みの .msixupload から MSIX を取り出し、検査用に Publisher を差し替えて自己署名したコピーを作り、
    appcert.exe で WACK を実行する。報告書は OutputDir へ保存し、WackGate.psm1 の規則で合否を判定する。

    検査するのは「同じ MSIX の中身を、Publisher と署名だけ差し替えたコピー」で、Store へ提出するファイル
    （.msixupload）そのものではない。提出物は変更しない。
    管理者権限が必要（証明書を LocalMachine\TrustedPeople へ入れる）。CI のランナーで実行することを想定している。
    手元での確認には RunWackTests.ps1 を使う。

.PARAMETER PackagesDir
    .msixupload を含むディレクトリ（再帰的に検索する。ちょうど 1 件であること）。

.PARAMETER ManifestPath
    期待する版（Identity Version）を読む Package.appxmanifest。

.PARAMETER OutputDir
    WACK の報告書（wack-report-<版>.xml）の保存先。

.PARAMETER AssetsPath
    Windows App Runtime の版を調べる project.assets.json。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagesDir,
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$OutputDir,
    [string]$AssetsPath = (Join-Path (Split-Path -Parent $ManifestPath) 'obj\project.assets.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'WackGate.psm1') -Force

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '管理者権限が必要です（証明書を LocalMachine\TrustedPeople へ入れるため）。'
}

$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows SDK が見つかりません。' }
$makeAppx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signTool = Join-Path $sdkBin.FullName 'x64\signtool.exe'
$appCert = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe"
foreach ($tool in $makeAppx, $signTool, $appCert) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "必要なツールが見つかりません: $tool" }
}

[xml]$sourceManifest = Get-Content -LiteralPath $ManifestPath -Raw
$expectedVersion = $sourceManifest.Package.Identity.Version

$uploads = @(Get-ChildItem -LiteralPath $PackagesDir -Recurse -File -Filter *.msixupload)
if ($uploads.Count -ne 1) {
    throw ".msixupload が 1 件ではありません（$($uploads.Count) 件）: $PackagesDir"
}

$workDir = Join-Path ([IO.Path]::GetTempPath()) "wack-gate-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $workDir, $OutputDir | Out-Null
# appcert の /reportoutputpath は絶対パスしか受け付けない。
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$certThumbprint = $null

try {
    # --- msixupload → msixbundle → msix ---
    Expand-Archive -LiteralPath $uploads[0].FullName -DestinationPath (Join-Path $workDir 'upload') -Force
    $bundle = @(Get-ChildItem (Join-Path $workDir 'upload') -Filter *.msixbundle)
    if ($bundle.Count -ne 1) { throw ".msixupload 内の .msixbundle が 1 件ではありません（$($bundle.Count) 件）。" }
    Expand-Archive -LiteralPath $bundle[0].FullName -DestinationPath (Join-Path $workDir 'bundle') -Force
    $msix = @(Get-ChildItem (Join-Path $workDir 'bundle') -Filter *.msix)
    if ($msix.Count -ne 1) { throw ".msixbundle 内の .msix が 1 件ではありません（$($msix.Count) 件）。" }

    # --- 検査用コピー: Publisher を差し替えて、自己署名する ---
    $content = Join-Path $workDir 'msix_content'
    & $makeAppx unpack /p $msix[0].FullName /d $content /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx unpack に失敗しました（exit $LASTEXITCODE）。" }

    $manifestFile = Join-Path $content 'AppxManifest.xml'
    [xml]$manifest = Get-Content -LiteralPath $manifestFile -Raw
    $packageVersion = $manifest.Package.Identity.Version
    if ($packageVersion -ne $expectedVersion) {
        throw "MSIX の版 '$packageVersion' が、マニフェストの版 '$expectedVersion' と一致しません。"
    }

    $subject = 'CN=PhotoGeoExplorer WACK CI'
    $manifest.Package.Identity.Publisher = $subject
    $manifest.Save($manifestFile)

    $testMsix = Join-Path $workDir 'PhotoGeoExplorer_WackGate.msix'
    & $makeAppx pack /d $content /p $testMsix /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack に失敗しました（exit $LASTEXITCODE）。" }

    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -NotAfter (Get-Date).AddDays(1)
    $certThumbprint = $cert.Thumbprint
    $cerFile = Join-Path $workDir 'wack-gate.cer'
    Export-Certificate -Cert $cert -FilePath $cerFile | Out-Null
    & $signTool sign /fd SHA256 /sha1 $certThumbprint $testMsix
    if ($LASTEXITCODE -ne 0) { throw "signtool sign に失敗しました（exit $LASTEXITCODE）。" }
    Import-Certificate -FilePath $cerFile -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

    # --- framework 依存（Windows App Runtime）が無ければ、参照 SDK と同じ版を NuGet パッケージから導入する ---
    foreach ($dependency in @($manifest.Package.Dependencies.PackageDependency)) {
        $installed = @(Get-AppxPackage -AllUsers -Name $dependency.Name |
            Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]$dependency.MinVersion })
        if ($installed.Count -gt 0) { continue }

        Write-Host "framework '$($dependency.Name)' $($dependency.MinVersion) 以上が無いため、NuGet パッケージから導入します。"
        if (-not (Test-Path -LiteralPath $AssetsPath)) { throw "project.assets.json が見つかりません（dotnet restore が必要）: $AssetsPath" }
        $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
        $runtime = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.WindowsAppSDK.Runtime/*' })
        if ($runtime.Count -ne 1) { throw "project.assets.json の Microsoft.WindowsAppSDK.Runtime が 1 件ではありません。" }
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
        $runtimeDir = Join-Path $nugetRoot "microsoft.windowsappsdk.runtime\$(($runtime[0] -split '/')[1])\tools\MSIX\win10-x64"
        foreach ($name in 'Microsoft.WindowsAppRuntime.2.msix', 'Microsoft.WindowsAppRuntime.Main.2.msix',
            'Microsoft.WindowsAppRuntime.Singleton.2.msix', 'Microsoft.WindowsAppRuntime.DDLM.2.msix') {
            Add-AppxPackage -Path (Join-Path $runtimeDir $name) -ErrorAction Stop
        }
    }

    # --- WACK ---
    $report = Join-Path $OutputDir "wack-report-$expectedVersion.xml"
    & $appCert reset
    & $appCert test -appxpackagepath $testMsix -reportoutputpath $report
    # 終了コードは、検査の完了と合否を区別しない。合否は報告書の内容で判定する。
    Write-Host "appcert exit $LASTEXITCODE"

    $summary = Get-WackReportSummary -ReportPath $report
    Write-Host "WACK OVERALL_RESULT=$($summary.OverallResult) tests=$($summary.TestCount) APP_VERSION=$($summary.AppVersion)"
    foreach ($test in $summary.FailedTests) {
        Write-Host ("  FAIL [{0}] {1} (optional={2})" -f $test.Index, $test.Name, $test.Optional)
    }
    Assert-WackGate -Summary $summary -ExpectedVersion $expectedVersion
    Write-Host "WACK のゲートを通過しました: $report"
}
finally {
    if ($certThumbprint) {
        foreach ($store in 'Cert:\LocalMachine\TrustedPeople', 'Cert:\CurrentUser\My') {
            Get-ChildItem $store | Where-Object { $_.Thumbprint -eq $certThumbprint } | Remove-Item -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
