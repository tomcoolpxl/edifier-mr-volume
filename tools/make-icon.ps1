<#
.SYNOPSIS
    Draws the app icon (a volume knob) and writes src/Mr45Volume/Assets/app.ico
    with PNG-compressed images from 16 to 256 px. Rerun after changing the design.
#>
[CmdletBinding()]
param(
    [string]$OutFile = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Mr45Volume\Assets\app.ico'),
    [string]$PreviewFile
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-KnobBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    $rim = [Math]::Max(1.0, $size * 0.07)
    $inset = $size * 0.04 + $rim / 2
    $rect = New-Object System.Drawing.RectangleF $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)

    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 70, 70, 74)), ([System.Drawing.Color]::FromArgb(255, 24, 24, 26)), 60.0
    $g.FillEllipse($body, $rect)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 120, 120, 126)), $rim
    $g.DrawEllipse($pen, $rect)

    # Indicator pointing to about 2 o'clock, in a bright green.
    $center = $size / 2.0
    $angle = -40.0 * [Math]::PI / 180.0
    $r1 = $size * 0.10
    $r2 = $size * 0.36
    $lineWidth = [Math]::Max(1.6, $size * 0.13)
    $green = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 34, 197, 94)), $lineWidth
    $green.StartCap = 'Round'
    $green.EndCap = 'Round'
    $g.DrawLine($green,
        [float]($center + $r1 * [Math]::Cos($angle)), [float]($center + $r1 * [Math]::Sin($angle)),
        [float]($center + $r2 * [Math]::Cos($angle)), [float]($center + $r2 * [Math]::Sin($angle)))

    $green.Dispose(); $pen.Dispose(); $body.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-KnobBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container: header, one directory entry per image, then the PNG payloads.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Host "Wrote $OutFile ($($out.Length) bytes, sizes $($sizes -join ', '))"

if ($PreviewFile) {
    $sheet = New-Object System.Drawing.Bitmap 520, 280
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
    $x = 10
    foreach ($s in 16, 24, 32, 48) {
        $b = New-KnobBitmap $s; $g.DrawImage($b, $x, 10, $s, $s); $b.Dispose(); $x += $s + 14
    }
    $b = New-KnobBitmap 256; $g.DrawImage($b, 250, 10, 256, 256); $b.Dispose()
    $g.InterpolationMode = 'NearestNeighbor'
    $b = New-KnobBitmap 16; $g.DrawImage($b, 10, 80, 128, 128); $b.Dispose()
    $g.Dispose(); $sheet.Save($PreviewFile); $sheet.Dispose()
}
