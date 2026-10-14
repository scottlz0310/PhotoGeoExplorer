BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\ListingData.psm1') -Force

    # 実ファイルと同じ UTF-8 BOM + CRLF で、フィールド内に改行と引用符を含む CSV を作る。
    function New-ListingCsv {
        param([string]$Path, [string]$English, [string]$Japanese, [switch]$NoReleaseNotes)
        $quote = { param($s) '"' + ($s -replace '"', '""') + '"' }
        $lines = @(
            'Field,ID,Type (種類),default,en-us,ja-jp',
            "Description,1,Text,,$(& $quote "Photo`r`nGeo `"Explorer`""),$(& $quote '説明')"
        )
        if (-not $NoReleaseNotes) {
            $lines += "ReleaseNotes,2,Text,,$(& $quote $English),$(& $quote $Japanese)"
        }
        $text = ($lines -join "`r`n") + "`r`n"
        [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($true))
    }
}

Describe 'Assert-ListingReleaseNotes' {
    BeforeEach {
        $script:csv = Join-Path $TestDrive 'listingData-test.csv'
    }

    It 'passes when both languages mention the tag version, including multi-line text' {
        New-ListingCsv -Path $csv -English "Release Notes (v1.9.0)`r`n- Fixed `"a`" bug" -Japanese "リリースノート（v1.9.0）`r`n- 修正"
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' } | Should -Not -Throw
    }

    It 'passes when the version appears after the first line' {
        New-ListingCsv -Path $csv -English "What is new`r`nv1.9.0 highlights" -Japanese "新機能`r`nv1.9.0 の主な変更"
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' } | Should -Not -Throw
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'a stale version in en-us'; English = 'Release Notes (v1.8.6)'; Japanese = 'リリースノート（v1.9.0）'; Expected = "*en-us*'v1.9.0'*" }
        @{ Name = 'a stale version in ja-jp'; English = 'Release Notes (v1.9.0)'; Japanese = 'リリースノート（v1.8.6）'; Expected = "*ja-jp*'v1.9.0'*" }
        @{ Name = 'an empty en-us'; English = ''; Japanese = 'リリースノート（v1.9.0）'; Expected = '*en-us*空*' }
        @{ Name = 'a whitespace-only ja-jp'; English = 'Release Notes (v1.9.0)'; Japanese = "  `r`n "; Expected = '*ja-jp*空*' }
        @{ Name = 'a longer patch number'; English = 'Release Notes (v1.9.01)'; Japanese = 'リリースノート（v1.9.0）'; Expected = '*en-us*' }
        @{ Name = 'a four-part version'; English = 'Release Notes (v1.9.0.1)'; Japanese = 'リリースノート（v1.9.0）'; Expected = '*en-us*' }
        @{ Name = 'a version without the v'; English = 'Release Notes (1.9.0)'; Japanese = 'リリースノート（v1.9.0）'; Expected = '*en-us*' }
    ) {
        New-ListingCsv -Path $csv -English $English -Japanese $Japanese
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' } | Should -Throw -ExpectedMessage $Expected
    }

    It 'reports both languages at once' {
        New-ListingCsv -Path $csv -English '' -Japanese 'リリースノート（v1.8.6）'
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' } | Should -Throw -ExpectedMessage '*en-us*ja-jp*'
    }

    It 'throws when the ReleaseNotes row is missing' {
        New-ListingCsv -Path $csv -English 'x' -Japanese 'x' -NoReleaseNotes
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' } | Should -Throw -ExpectedMessage '*ReleaseNotes*'
    }

    It 'throws when a language column is missing' {
        New-ListingCsv -Path $csv -English 'v1.9.0' -Japanese 'v1.9.0'
        { Assert-ListingReleaseNotes -CsvPath $csv -Version '1.9.0' -Languages @('en-us', 'fr-fr') } |
            Should -Throw -ExpectedMessage '*fr-fr*列*'
    }

    It 'throws when the file does not exist' {
        { Assert-ListingReleaseNotes -CsvPath (Join-Path $TestDrive 'missing.csv') -Version '1.9.0' } |
            Should -Throw -ExpectedMessage '*見つかりません*'
    }
}

Describe 'Test-ListingData.ps1' {
    BeforeAll {
        $script:entry = Join-Path $PSScriptRoot '..\Test-ListingData.ps1'
    }

    It 'passes for a directory with one matching CSV' {
        $dir = Join-Path $TestDrive 'one'
        New-Item -ItemType Directory -Path $dir | Out-Null
        New-ListingCsv -Path (Join-Path $dir 'listingData-a.csv') -English 'Release Notes (v1.9.0)' -Japanese 'リリースノート（v1.9.0）'
        { & $entry -Directory $dir -Version '1.9.0' } | Should -Not -Throw
    }

    It 'throws when there is not exactly one CSV (<Count>)' -ForEach @(
        @{ Count = 0 }
        @{ Count = 2 }
    ) {
        $dir = Join-Path $TestDrive "count$Count"
        New-Item -ItemType Directory -Path $dir | Out-Null
        for ($i = 0; $i -lt $Count; $i++) {
            New-ListingCsv -Path (Join-Path $dir "listingData-$i.csv") -English 'v1.9.0' -Japanese 'v1.9.0'
        }
        { & $entry -Directory $dir -Version '1.9.0' } | Should -Throw -ExpectedMessage '*1 件ではありません*'
    }

    It 'throws for the real repository CSV when the version is stale' {
        $assets = Join-Path $PSScriptRoot '..\..\PhotoGeoExplorer\Assets\propose'
        { & $entry -Directory $assets -Version '0.0.1' } | Should -Throw -ExpectedMessage '*v0.0.1*'
    }
}
