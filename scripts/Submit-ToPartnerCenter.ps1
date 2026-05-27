#Requires -Version 7.0
<#
.SYNOPSIS
    PhotoGeoExplorer を Microsoft Store に申請する。

.DESCRIPTION
    MSIX / appxupload 系 Submission API を使用する。
      ベース URL : https://manage.devcenter.microsoft.com/v1.0/my/
      認証 resource : https://manage.devcenter.microsoft.com

    MSI/EXE 向けの api.store.microsoft.com 系 (/submission/v1/product/...) とは別物。

    自動化の終点は CommitStarted 状態への commit 送信まで。
    Certification（認定依頼・公開）は Partner Center UI でユーザーが手動実施する。

.PARAMETER MsixUpload
    アップロードする .msixupload ファイルのパス。

.PARAMETER ListingDataCsv
    listing data CSV ファイルのパス。

.PARAMETER DryRun
    API を呼び出さずに入力の検証と適用内容のプレビューのみ行うモード。

.PARAMETER DeletePending
    pending submission が存在する場合に削除して続行する（既定: fail fast）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixUpload,
    [Parameter(Mandatory)][string]$ListingDataCsv,
    [switch]$DryRun,
    [switch]$DeletePending
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$API_BASE      = 'https://manage.devcenter.microsoft.com/v1.0/my'
$AUTH_RESOURCE = 'https://manage.devcenter.microsoft.com'
$AUTH_URL      = 'https://login.microsoftonline.com/{0}/oauth2/token'

$script:AccessToken = $null

# ── ログ ──────────────────────────────────────────────────────────────────────
function Write-Step([string]$msg)  { Write-Host "▶ $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)    { Write-Host "  ✔ $msg" -ForegroundColor Green }
function Write-Info([string]$msg)  { Write-Host "  ℹ $msg" }
function Write-Warn([string]$msg)  { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }

function Write-ApiLog {
    param([string]$Method, [string]$Uri, [int]$Status, [object]$ResponseHeaders = $null)
    $corr = '—'
    if ($ResponseHeaders) {
        $corr = $ResponseHeaders['MS-CV'] ?? $ResponseHeaders['x-ms-request-id'] ?? '—'
    }
    $shortUri = if ($Uri.Length -gt 100) { $Uri.Substring(0, 100) + '…' } else { $Uri }
    Write-Info "$Method $shortUri → HTTP $Status  (correlation: $corr)"
}

# ── API 呼び出しラッパー ─────────────────────────────────────────────────────
function Invoke-StoreApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body   = $null,
        [string]$InFile = $null
    )
    $uri = "$API_BASE/$Path"
    $headers = @{
        'Authorization' = "Bearer $script:AccessToken"
        'Content-Type'  = 'application/json'
    }

    $params = @{
        Method          = $Method
        Uri             = $uri
        Headers         = $headers
        UseBasicParsing = $true
    }
    if ($Body)   { $params['Body'] = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    if ($InFile) {
        $params.Remove('Body') | Out-Null
        $params['InFile']         = $InFile
        $headers['Content-Type']  = 'application/octet-stream'
    }

    try {
        $resp = Invoke-WebRequest @params
        Write-ApiLog -Method $Method -Uri $uri -Status ([int]$resp.StatusCode) -ResponseHeaders $resp.Headers
        if ($resp.Content -and $resp.Content.Length -gt 0) {
            return $resp.Content | ConvertFrom-Json -Depth 20
        }
        return $null
    }
    catch {
        $sc   = $_.Exception.Response?.StatusCode.value__ ?? '?'
        $corr = $_.Exception.Response?.Headers?['MS-CV'] ?? '—'
        $body = $null
        try { $body = $_.Exception.Response.GetResponseStream() | ForEach-Object { [System.IO.StreamReader]::new($_).ReadToEnd() } } catch {}
        Write-Error ("API エラー: $Method $uri`n" +
            "  HTTP  : $sc`n" +
            "  corr  : $corr`n" +
            "  msg   : $($_.Exception.Message)`n" +
            "  body  : $body")
        throw
    }
}

