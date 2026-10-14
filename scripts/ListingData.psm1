Set-StrictMode -Version Latest

# Store リスティング用 CSV の ReleaseNotes が、今回のリリース版のものであることを検査する。
# 提出スクリプト（Submit-ToPartnerCenter.ps1）は空の値を読み飛ばすため、更新漏れがあると前回の文が Store に残る。

function Assert-ListingReleaseNotes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CsvPath,
        # タグから "v" を除いた版（例: 1.9.0）。
        [Parameter(Mandatory)][string]$Version,
        # 提出で実際に上書きされる言語列。
        [string[]]$Languages = @('en-us', 'ja-jp')
    )

    if (-not (Test-Path -LiteralPath $CsvPath -PathType Leaf)) {
        throw "listing data の CSV が見つかりません: $CsvPath"
    }

    # 提出スクリプトと同じ読み方にして、解釈の食い違いを避ける。
    $rows = @(Import-Csv -LiteralPath $CsvPath -Encoding UTF8)
    $notes = @($rows | Where-Object { $_.Field -eq 'ReleaseNotes' })
    if ($notes.Count -ne 1) {
        throw "CSV に Field=ReleaseNotes の行が 1 件ではありません（$($notes.Count) 件）: $CsvPath"
    }

    # v1.9.0 が v1.9.01 や v1.9.0.1 に一致しないよう、後ろに版の続きが無いことを要求する。
    $versionPattern = [regex]::Escape("v$Version") + '(?!\d|\.\d)'
    $problems = [System.Collections.Generic.List[string]]::new()

    foreach ($language in $Languages) {
        if (-not $notes[0].PSObject.Properties[$language]) {
            $problems.Add("$language の列が CSV にありません。")
            continue
        }
        $text = ([string]$notes[0].$language).Trim()
        if (-not $text) {
            $problems.Add("$language の ReleaseNotes が空です（空だと提出で上書きされず、前回の文が残ります）。")
        }
        elseif ($text -notmatch $versionPattern) {
            $problems.Add("$language の ReleaseNotes にタグの版 'v$Version' が含まれていません（前回の文のままの可能性があります）。")
        }
    }

    if ($problems.Count -gt 0) {
        throw ("listing data の ReleaseNotes を検証できません: $CsvPath`n" + ($problems -join "`n"))
    }
}

Export-ModuleMember -Function Assert-ListingReleaseNotes
