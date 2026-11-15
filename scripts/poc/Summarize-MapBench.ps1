<#
.SYNOPSIS
Invoke-MapBench.ps1 の結果（result-*.json）を集計して、Markdown の表で出す（スパイク #293 の P0）。

.DESCRIPTION
コールド（ラベルが cold で始まる）と、ウォーム（warm で始まる）を分けて集計する。
ウォームは複数回の中央値と、最小〜最大（測定ばらつき）を出す。

.EXAMPLE
pwsh -File .\scripts\poc\Summarize-MapBench.ps1 -Dir $env:TEMP\pge-bench\baseline-main
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Dir
)

$ErrorActionPreference = 'Stop'
$runs = Get-ChildItem $Dir -Filter 'result-*.json' | ForEach-Object { Get-Content $_.FullName -Raw -Encoding utf8 | ConvertFrom-Json }
foreach ($run in $runs) { if ($run.error) { throw "実行 $($run.label) が失敗しています: $($run.error)" } }
$cold = @($runs | Where-Object { $_.label -like 'cold*' })
$warm = @($runs | Where-Object { $_.label -like 'warm*' })

function Median([double[]]$values) {
    if ($values.Count -eq 0) { return [double]::NaN }
    $s = $values | Sort-Object
    $mid = [int][math]::Floor(($s.Count - 1) / 2)
    if ($s.Count % 2 -eq 1) { $s[$mid] } else { ($s[$mid] + $s[$mid + 1]) / 2 }
}

function Fmt($value) { if ([double]::IsNaN($value)) { '-' } else { '{0:N1}' -f $value } }

function Cell([object[]]$runsForPhase, [scriptblock]$selector) {
    $values = @($runsForPhase | ForEach-Object { & $selector $_ } | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    if ($values.Count -eq 0) { return '-' }
    $min = ($values | Measure-Object -Minimum).Minimum
    $max = ($values | Measure-Object -Maximum).Maximum
    if ($values.Count -eq 1) { return (Fmt $values[0]) }
    "{0} ({1}〜{2})" -f (Fmt (Median $values)), (Fmt $min), (Fmt $max)
}

$first = $runs[0]
Write-Output "## 環境"
Write-Output ""
Write-Output "- OS: $($first.os)"
Write-Output "- CPU: $($first.cpu)（論理 $($first.logicalProcessors)）"
Write-Output "- SkiaSharp: $($first.skiaSharp)、$($first.framework)"
Write-Output "- DPI（RasterizationScale）: $($first.rasterizationScale)、地図ペイン $($first.controlSizeDip -join '×') DIP、ビューポート $($first.viewportPx -join '×') px"
Write-Output "- 実行: コールド $($cold.Count) 回、ウォーム $($warm.Count) 回。ウォームは中央値（最小〜最大）"
Write-Output "- 入力: $($first.inputCadence)"
Write-Output "- 対象のコミット: $($first.commit)"
Write-Output ""

$ids = $first.phases | ForEach-Object { $_.id }

Write-Output "## 確定描画（ms）"
Write-Output ""
Write-Output "| 手順 | コールド: ネットワーク完了 | コールド: 鮮明な画像 | ウォーム: ネットワーク完了 | ウォーム: 鮮明な画像 |"
Write-Output "| --- | ---: | ---: | ---: | ---: |"
foreach ($id in $ids) {
    $kind = ($first.phases | Where-Object { $_.id -eq $id }).kind
    if ($kind -in 'pan', 'continuous-zoom') { continue }
    $c = @($cold | ForEach-Object { $_.phases | Where-Object { $_.id -eq $id } })
    $w = @($warm | ForEach-Object { $_.phases | Where-Object { $_.id -eq $id } })
    Write-Output ("| {0} | {1} | {2} | {3} | {4} |" -f $id, (Cell $c { param($p) $p.networkIdleMs }), (Cell $c { param($p) $p.timeToSharpMs }), (Cell $w { param($p) $p.networkIdleMs }), (Cell $w { param($p) $p.timeToSharpMs }))
}

Write-Output ""
Write-Output "## 入力追従（パン・連続ズーム。ms）"
Write-Output ""
Write-Output "| 手順 | 実行 | 描画間隔 p50 | 描画間隔 p95 | 描画間隔 最大 | 50ms 超の停止 | 描画時間 p50 | 描画時間 p95 | 入力→描画 p50 | 入力→描画 p95 | 操作後の確定 |"
Write-Output "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
foreach ($id in $ids) {
    $kind = ($first.phases | Where-Object { $_.id -eq $id }).kind
    if ($kind -notin 'pan', 'continuous-zoom') { continue }
    foreach ($group in @(@('コールド', $cold), @('ウォーム', $warm))) {
        $p = @($group[1] | ForEach-Object { $_.phases | Where-Object { $_.id -eq $id } })
        Write-Output ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |" -f $id, $group[0],
            (Cell $p { param($x) $x.frameIntervalMs.p50 }), (Cell $p { param($x) $x.frameIntervalMs.p95 }), (Cell $p { param($x) $x.frameIntervalMs.max }),
            (Cell $p { param($x) $x.longStallsOver50Ms }),
            (Cell $p { param($x) $x.drawDurationMs.p50 }), (Cell $p { param($x) $x.drawDurationMs.p95 }),
            (Cell $p { param($x) $x.inputToRenderMs.p50 }), (Cell $p { param($x) $x.inputToRenderMs.p95 }),
            (Cell $p { param($x) $x.settleAfterInputMs }))
    }
}

Write-Output ""
Write-Output "## メモリ（MB。各実行の最後の手順）"
Write-Output ""
Write-Output "| 実行 | ワーキングセット | GC ヒープ |"
Write-Output "| --- | ---: | ---: |"
foreach ($run in $runs) {
    $last = $run.phases[-1]
    Write-Output ("| {0} | {1} | {2} |" -f $run.label, (Fmt ([double]$last.workingSetMB)), (Fmt ([double]$last.gcHeapMB)))
}