# ── 1. 認証 ──────────────────────────────────────────────────────────────────
function Get-AccessToken {
    param([string]$TenantId, [string]$ClientId, [string]$ClientSecret)
    Write-Step '認証 (Azure AD client credentials)'
    $resp = Invoke-RestMethod `
        -Method      Post `
        -Uri         ($AUTH_URL -f $TenantId) `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body        @{
            grant_type    = 'client_credentials'
            client_id     = $ClientId
            client_secret = $ClientSecret
            resource      = $AUTH_RESOURCE
        }
    Write-Ok 'アクセストークン取得完了'
    return $resp.access_token
}

# ── 2. アプリ情報取得 ─────────────────────────────────────────────────────────
function Get-Application([string]$AppId) {
    Write-Step "アプリ情報取得 (appId: $AppId)"
    $app = Invoke-StoreApi -Method GET -Path "applications/$AppId"
    Write-Ok "アプリ名: $($app.primaryName)"
    return $app
}

# ── 3. pending 申請の確認・処理 ──────────────────────────────────────────────
function Resolve-PendingSubmission {
    param([object]$App, [string]$AppId, [bool]$ShouldDelete)
    $pending = $App.pendingApplicationSubmission
    if (-not $pending) {
        Write-Ok 'pending submission なし'
        return
    }
    $pendingId = $pending.id
    if (-not $ShouldDelete) {
        throw ("pending submission が存在します (id: $pendingId)。`n" +
            "  Partner Center で手動削除するか、-DeletePending フラグを指定して再実行してください。")
    }
    Write-Warn "pending submission を削除します: $pendingId"
    Invoke-StoreApi -Method DELETE -Path "applications/$AppId/submissions/$pendingId" | Out-Null
    Write-Ok "削除完了: $pendingId"
}

# ── 4. 新規申請作成 ───────────────────────────────────────────────────────────
function New-StoreSubmission([string]$AppId) {
    Write-Step '新規申請作成 (直前の公開済み申請からクローン)'
    $sub = Invoke-StoreApi -Method POST -Path "applications/$AppId/submissions"
    Write-Ok "submission ID  : $($sub.id)"
    $shortUrl = if ($sub.fileUploadUrl.Length -gt 80) { $sub.fileUploadUrl.Substring(0,80) + '…' } else { $sub.fileUploadUrl }
    Write-Ok "fileUploadUrl  : $shortUrl"
    return $sub
}

# ── 5. listing data 更新（CSV → listings JSON）───────────────────────────────
function Update-ListingsFromCsv {
    param([object]$Submission, [string]$CsvPath)
    Write-Step 'listing data を CSV から更新'

    $rows = Import-Csv -Path $CsvPath -Encoding UTF8
    $fieldMap = @{}
    foreach ($row in $rows) {
        if (-not [string]::IsNullOrWhiteSpace($row.Field)) {
            $fieldMap[$row.Field] = $row
        }
    }

    $langCols = @{ 'en-us' = 'en-us'; 'ja-jp' = 'ja-jp' }

    foreach ($csvLang in $langCols.Keys) {
        $apiLang = $langCols[$csvLang]
        if (-not $Submission.listings.PSObject.Properties[$apiLang]) {
            Write-Warn "listings.$apiLang が存在しないためスキップ"
            continue
        }
        $base = $Submission.listings.$apiLang.baseListing

        # テキストフィールド
        $textFields = @(
            @{ csv = 'Description';      api = 'description' },
            @{ csv = 'ReleaseNotes';     api = 'releaseNotes' },
            @{ csv = 'ShortTitle';       api = 'shortTitle' },
            @{ csv = 'ShortDescription'; api = 'shortDescription' }
        )
        foreach ($tf in $textFields) {
            if ($fieldMap.ContainsKey($tf.csv)) {
                $val = ($fieldMap[$tf.csv].$csvLang ?? '').Trim()
                if ($val) { $base.($tf.api) = $val }
            }
        }

        # features 配列（Feature1〜Feature10）
        $features = [System.Collections.Generic.List[string]]::new()
        for ($i = 1; $i -le 10; $i++) {
            $key = "Feature$i"
            if ($fieldMap.ContainsKey($key)) {
                $val = ($fieldMap[$key].$csvLang ?? '').Trim()
                if ($val) { $features.Add($val) }
            }
        }
        if ($features.Count -gt 0) { $base.features = $features.ToArray() }

        # keywords 配列（SearchTerm1〜SearchTerm5）
        $keywords = [System.Collections.Generic.List[string]]::new()
        for ($i = 1; $i -le 5; $i++) {
            $key = "SearchTerm$i"
            if ($fieldMap.ContainsKey($key)) {
                $val = ($fieldMap[$key].$csvLang ?? '').Trim()
                if ($val) { $keywords.Add($val) }
            }
        }
        if ($keywords.Count -gt 0) { $base.keywords = $keywords.ToArray() }

        $descPreview = ($base.description ?? '').Replace("`n", ' ')
        $descPreview = if ($descPreview.Length -gt 50) { $descPreview.Substring(0, 50) + '…' } else { $descPreview }
        Write-Ok "${apiLang}: description=${descPreview}"
        Write-Info "  features $($features.Count) 件 / keywords $($keywords.Count) 件"
    }

    return $Submission
}

