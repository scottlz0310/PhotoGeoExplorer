<#
.SYNOPSIS
    Store 提出用の .msixupload（MSIX Bundle）を Release / x64 でビルドする。

.DESCRIPTION
    Release ワークフローと、PR 時の WACK ワークフローが同じビルドを使うための共通入口。
    WACK は提出するパッケージと同じビルドで検査しないと、Release で不合格になるものを見逃すため、
    ビルド手順を 2 箇所に書かない。成果物は PhotoGeoExplorer\AppPackages 以下に出力される。
    事前に dotnet restore が必要。署名はしない（署名は Store 側）。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'PhotoGeoExplorer\PhotoGeoExplorer.csproj'

dotnet publish $project `
    -c Release `
    -p:Platform=x64 `
    -p:WindowsPackageType=MSIX `
    -p:AppxBundle=Always `
    -p:AppxBundlePlatforms=x64 `
    -p:UapAppxPackageBuildMode=StoreUpload `
    -p:AppxPackageSigningEnabled=false `
    -p:GenerateAppxPackageOnBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました（exit $LASTEXITCODE）。" }
