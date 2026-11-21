<#
.SYNOPSIS
    Builds src/EdifierMrVolume/Assets/app.ico (and the SVG the app shows in its About row)
    from the SVG masters in assets/icon.

.DESCRIPTION
    The design lives in assets/icon/app-icon.svg (32 px and up) and app-icon-small.svg
    (16-24 px). Microsoft Edge, which every Windows 10/11 has, renders each master once at
    256 px; the sizes are scaled from that and packed as PNG images into one .ico.
    Rerun after changing an SVG.
#>
[CmdletBinding()]
param(
    [string]$PreviewFile
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path $PSScriptRoot -Parent
$iconDir = Join-Path $repo 'assets\icon'
$assets = Join-Path $repo 'src\EdifierMrVolume\Assets'
$outIco = Join-Path $assets 'app.ico'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is needed to render the SVG icon.' }

$work = Join-Path ([IO.Path]::GetTempPath()) "emv-icon-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

# Renders an SVG at 256x256 with a transparent background.
function Render-Svg([string]$svg, [string]$png) {
    $uri = ([Uri](Resolve-Path $svg).Path).AbsoluteUri
    $html = Join-Path $work 'page.html'
    "<!doctype html><html><body style='margin:0;background:transparent'><img src='$uri' width='256' height='256' style='display:block'></body></html>" |
        Set-Content -Path $html -Encoding utf8
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --default-background-color=00000000 --window-size=256,256 `
        "--screenshot=$png" ([Uri]$html).AbsoluteUri 2>$null | Out-Null
    $deadline = (Get-Date).AddSeconds(20)
    while (-not (Test-Path $png) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $png)) { throw "Edge did not render $svg." }
}

function Scale([System.Drawing.Image]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

$bigPng = Join-Path $work 'big.png'
$smallPng = Join-Path $work 'small.png'
Render-Svg (Join-Path $iconDir 'app-icon.svg') $bigPng
Render-Svg (Join-Path $iconDir 'app-icon-small.svg') $smallPng
$big = [System.Drawing.Image]::FromFile($bigPng)
$small = [System.Drawing.Image]::FromFile($smallPng)

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = Scale ($(if ($s -le 24) { $small } else { $big })) $s
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
[IO.File]::WriteAllBytes($outIco, $out.ToArray())
# The About row in the settings shows the simpler design: its bold outline and fewer shapes
# read better at that size than the detailed one.
Copy-Item (Join-Path $iconDir 'app-icon-small.svg') (Join-Path $assets 'app-icon.svg') -Force
Write-Host "Wrote $outIco ($($out.Length) bytes) and Assets\app-icon.svg"

if ($PreviewFile) {
    # The icon at its real sizes on dark and light backgrounds, plus 128 px.
    $sheet = New-Object System.Drawing.Bitmap 560, 330
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
    $g.FillRectangle([System.Drawing.Brushes]::WhiteSmoke, 0, 165, 560, 165)
    foreach ($row in 0, 1) {
        $x = 12
        foreach ($s in 16, 20, 24, 32, 48, 64) {
            $bmp = Scale ($(if ($s -le 24) { $small } else { $big })) $s
            $g.DrawImage($bmp, $x, 12 + $row * 165, $s, $s); $bmp.Dispose(); $x += $s + 14
        }
        $g.DrawImage($big, 400, 12 + $row * 165, 140, 140)
    }
    $g.Dispose(); $sheet.Save($PreviewFile); $sheet.Dispose()
}
$big.Dispose(); $small.Dispose()
Remove-Item $work -Recurse -Force