# ── 6. パッケージ更新 ────────────────────────────────────────────────────────
function Update-Packages {
    param([object]$Submission, [string]$MsixUploadPath)
    Write-Step 'applicationPackages を更新'
    $fileName = [System.IO.Path]::GetFileName($MsixUploadPath)

    # 既存パッケージを PendingDelete に
    $existing = @($Submission.applicationPackages)
    foreach ($pkg in $existing) {
        $pkg.fileStatus = 'PendingDelete'
        Write-Info "  PendingDelete: $($pkg.fileName)"
    }

    # 新規パッケージ追加（必須フィールドを既存パッケージから引き継ぐ）
    $refPkg = $existing | Select-Object -First 1
    $newPkg = [PSCustomObject]@{
        fileName              = $fileName
        fileStatus            = 'PendingUpload'
        minimumDirectXVersion = if ($refPkg -and $refPkg.minimumDirectXVersion) { $refPkg.minimumDirectXVersion } else { 'None' }
        minimumSystemRam      = if ($refPkg -and $refPkg.minimumSystemRam)      { $refPkg.minimumSystemRam }      else { 'None' }
    }
    $Submission.applicationPackages = $existing + $newPkg
    Write-Ok "PendingUpload : $fileName"
    return $Submission
}

# ── 7. 申請 PUT ───────────────────────────────────────────────────────────────
function Set-StoreSubmission {
    param([string]$AppId, [string]$SubmissionId, [object]$Submission)
    Write-Step '申請内容を PUT'
    Invoke-StoreApi -Method PUT -Path "applications/$AppId/submissions/$SubmissionId" -Body $Submission | Out-Null
    Write-Ok 'PUT 完了'
}

# ── 8. パッケージ ZIP 作成・アップロード ───────────────────────────────────────
function Upload-Package {
    param([string]$MsixUploadPath, [string]$FileUploadUrl)
    Write-Step 'パッケージを Azure Blob SAS URL にアップロード'
    $tempZip = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        [System.IO.Path]::GetRandomFileName() + '.zip'
    )
    try {
        $fileName = [System.IO.Path]::GetFileName($MsixUploadPath)
        Write-Info "ZIP 作成: $fileName → $tempZip"
        Compress-Archive -Path $MsixUploadPath -DestinationPath $tempZip -Force

        $zipSize = (Get-Item $tempZip).Length
        Write-Info "ZIP サイズ: $([Math]::Round($zipSize / 1MB, 2)) MB"

        $resp = Invoke-WebRequest `
            -Method      Put `
            -Uri         $FileUploadUrl `
            -InFile      $tempZip `
            -Headers     @{ 'x-ms-blob-type' = 'BlockBlob' } `
            -ContentType 'application/octet-stream' `
            -UseBasicParsing
        Write-ApiLog -Method PUT -Uri $FileUploadUrl -Status ([int]$resp.StatusCode) -ResponseHeaders $resp.Headers
        Write-Ok 'アップロード完了'
    }
    finally {
        if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
    }
}

# ── 9. commit ────────────────────────────────────────────────────────────────
function Submit-Commit {
    param([string]$AppId, [string]$SubmissionId)
    Write-Step '申請を commit → CommitStarted 状態へ遷移'
    $result = Invoke-StoreApi -Method POST -Path "applications/$AppId/submissions/$SubmissionId/commit"
    Write-Ok "status: $($result.status)"
    Write-Info "Partner Center: https://partner.microsoft.com/dashboard/products/$AppId/submissions/$SubmissionId"
    Write-Info '以降の認定依頼は Partner Center UI でユーザーが手動実施してください。'
}

