Set-StrictMode -Version Latest

# 公開サイト（Cloudflare Pages）が main の docs と同じ内容かを検査する。
# Cloudflare Pages の Git 連携は GitHub の外で動くため、push の通知が届かなくなってもチェックが赤くならず、
# 古い内容が公開され続ける（2026-02-27 から 8 か月間気付かなかった）。その検知のための、読み取りだけの監視。

# 公開 URL と、docs 内の配信元ファイルの対応（docs/GitHubPagesSetup.md の「配信対象」と同じ）。
function Get-PublishedDocsTargets {
    [CmdletBinding()]
    param()

    @(
        [pscustomobject]@{ UrlPath = '/'; File = 'index.html' }
        [pscustomobject]@{ UrlPath = '/privacy-policy'; File = 'privacy-policy/index.html' }
        [pscustomobject]@{ UrlPath = '/help/index.html'; File = 'help/index.html' }
        [pscustomobject]@{ UrlPath = '/help/index.en.html'; File = 'help/index.en.html' }
    )
}

# 改行コード・BOM・末尾の空白の違いは、配信の内容の違いではないので無視する。
function ConvertTo-ComparableContent {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        return ''
    }

    ($Text.TrimStart([char]0xFEFF) -replace "`r`n", "`n").TrimEnd()
}

function Get-FirstDifference {
    param([string]$Expected, [string]$Actual)

    $expectedLines = $Expected -split "`n"
    $actualLines = $Actual -split "`n"
    $count = [Math]::Max($expectedLines.Count, $actualLines.Count)
    for ($i = 0; $i -lt $count; $i++) {
        $e = if ($i -lt $expectedLines.Count) { $expectedLines[$i] } else { '(行なし)' }
        $a = if ($i -lt $actualLines.Count) { $actualLines[$i] } else { '(行なし)' }
        if ($e -cne $a) {
            $clip = { param($s) if ($s.Length -gt 80) { $s.Substring(0, 80) + '...' } else { $s } }
            return "$($i + 1) 行目 期待: [$(& $clip $e.Trim())] 公開: [$(& $clip $a.Trim())]"
        }
    }
}

# 一致しない対象を返す（すべて一致なら何も返さない）。Fetch は URL を受け取り、本文を返す（テストで差し替える）。
function Find-PublishedDocsMismatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$BaseUrl,
        [Parameter(Mandatory)][string]$DocsRoot,
        [Parameter(Mandatory)][scriptblock]$Fetch
    )

    foreach ($target in Get-PublishedDocsTargets) {
        $path = Join-Path $DocsRoot $target.File
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "配信元のファイルが見つかりません: $path"
        }

        $expected = ConvertTo-ComparableContent ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $path).Path))
        $url = $BaseUrl.TrimEnd('/') + $target.UrlPath
        try {
            $actual = ConvertTo-ComparableContent (& $Fetch $url)
        }
        catch {
            [pscustomobject]@{ UrlPath = $target.UrlPath; Reason = "取得に失敗しました: $($_.Exception.Message)" }
            continue
        }

        if ($actual -cne $expected) {
            [pscustomobject]@{ UrlPath = $target.UrlPath; Reason = "内容が docs と一致しません（$(Get-FirstDifference -Expected $expected -Actual $actual)）" }
        }
    }
}

# 一致するまで IntervalSeconds おきに再試行し、TimeoutSeconds を過ぎても一致しなければ失敗する。
# push の直後はデプロイ中の可能性があるため待つ。0 を指定すると 1 回だけ検査する。
function Assert-PublishedDocs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$BaseUrl,
        [Parameter(Mandatory)][string]$DocsRoot,
        [Parameter(Mandatory)][scriptblock]$Fetch,
        [ValidateRange(0, 3600)][int]$TimeoutSeconds = 0,
        [ValidateRange(1, 600)][int]$IntervalSeconds = 60,
        [scriptblock]$Sleep = { param($seconds) Start-Sleep -Seconds $seconds }
    )

    $attempts = [Math]::Floor($TimeoutSeconds / $IntervalSeconds) + 1
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        $mismatches = @(Find-PublishedDocsMismatch -BaseUrl $BaseUrl -DocsRoot $DocsRoot -Fetch $Fetch)
        if ($mismatches.Count -eq 0) {
            return $attempt
        }

        if ($attempt -lt $attempts) {
            & $Sleep $IntervalSeconds
        }
    }

    $lines = $mismatches | ForEach-Object { "- $($_.UrlPath): $($_.Reason)" }
    throw "公開サイトが main の docs と一致しません（$attempts 回確認）:`n$($lines -join "`n")"
}

Export-ModuleMember -Function Get-PublishedDocsTargets, ConvertTo-ComparableContent, Find-PublishedDocsMismatch, Assert-PublishedDocs
