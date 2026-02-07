[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-Text {
    param(
        [AllowNull()]
        [string]$Text
    )

    if ($null -eq $Text) {
        return "`n"
    }

    $normalized = $Text -replace "`r`n", "`n"
    $normalized = $normalized.TrimEnd("`n")
    return "$normalized`n"
}

function Get-ToolSpecificContent {
    param(
        [AllowNull()]
        [string]$ExistingContent,
        [string]$DefaultContent
    )

    if ([string]::IsNullOrWhiteSpace($ExistingContent)) {
        return $DefaultContent
    }

    $pattern = '(?s)<!-- BEGIN TOOL-SPECIFIC -->\r?\n(.*?)\r?\n<!-- END TOOL-SPECIFIC -->'
    $match = [regex]::Match($ExistingContent, $pattern)
    if (-not $match.Success) {
        return $DefaultContent
    }

    $content = $match.Groups[1].Value.TrimEnd("`r", "`n")
    if ([string]::IsNullOrWhiteSpace($content)) {
        return $DefaultContent
    }

    return $content
}

function New-TargetContent {
    param(
        [hashtable]$Target,
        [string]$SharedContent,
        [string]$ToolSpecificContent
    )

    $template = @'
# __HEADING__

> [!IMPORTANT]
> このファイルは `AgentGuidelineSource.md` を正本として `scripts/Sync-AgentDocs.ps1` で自動生成されています。
> 共通ルールの変更は `AgentGuidelineSource.md` を編集し、`pwsh ./scripts/Sync-AgentDocs.ps1` を実行してください。
> 手編集は `<!-- BEGIN TOOL-SPECIFIC -->` と `<!-- END TOOL-SPECIFIC -->` の間だけ許可します。

## 対象
- __AUDIENCE__

## 共通ガイドライン（自動生成）
<!-- BEGIN SHARED -->
__SHARED__
<!-- END SHARED -->

## 固有補足（手編集可）
<!-- BEGIN TOOL-SPECIFIC -->
__TOOL_SPECIFIC__
<!-- END TOOL-SPECIFIC -->
'@

    return $template.
        Replace('__HEADING__', $Target.Heading).
        Replace('__AUDIENCE__', $Target.Audience).
        Replace('__SHARED__', $SharedContent).
        Replace('__TOOL_SPECIFIC__', $ToolSpecificContent)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourcePath = Join-Path $repoRoot 'AgentGuidelineSource.md'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source file not found: $sourcePath"
}

$sharedContent = (Get-Content -LiteralPath $sourcePath -Raw).TrimEnd("`r", "`n")

$targets = @(
    @{
        Path = 'AGENTS.md'
        Heading = 'AGENTS.md'
        Audience = 'Codex CLI / Cline などの汎用 AI エージェント'
        DefaultToolSpecificContent = '- 固有指示が必要な場合のみ、このブロックに追記してください。'
    },
    @{
        Path = 'CLAUDE.md'
        Heading = 'CLAUDE.md'
        Audience = 'Claude Code'
        DefaultToolSpecificContent = '- Claude Code 固有の追加指示がある場合、このブロックに追記してください。'
    },
    @{
        Path = '.github/copilot-instructions.md'
        Heading = 'GitHub Copilot Instructions for PhotoGeoExplorer'
        Audience = 'GitHub Copilot Coding Agent'
        DefaultToolSpecificContent = '- GitHub Copilot 固有の追加指示がある場合、このブロックに追記してください。'
    }
)

$mismatched = New-Object System.Collections.Generic.List[string]
$updated = New-Object System.Collections.Generic.List[string]

foreach ($target in $targets) {
    $targetPath = Join-Path $repoRoot $target.Path
    $existingContent = if (Test-Path -LiteralPath $targetPath) {
        Get-Content -LiteralPath $targetPath -Raw
    } else {
        ''
    }

    $toolSpecificContent = Get-ToolSpecificContent -ExistingContent $existingContent -DefaultContent $target.DefaultToolSpecificContent
    $generatedContent = New-TargetContent -Target $target -SharedContent $sharedContent -ToolSpecificContent $toolSpecificContent

    $normalizedExisting = Normalize-Text -Text $existingContent
    $normalizedGenerated = Normalize-Text -Text $generatedContent

    if ($Check) {
        if ($normalizedExisting -ne $normalizedGenerated) {
            $mismatched.Add($target.Path)
        }
        continue
    }

    if ($normalizedExisting -ne $normalizedGenerated) {
        $directory = Split-Path -Parent $targetPath
        if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        Set-Content -LiteralPath $targetPath -Value $normalizedGenerated -Encoding utf8NoBOM -NoNewline
        $updated.Add($target.Path)
    }
}

if ($Check) {
    if ($mismatched.Count -gt 0) {
        Write-Host 'Agent guideline files are out of sync:' -ForegroundColor Red
        foreach ($path in $mismatched) {
            Write-Host "  - $path" -ForegroundColor Red
        }
        Write-Host 'Run: pwsh ./scripts/Sync-AgentDocs.ps1' -ForegroundColor Yellow
        exit 1
    }

    Write-Host 'Agent guideline files are in sync.' -ForegroundColor Green
    exit 0
}

if ($updated.Count -eq 0) {
    Write-Host 'No changes. Agent guideline files are already synchronized.' -ForegroundColor Green
    exit 0
}

Write-Host 'Updated agent guideline files:' -ForegroundColor Green
foreach ($path in $updated) {
    Write-Host "  - $path" -ForegroundColor Green
}