# ── DryRun プレビュー ────────────────────────────────────────────────────────
function Show-DryRunPreview {
    param([string]$MsixUploadPath, [string]$CsvPath)
    Write-Host ''
    Write-Host '━━━  DRY RUN（API 呼び出しなし）  ━━━' -ForegroundColor Magenta

    Write-Step 'パッケージ'
    $sizeKb = [Math]::Round((Get-Item $MsixUploadPath).Length / 1KB)
    Write-Info "ファイル : $MsixUploadPath"
    Write-Info "サイズ   : $sizeKb KB"

    Write-Step 'listing data プレビュー'
    $rows = Import-Csv -Path $CsvPath -Encoding UTF8
    $fieldMap = @{}
    foreach ($row in $rows) {
        if (-not [string]::IsNullOrWhiteSpace($row.Field)) { $fieldMap[$row.Field] = $row }
    }

    foreach ($lang in @('en-us', 'ja-jp')) {
        Write-Host "  [$lang]" -ForegroundColor Yellow
        foreach ($f in @('Description', 'ReleaseNotes', 'ShortTitle', 'ShortDescription')) {
            if ($fieldMap.ContainsKey($f)) {
                $val     = ($fieldMap[$f].$lang ?? '').Trim().Replace("`n", ' ')
                $preview = if ($val.Length -gt 70) { $val.Substring(0, 70) + '…' } else { $val }
                Write-Info "  $f : $preview"
            }
        }
        $ftCount = 0
        for ($i = 1; $i -le 10; $i++) {
            if ($fieldMap.ContainsKey("Feature$i") -and ($fieldMap["Feature$i"].$lang ?? '').Trim()) { $ftCount++ }
        }
        $kwCount = 0
        for ($i = 1; $i -le 5; $i++) {
            if ($fieldMap.ContainsKey("SearchTerm$i") -and ($fieldMap["SearchTerm$i"].$lang ?? '').Trim()) { $kwCount++ }
        }
        Write-Info "  features $ftCount 件 / keywords $kwCount 件"
    }

    Write-Host ''
    Write-Host '実行時フロー:' -ForegroundColor Cyan
    Write-Host '  1. Azure AD でアクセストークン取得'
    Write-Host '  2. GET  applications/{appId} → アプリ情報確認'
    Write-Host '  3. pending 確認 → 既定 fail fast（-DeletePending で削除して続行）'
    Write-Host '  4. POST applications/{appId}/submissions → クローン作成'
    Write-Host '  5. listing data を CSV で更新（screenshots は保持）'
    Write-Host '  6. applicationPackages: 旧 PendingDelete / 新 PendingUpload'
    Write-Host '  7. PUT  applications/{appId}/submissions/{id} → 申請内容確定'
    Write-Host '  8. ZIP 作成 → PUT fileUploadUrl (Azure Blob BlockBlob)'
    Write-Host '  9. POST applications/{appId}/submissions/{id}/commit → CommitStarted'
    Write-Host ''
    Write-Host '─── DRY RUN 終了 ───' -ForegroundColor Magenta
}

# ── メイン ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '━━━  Submit-ToPartnerCenter.ps1  ━━━' -ForegroundColor Cyan
Write-Host "  MsixUpload    : $MsixUpload"
Write-Host "  ListingDataCsv: $ListingDataCsv"
Write-Host "  DryRun        : $DryRun"
Write-Host "  DeletePending : $DeletePending"
Write-Host ''

# 入力検証
if (-not (Test-Path $MsixUpload))     { throw "MsixUpload が見つかりません: $MsixUpload" }
if (-not (Test-Path $ListingDataCsv)) { throw "ListingDataCsv が見つかりません: $ListingDataCsv" }
if (-not $MsixUpload.EndsWith('.msixupload', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "MsixUpload は .msixupload ファイルである必要があります: $MsixUpload"
}

if ($DryRun) {
    Show-DryRunPreview -MsixUploadPath $MsixUpload -CsvPath $ListingDataCsv
    exit 0
}

# Secrets 確認
foreach ($key in @('MSSTORE_TENANT_ID', 'MSSTORE_CLIENT_ID', 'MSSTORE_CLIENT_SECRET', 'MSSTORE_PRODUCT_ID')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) {
        throw "環境変数 $key が設定されていません"
    }
}

$appId = $env:MSSTORE_PRODUCT_ID

$script:AccessToken = Get-AccessToken `
    -TenantId     $env:MSSTORE_TENANT_ID `
    -ClientId     $env:MSSTORE_CLIENT_ID `
    -ClientSecret $env:MSSTORE_CLIENT_SECRET

$app = Get-Application -AppId $appId

Resolve-PendingSubmission -App $app -AppId $appId -ShouldDelete $DeletePending.IsPresent

$submission    = New-StoreSubmission -AppId $appId
$fileUploadUrl = $submission.fileUploadUrl

try {
    $submission = Update-ListingsFromCsv -Submission $submission -CsvPath $ListingDataCsv
    $submission = Update-Packages        -Submission $submission -MsixUploadPath $MsixUpload

    Set-StoreSubmission -AppId $appId -SubmissionId $submission.id -Submission $submission

    Upload-Package -MsixUploadPath $MsixUpload -FileUploadUrl $fileUploadUrl

    Submit-Commit -AppId $appId -SubmissionId $submission.id
}
catch {
    Write-Error ("申請処理が失敗しました。Partner Center に pending submission (id: $($submission.id)) が残っています。`n" +
        "  復旧手順:`n" +
        "    1. Partner Center (https://partner.microsoft.com/dashboard) で pending submission を確認・削除してください。`n" +
        "    2. 問題を修正後、スクリプトを再実行してください。`n" +
        "    3. 意図して pending submission を削除して再実行する場合は -DeletePending フラグを使用してください。")
    throw
}

Write-Host ''
Write-Host '━━━  完了  ━━━' -ForegroundColor Green
Write-Host "  申請 ID : $($submission.id)"
Write-Host "  次のステップ: Partner Center で認定依頼を実施してください"
Write-Host ''
