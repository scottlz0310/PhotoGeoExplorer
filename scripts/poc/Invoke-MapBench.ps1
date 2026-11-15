<#
.SYNOPSIS
地図ペインの計測（スパイク #293 の P0）を 1 回実行する。

.DESCRIPTION
計測用の入口を持つビルド（PGE_BENCH_OUT で有効になる）を起動し、決まった手順を実行させて、結果の JSON と基準画像を OutDir に出す。
- 設定ファイル（settings.json）は実行前に退避し、終了後に必ず元へ戻す（アプリはウィンドウを閉じるときに設定を保存するため）。
- -CleanCache を付けるとタイルのキャッシュを空にして始める（初回 = コールド）。付けなければ既存のキャッシュで始める（ウォーム）。
- -Record を付けると、ウィンドウの領域を ffmpeg（gdigrab のデスクトップ指定）で録画する。
  WinUI のウィンドウは gdigrab の title 指定では黒くなるため、領域指定で録り、録画中は最前面にして重なりを避ける。

.EXAMPLE
pwsh -File .\scripts\poc\Invoke-MapBench.ps1 -Label cold-1 -ExePath <PhotoGeoExplorer.exe> -CleanCache -Record
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Label,
    [Parameter(Mandatory = $true)] [string]$ExePath,
    [string]$OutDir = (Join-Path $env:TEMP 'pge-bench\out'),
    [string]$CacheDir = (Join-Path $env:TEMP 'pge-bench\cache'),
    [string]$Commit = '',
    [string]$FolderPath = (Join-Path $env:TEMP 'pge-bench\empty'),
    [switch]$CleanCache,
    [switch]$Record,
    [switch]$LargeMap,
    [int]$TimeoutMinutes = 20
)

$ErrorActionPreference = 'Stop'
$settings = Join-Path $env:LOCALAPPDATA 'PhotoGeoExplorer\settings.json'
if (Get-Process PhotoGeoExplorer -ErrorAction SilentlyContinue) { throw 'PhotoGeoExplorer が起動中です。閉じてから実行してください。' }
if (-not (Test-Path $ExePath)) { throw "ExePath が見つかりません: $ExePath" }

if ($Record) {
    Add-Type -Namespace Pge -Name Win -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if ($CleanCache -and (Test-Path $CacheDir)) { Remove-Item -Recurse -Force $CacheDir }
New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null

$backup = Join-Path $OutDir "settings.backup.$Label.json"
Copy-Item $settings $backup -Force
$hashBefore = (Get-FileHash $settings).Hash
# 映像・結果に実データのフォルダー名が出ないよう、計測中だけ空のフォルダーを開く（終了後に退避から復元する）
New-Item -ItemType Directory -Force -Path $FolderPath | Out-Null
$text = [IO.File]::ReadAllText($settings)
$text = [regex]::Replace($text, '"lastFolderPath"\s*:\s*"[^"]*"', ('"lastFolderPath": ' + (ConvertTo-Json $FolderPath)))
if ($LargeMap) {
    # 地図を右の大きな領域へ、プレビューを左下へ入れ替える（設定は終了後に復元する）
    $text = $text.Replace('"paneRegion2View": "map"', '"paneRegion2View": "preview"').Replace('"paneRegion3View": "preview"', '"paneRegion3View": "map"')
}
[IO.File]::WriteAllBytes($settings, (New-Object Text.UTF8Encoding($false)).GetBytes($text))
$ffmpeg = $null
try {
    $env:PGE_BENCH_OUT = $OutDir
    $env:PGE_BENCH_LABEL = $Label
    $env:PGE_BENCH_CACHE_DIR = $CacheDir
    $env:PGE_BENCH_COMMIT = $Commit
    $env:PGE_BENCH_WAIT_FLAG = '1'
    Remove-Item (Join-Path $OutDir "start-$Label.flag") -ErrorAction SilentlyContinue
    $proc = Start-Process -FilePath $ExePath -PassThru
    if ($Record) {
        for ($i = 0; $i -lt 40 -and -not $proc.MainWindowHandle; $i++) { Start-Sleep -Milliseconds 250; $proc.Refresh() }
        [Pge.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0003) | Out-Null
        # ウィンドウの寸法が落ち着く（1.5 秒変わらない）まで待つ
        $rect = New-Object Pge.Win+RECT
        $last = ''; $stableSince = [Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt 120; $i++) {
            $proc.Refresh()
            [Pge.Win]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
            $now = "$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)"
            # 起動直後は小さな仮のウィンドウ（約 620x300）が先に出るため、本来の寸法（800x500 以上）になるまで待つ
            $big = ($rect.Right - $rect.Left) -ge 800 -and ($rect.Bottom - $rect.Top) -ge 500
            if ($now -ne $last -or -not $big) { $last = $now; $stableSince.Restart() } elseif ($stableSince.ElapsedMilliseconds -ge 1500) { break }
            Start-Sleep -Milliseconds 100
        }
        [Pge.Win]::SetWindowPos($proc.MainWindowHandle, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0003) | Out-Null
        Start-Sleep -Milliseconds 300
        $w = ($rect.Right - $rect.Left) -band -2
        $h = ($rect.Bottom - $rect.Top) -band -2
        $video = Join-Path $OutDir "recording-$Label.mp4"
        $psi = [Diagnostics.ProcessStartInfo]::new((Get-Command ffmpeg).Source)
        $ffArgs = @('-y', '-f', 'gdigrab', '-framerate', '60', '-offset_x', "$($rect.Left)", '-offset_y', "$($rect.Top)", '-video_size', "${w}x${h}", '-i', 'desktop', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '23', '-pix_fmt', 'yuv420p', $video)
        foreach ($arg in $ffArgs) { $psi.ArgumentList.Add($arg) }
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $ffmpeg = [Diagnostics.Process]::Start($psi)
        $null = $ffmpeg.StandardError.ReadToEndAsync()
        Start-Sleep -Milliseconds 1000
    }
    New-Item -ItemType File -Force -Path (Join-Path $OutDir "start-$Label.flag") | Out-Null
    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $proc.Kill()
        throw "計測が ${TimeoutMinutes} 分で終わりませんでした。"
    }
}
finally {
    foreach ($name in 'PGE_BENCH_OUT', 'PGE_BENCH_LABEL', 'PGE_BENCH_CACHE_DIR', 'PGE_BENCH_COMMIT', 'PGE_BENCH_WAIT_FLAG') { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    if ($ffmpeg -and -not $ffmpeg.HasExited) {
        $ffmpeg.StandardInput.Write('q')
        $ffmpeg.StandardInput.Flush()
        if (-not $ffmpeg.WaitForExit(15000)) { $ffmpeg.Kill() }
    }
    Copy-Item $backup $settings -Force
    $hashAfter = (Get-FileHash $settings).Hash
    Write-Host ("settings restored: {0}" -f ($hashBefore -eq $hashAfter))
}

$result = Join-Path $OutDir "result-$Label.json"
if (-not (Test-Path $result)) { throw "結果ファイルがありません: $result" }
Write-Host "result: $result"
