BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\WackGate.psm1') -Force

    function New-WackReport {
        param(
            [string]$Path,
            [string]$Overall = 'PASS',
            [string]$Partial = 'FALSE',
            [string]$Version = '1.9.0.0',
            [object[]]$Tests = @()
        )
        $testXml = ($Tests | ForEach-Object {
            "<TEST INDEX=`"$($_.Index)`" NAME=`"$($_.Name)`" OPTIONAL=`"$($_.Optional)`"><RESULT><![CDATA[$($_.Result)]]></RESULT></TEST>"
        }) -join ''
        $xml = "<?xml version=`"1.0`" encoding=`"UTF-16`"?><REPORT OVERALL_RESULT=`"$Overall`" PARTIAL_RUN=`"$Partial`" APP_VERSION=`"$Version`"><REQUIREMENTS><REQUIREMENT NUMBER=`"1`" TITLE=`"t`">$testXml</REQUIREMENT></REQUIREMENTS></REPORT>"
        Set-Content -LiteralPath $Path -Value $xml -Encoding Unicode
    }

    $script:passTest = @{ Index = 1; Name = 'Supported APIs'; Optional = 'FALSE'; Result = 'PASS' }
    $script:optionalFail = @{ Index = 88; Name = 'Blocked executables'; Optional = 'TRUE'; Result = 'FAIL' }
    $script:requiredFail = @{ Index = 12; Name = 'App manifest'; Optional = 'FALSE'; Result = 'FAIL' }
}

Describe 'Get-WackReportSummary' {
    It 'reads the report attributes and the failed tests' {
        $path = Join-Path $TestDrive 'ok.xml'
        New-WackReport -Path $path -Tests @($passTest, $optionalFail)

        $summary = Get-WackReportSummary -ReportPath $path

        $summary.OverallResult | Should -Be 'PASS'
        $summary.PartialRun | Should -Be 'FALSE'
        $summary.AppVersion | Should -Be '1.9.0.0'
        $summary.TestCount | Should -Be 2
        @($summary.FailedTests).Count | Should -Be 1
        $summary.FailedTests[0].Index | Should -Be '88'
        $summary.FailedTests[0].Optional | Should -BeTrue
    }

    It 'throws when the report does not exist' {
        { Get-WackReportSummary -ReportPath (Join-Path $TestDrive 'missing.xml') } | Should -Throw '*見つかりません*'
    }

    It 'throws when the root element is not REPORT' {
        $path = Join-Path $TestDrive 'other.xml'
        Set-Content -LiteralPath $path -Value '<OTHER />'
        { Get-WackReportSummary -ReportPath $path } | Should -Throw '*REPORT*'
    }
}

Describe 'Assert-WackGate' {
    BeforeAll {
        function Get-Summary {
            param([hashtable]$Override = @{})
            $base = @{
                OverallResult = 'PASS'
                PartialRun    = 'FALSE'
                AppVersion    = '1.9.0.0'
                TestCount     = 24
                FailedTests   = @()
            }
            foreach ($key in $Override.Keys) { $base[$key] = $Override[$key] }
            [pscustomobject]$base
        }
    }

    It 'passes when the result is PASS, the run is complete and the version matches' {
        { Assert-WackGate -Summary (Get-Summary) -ExpectedVersion '1.9.0.0' } | Should -Not -Throw
    }

    It 'allows optional test failures when OVERALL_RESULT is PASS' {
        $optional = [pscustomobject]@{ Index = '88'; Name = 'Blocked executables'; Optional = $true }
        { Assert-WackGate -Summary (Get-Summary @{ FailedTests = @($optional) }) -ExpectedVersion '1.9.0.0' } | Should -Not -Throw
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'a FAIL result';       Override = @{ OverallResult = 'FAIL' };    Expected = "*OVERALL_RESULT*'FAIL'*" }
        @{ Name = 'an empty result';     Override = @{ OverallResult = '' };        Expected = '*OVERALL_RESULT*' }
        @{ Name = 'a partial run';       Override = @{ PartialRun = 'TRUE' };       Expected = '*PARTIAL_RUN*' }
        @{ Name = 'a missing partial flag'; Override = @{ PartialRun = '' };        Expected = '*PARTIAL_RUN*' }
        @{ Name = 'a different version'; Override = @{ AppVersion = '1.8.6.0' };     Expected = '*APP_VERSION*1.8.6.0*1.9.0.0*' }
    ) {
        { Assert-WackGate -Summary (Get-Summary $Override) -ExpectedVersion '1.9.0.0' } | Should -Throw -ExpectedMessage $Expected
    }

    It 'names the required tests that failed' {
        $required = [pscustomobject]@{ Index = '12'; Name = 'App manifest'; Optional = $false }
        $summary = Get-Summary @{ OverallResult = 'FAIL'; FailedTests = @($required) }
        { Assert-WackGate -Summary $summary -ExpectedVersion '1.9.0.0' } | Should -Throw -ExpectedMessage '*`[12`] App manifest*'
    }

    It 'reports every problem at once' {
        $summary = Get-Summary @{ OverallResult = 'FAIL'; PartialRun = 'TRUE'; AppVersion = '0.0.0.0' }
        { Assert-WackGate -Summary $summary -ExpectedVersion '1.9.0.0' } |
            Should -Throw -ExpectedMessage '*OVERALL_RESULT*PARTIAL_RUN*APP_VERSION*'
    }
}

Describe 'WACK gate end to end (report file to verdict)' {
    It 'accepts a PASS report whose only failure is optional' {
        $path = Join-Path $TestDrive 'pass.xml'
        New-WackReport -Path $path -Tests @($passTest, $optionalFail)
        { Assert-WackGate -Summary (Get-WackReportSummary -ReportPath $path) -ExpectedVersion '1.9.0.0' } | Should -Not -Throw
    }

    It 'rejects a FAIL report and names the failed required test' {
        $path = Join-Path $TestDrive 'fail.xml'
        New-WackReport -Path $path -Overall 'FAIL' -Tests @($passTest, $requiredFail)
        { Assert-WackGate -Summary (Get-WackReportSummary -ReportPath $path) -ExpectedVersion '1.9.0.0' } |
            Should -Throw -ExpectedMessage '*`[12`] App manifest*'
    }
}
