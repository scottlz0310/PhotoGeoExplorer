<#
.SYNOPSIS
    公開サイト（Cloudflare Pages）が main の docs と同じ内容か検査する。一致しなければ失敗する。

.DESCRIPTION
    プライバシーポリシーとヘルプなど、docs の配信対象を公開 URL から取得して比較する（読み取りのみ）。
    デプロイは行わない。判定ロジックは PublishedDocs.psm1。

.PARAMETER BaseUrl
    公開サイトのルート URL。

.PARAMETER DocsRoot
    比較元の docs ディレクトリ。

.PARAMETER TimeoutSeconds
    一致するまで再試行する時間（秒）。push 直後のデプロイ待ちに使う。0 なら 1 回だけ検査する。

.PARAMETER IntervalSeconds
    再試行の間隔（秒）。
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://photogeoexplorer.pages.dev',
    [string]$DocsRoot = (Join-Path $PSScriptRoot '..\docs'),
    [int]$TimeoutSeconds = 0,
    [int]$IntervalSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'PublishedDocs.psm1') -Force

$fetch = {
    param($url)
    (Invoke-WebRequest -Uri $url -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30).Content
}

$attempt = Assert-PublishedDocs -BaseUrl $BaseUrl -DocsRoot $DocsRoot -Fetch $fetch `
    -TimeoutSeconds $TimeoutSeconds -IntervalSeconds $IntervalSeconds
Write-Host "公開サイトは main の docs と一致しています（$attempt 回目の確認）: $BaseUrl"
