<#
.SYNOPSIS
P1（#293）: 実アプリの MapControl で、表示範囲画像（方式 A）と標識の位置が一致するかを、画面のキャプチャで確認する。

.DESCRIPTION
PGE_P1_OUT で有効になる確認（PhotoGeoExplorer/Diagnostics/MapBench/P1LiveCheck.cs）を 1 回実行する。保存済みの MVT（PGE_P1_MVT）から描いた画像だけを背景にし、通信しない。
設定ファイルは退避し、計測中だけ空のフォルダーを開く設定にして、終了後に元へ戻す。画面のキャプチャのため、ウィンドウを最前面にする。他の操作はしないこと。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ExePath,
    [string]$OutDir = (Join-Path $env:TEMP 'pge-bench\p1-live'),
    [string]$MvtDir = (Join-Path $env:TEMP 'pge-bench\mvt'),
    [string]$FolderPath = (Join-Path $env:TEMP 'pge-bench\empty'),
    [switch]$LargeMap,
    [string]$MoveTo = '',
    [int]$TimeoutMinutes = 5
)

$ErrorActionPreference = 'Stop'
$settings = Join-Path $env:LOCALAPPDATA 'PhotoGeoExplorer\settings.json'
if (Get-Process PhotoGeoExplorer -ErrorAction SilentlyContinue) { throw 'PhotoGeoExplorer が起動中です。閉じてから実行してください。' }
Add-Type -Namespace Pge -Name Win -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
'@
New-Item -ItemType Directory -Force -Path $OutDir, $FolderPath | Out-Null
$backup = Join-Path $OutDir 'settings.backup.json'
Copy-Item $settings $backup -Force
$hashBefore = (Get-FileHash $settings).Hash
try {
    $text = [IO.File]::ReadAllText($settings)
    $text = [regex]::Replace($text, '"lastFolderPath"\s*:\s*"[^"]*"', ('"lastFolderPath": ' + (ConvertTo-Json $FolderPath)))
    if ($LargeMap) {
        $text = $text.Replace('"paneRegion2View": "map"', '"paneRegion2View": "preview"').Replace('"paneRegion3View": "preview"', '"paneRegion3View": "map"')
    }
    [IO.File]::WriteAllBytes($settings, (New-Object Text.UTF8Encoding($false)).GetBytes($text))
    $env:PGE_P1_OUT = $OutDir
    $env:PGE_P1_MVT = $MvtDir
    $proc = Start-Process -FilePath $ExePath -PassThru
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) {
            # -MoveTo "x,y" で、別のモニターへ移す（DPI の違うモニターでの確認用）。最前面にもする
            if ($MoveTo) { $xy = $MoveTo.Split(','); [Pge.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::new(-1), [int]$xy[0], [int]$xy[1], 0, 0, 0x0001) | Out-Null }
            [Pge.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0003) | Out-Null
        }
        if ($proc.HasExited) { break }
    }
    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) { $proc.Kill(); throw "確認が ${TimeoutMinutes} 分で終わりませんでした。" }
}
finally {
    Remove-Item Env:PGE_P1_OUT, Env:PGE_P1_MVT -ErrorAction SilentlyContinue
    Copy-Item $backup $settings -Force
    Write-Host ("settings restored: {0}" -f ($hashBefore -eq (Get-FileHash $settings).Hash))
}
