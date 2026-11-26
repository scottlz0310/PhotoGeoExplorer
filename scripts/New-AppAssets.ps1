<#
.SYNOPSIS
原画から、アプリアイコン・タイル・起動画面の画像を作り直す。

.DESCRIPTION
原画は assets-source/ に置く。
- app-icon.png: 正方形（1240px 以上）。角丸の外側は透明
- splash.png:   横長（620:300。1240×600 以上）

出力は PhotoGeoExplorer/Assets/。表示倍率ごと（scale-*）と、タスクバーなどの固定の大きさ（targetsize-*）の
画像も作る。画像を差し替えるときは原画を置き換えてこのスクリプトを実行し、出力をコミットする。

System.Drawing を使うため、Windows で実行する。
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourceDirectory = Join-Path $RepositoryRoot 'assets-source'
$assetsDirectory = Join-Path $RepositoryRoot 'PhotoGeoExplorer/Assets'

# 表示倍率 100% の大きさは、倍率の指定がないファイル名にする。
# 大きい画像は、パッケージが大きくならないように倍率を絞る（400% の起動画面は原画より大きく、作れない）
$smallScales = 100, 125, 150, 200, 400
$mediumScales = 100, 125, 150, 200
# Windows 11 のスタートに出ない大きいタイル
$largeTileScales = 100, 200
$targetSizes = 16, 20, 24, 32, 40, 44, 48, 64, 256
$iconSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Get-ScaledLength([int]$Length, [int]$Scale) {
    [int][Math]::Round($Length * $Scale / 100.0, [MidpointRounding]::AwayFromZero)
}

function New-ResizedBitmap([System.Drawing.Bitmap]$Source, [int]$Width, [int]$Height) {
    if ($Width -gt $Source.Width -or $Height -gt $Source.Height) {
        throw "原画（$($Source.Width)x$($Source.Height)）より大きい画像は作りません: ${Width}x${Height}"
    }

    # 乗算済みアルファで縮小し、透明な部分との境目に色の縁が出ないようにする
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $destination = [System.Drawing.Rectangle]::new(0, 0, $Width, $Height)
        $graphics.DrawImage($Source, $destination, 0, 0, $Source.Width, $Source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
    }
    finally {
        $attributes.Dispose()
        $graphics.Dispose()
    }

    $bitmap
}

function Get-PngBytes([System.Drawing.Bitmap]$Source, [int]$Width, [int]$Height) {
    $bitmap = New-ResizedBitmap $Source $Width $Height
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

function Save-Png([System.Drawing.Bitmap]$Source, [int]$Width, [int]$Height, [string]$Name) {
    [System.IO.File]::WriteAllBytes((Join-Path $assetsDirectory $Name), (Get-PngBytes $Source $Width $Height))
}

function Save-ScaledSet([System.Drawing.Bitmap]$Source, [string]$BaseName, [int]$Width, [int]$Height, [int[]]$Scales) {
    foreach ($scale in $Scales) {
        $suffix = if ($scale -eq 100) { '' } else { ".scale-$scale" }
        Save-Png $Source (Get-ScaledLength $Width $scale) (Get-ScaledLength $Height $scale) "$BaseName$suffix.png"
    }
}

# 各大きさの PNG を並べた ICO を書き出す
function Save-Icon([System.Drawing.Bitmap]$Source, [int[]]$Sizes, [string]$Name) {
    $images = foreach ($size in $Sizes) { , (Get-PngBytes $Source $size $size) }
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)
        $offset = 6 + (16 * $Sizes.Count)
        for ($index = 0; $index -lt $Sizes.Count; $index++) {
            # 256 は 0 と書く
            $dimension = [byte]($Sizes[$index] % 256)
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }

        foreach ($image in $images) {
            $writer.Write([byte[]]$image)
        }

        $writer.Flush()
        [System.IO.File]::WriteAllBytes((Join-Path $assetsDirectory $Name), $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$icon = [System.Drawing.Bitmap]::new((Join-Path $sourceDirectory 'app-icon.png'))
$splash = [System.Drawing.Bitmap]::new((Join-Path $sourceDirectory 'splash.png'))
try {
    Save-ScaledSet $icon 'Square44x44Logo' 44 44 $smallScales
    Save-ScaledSet $icon 'StoreLogo' 50 50 $smallScales
    Save-ScaledSet $icon 'Square71x71Logo' 71 71 $mediumScales
    Save-ScaledSet $icon 'Square150x150Logo' 150 150 $mediumScales
    Save-ScaledSet $icon 'Square310x310Logo' 310 310 $largeTileScales

    # タスクバーなどは targetsize の画像を使う。unplated は背景の板を付けない表示（暗いテーマ用と明るいテーマ用）
    foreach ($size in $targetSizes) {
        foreach ($variant in '', '_altform-unplated', '_altform-lightunplated') {
            Save-Png $icon $size $size "Square44x44Logo.targetsize-$size$variant.png"
        }
    }

    Save-ScaledSet $splash 'Wide310x150Logo' 310 150 $largeTileScales
    Save-ScaledSet $splash 'SplashScreen' 620 300 $mediumScales

    Save-Icon $icon $iconSizes 'icon.ico'
}
finally {
    $icon.Dispose()
    $splash.Dispose()
}

Write-Host "画像を作り直しました: $assetsDirectory"
