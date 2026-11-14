BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\PublishedDocs.psm1') -Force

    # 実際の docs と同じ構成で、各配信対象に固有の本文を持つ docs ディレクトリを作る。
    function New-DocsRoot {
        param([string]$Root)
        foreach ($target in Get-PublishedDocsTargets) {
            $path = Join-Path $Root $target.File
            New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
            [IO.File]::WriteAllText($path, "<html>$($target.UrlPath)</html>`n")
        }
    }

    # URL のパス部分から本文を返す Fetch。Overrides で一部を差し替える。
    function New-Fetch {
        param([hashtable]$Overrides = @{})
        $map = $Overrides
        { param($url)
            $path = ([uri]$url).AbsolutePath
            if ($map.ContainsKey($path)) {
                $value = $map[$path]
                if ($value -is [scriptblock]) { return (& $value) }
                return $value
            }
            "<html>$path</html>`n"
        }.GetNewClosure()
    }
}

Describe 'ConvertTo-ComparableContent' {
    It 'ignores line endings, BOM and trailing whitespace' -ForEach @(
        @{ Name = 'CRLF'; Text = "a`r`nb`r`n"; Expected = "a`nb" }
        @{ Name = 'BOM'; Text = "$([char]0xFEFF)a`n"; Expected = 'a' }
        @{ Name = 'trailing blank lines'; Text = "a`n`n  `n"; Expected = 'a' }
        @{ Name = 'null'; Text = $null; Expected = '' }
    ) {
        ConvertTo-ComparableContent -Text $Text | Should -BeExactly $Expected
    }

    It 'keeps case and inner whitespace' {
        ConvertTo-ComparableContent -Text "A  b`n" | Should -BeExactly 'A  b'
    }
}

Describe 'Get-PublishedDocsTargets' {
    It 'covers the pages the app and the Store listing link to' {
        $paths = (Get-PublishedDocsTargets).UrlPath
        $paths | Should -Contain '/privacy-policy'
        $paths | Should -Contain '/help/index.html'
        $paths | Should -Contain '/help/index.en.html'
    }
}

Describe 'Find-PublishedDocsMismatch' {
    BeforeEach {
        $script:docs = Join-Path $TestDrive 'docs'
        New-DocsRoot -Root $docs
    }

    It 'returns nothing when every page matches' {
        @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch (New-Fetch)) | Should -BeNullOrEmpty
    }

    It 'treats CRLF in the published page as equal' {
        $fetch = New-Fetch -Overrides @{ '/privacy-policy' = "<html>/privacy-policy</html>`r`n" }
        @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test/' -DocsRoot $docs -Fetch $fetch) | Should -BeNullOrEmpty
    }

    It 'reports the page whose content differs, with the first differing line' {
        $fetch = New-Fetch -Overrides @{ '/help/index.html' = "<html>stale</html>`n" }
        $result = @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch)
        $result.Count | Should -Be 1
        $result[0].UrlPath | Should -Be '/help/index.html'
        $result[0].Reason | Should -BeLike '*1 行目*stale*'
    }

    It 'compares case-sensitively' {
        $fetch = New-Fetch -Overrides @{ '/' = '<HTML>/</HTML>' }
        @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch).UrlPath | Should -Be '/'
    }

    It 'reports a page that is shorter than the source' {
        $path = Join-Path $docs 'help/index.en.html'
        [IO.File]::WriteAllText($path, "line1`nline2`n")
        $fetch = New-Fetch -Overrides @{ '/help/index.en.html' = "line1`n" }
        $result = @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch)
        $result[0].Reason | Should -BeLike '*2 行目*(行なし)*'
    }

    It 'reports a fetch failure for that page and keeps checking the others' {
        $fetch = New-Fetch -Overrides @{
            '/privacy-policy' = { throw 'HTTP 404' }
            '/help/index.html' = "<html>stale</html>`n"
        }
        $result = @(Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch)
        $result.UrlPath | Should -Be @('/privacy-policy', '/help/index.html')
        $result[0].Reason | Should -BeLike '*取得に失敗*404*'
    }

    It 'throws when a source file is missing, which is a configuration error' {
        Remove-Item (Join-Path $docs 'help/index.html')
        { Find-PublishedDocsMismatch -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch (New-Fetch) } |
            Should -Throw '*配信元のファイルが見つかりません*'
    }
}

Describe 'Assert-PublishedDocs' {
    BeforeEach {
        $script:docs = Join-Path $TestDrive 'docs'
        New-DocsRoot -Root $docs
        $script:sleeps = [System.Collections.Generic.List[int]]::new()
        $script:sleep = { param($seconds) $script:sleeps.Add($seconds) }
    }

    It 'passes on the first attempt without waiting' {
        Assert-PublishedDocs -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch (New-Fetch) -TimeoutSeconds 600 -Sleep $sleep | Should -Be 1
        $sleeps.Count | Should -Be 0
    }

    It 'checks only once when the timeout is 0' {
        $fetch = New-Fetch -Overrides @{ '/' = 'stale' }
        { Assert-PublishedDocs -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch -Sleep $sleep } |
            Should -Throw '*1 回確認*'
        $sleeps.Count | Should -Be 0
    }

    It 'waits for a deployment that finishes within the timeout' {
        $script:calls = 0
        $fetch = New-Fetch -Overrides @{ '/privacy-policy' = { $script:calls++; if ($script:calls -lt 3) { 'stale' } else { '<html>/privacy-policy</html>' } } }
        Assert-PublishedDocs -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch -TimeoutSeconds 600 -IntervalSeconds 60 -Sleep $sleep | Should -Be 3
        $sleeps.ToArray() | Should -Be @(60, 60)
    }

    It 'fails after the timeout and lists every stale page' {
        $fetch = New-Fetch -Overrides @{ '/privacy-policy' = 'stale'; '/help/index.html' = 'stale' }
        { Assert-PublishedDocs -BaseUrl 'https://example.test' -DocsRoot $docs -Fetch $fetch -TimeoutSeconds 120 -IntervalSeconds 60 -Sleep $sleep } |
            Should -Throw '*3 回確認*/privacy-policy*/help/index.html*'
        $sleeps.ToArray() | Should -Be @(60, 60)
    }
}
