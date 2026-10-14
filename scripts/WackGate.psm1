Set-StrictMode -Version Latest

# WACK の報告書（appcert の XML）を読み、Release を続行してよいかを判定する。
# 判定ロジックだけをここに置き、パッケージの生成・導入・appcert の実行は Invoke-WackGate.ps1 が担当する。

function Get-WackReportSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ReportPath
    )

    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        throw "WACK の報告書が見つかりません: $ReportPath"
    }

    $xml = [xml](Get-Content -LiteralPath $ReportPath -Raw)
    $report = $xml.DocumentElement
    if ($null -eq $report -or $report.Name -ne 'REPORT') {
        throw "WACK の報告書として読めません（ルート要素が REPORT ではありません）: $ReportPath"
    }

    # RESULT は CDATA を含む要素なので、プロパティ参照ではなく InnerText で読む。
    $tests = @($xml.SelectNodes('//REQUIREMENT/TEST'))
    $failed = @($tests | Where-Object { $_.SelectSingleNode('RESULT').InnerText.Trim() -match 'FAIL' } | ForEach-Object {
        [pscustomobject]@{
            Index    = $_.GetAttribute('INDEX')
            Name     = $_.GetAttribute('NAME')
            Optional = ($_.GetAttribute('OPTIONAL') -eq 'TRUE')
        }
    })

    [pscustomobject]@{
        OverallResult = $report.GetAttribute('OVERALL_RESULT')
        PartialRun    = $report.GetAttribute('PARTIAL_RUN')
        AppVersion    = $report.GetAttribute('APP_VERSION')
        TestCount     = $tests.Count
        FailedTests   = $failed
    }
}

function Assert-WackGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Summary,
        [Parameter(Mandatory)][string]$ExpectedVersion
    )

    $problems = [System.Collections.Generic.List[string]]::new()

    if ($Summary.OverallResult -ne 'PASS') {
        $problems.Add("OVERALL_RESULT が PASS ではありません: '$($Summary.OverallResult)'")
        foreach ($test in @($Summary.FailedTests | Where-Object { -not $_.Optional })) {
            $problems.Add("  必須テストの失敗: [$($test.Index)] $($test.Name)")
        }
    }
    if ($Summary.PartialRun -ne 'FALSE') {
        $problems.Add("一部のテストだけの実行です（PARTIAL_RUN='$($Summary.PartialRun)'）。")
    }
    if ($Summary.AppVersion -ne $ExpectedVersion) {
        $problems.Add("報告書の APP_VERSION '$($Summary.AppVersion)' が、マニフェストの版 '$ExpectedVersion' と一致しません。")
    }

    if ($problems.Count -gt 0) {
        throw ("WACK のゲートを通過できません。`n" + ($problems -join "`n"))
    }
}

Export-ModuleMember -Function Get-WackReportSummary, Assert-WackGate
