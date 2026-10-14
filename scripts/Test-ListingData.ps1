<#
.SYNOPSIS
    Release ワークフロー用の listing data 検証。ReleaseNotes が今回の版のものでなければ失敗する。

.DESCRIPTION
    Directory 内の listingData-*.csv（ちょうど 1 件であること）を検査する。
    提出前（パッケージのビルド前）に実行し、更新漏れの文が Store へ公開されるのを防ぐ。
    判定ロジックは ListingData.psm1。内容が正しいかは判定できず、空・版違いだけを検出する。

.PARAMETER Directory
    listingData-*.csv を含むディレクトリ。

.PARAMETER Version
    タグから "v" を除いた版（例: 1.9.0）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ListingData.psm1') -Force

$files = @(Get-ChildItem -LiteralPath $Directory -Filter 'listingData-*.csv' -File)
if ($files.Count -ne 1) {
    $names = if ($files.Count -eq 0) { '(none)' } else { $files.Name -join ', ' }
    throw "listingData-*.csv がちょうど 1 件ではありません（$($files.Count) 件）: $names"
}

Assert-ListingReleaseNotes -CsvPath $files[0].FullName -Version $Version
Write-Host "listing data の ReleaseNotes を確認しました（v$Version）: $($files[0].Name)"
