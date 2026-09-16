# Draws the WindowSwitcher icon (rows of window tiles, one highlighted) and writes it as a
# multi-size .ico with PNG-compressed entries. Re-run after changing the drawing; the result is
# committed as src\WindowSwitcher\WindowSwitcher.ico.
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\src\WindowSwitcher\WindowSwitcher.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Design on a 256 grid: [x, y, w, h, isAccent]
$tiles = @(
    @(40, 44, 80, 48, $true), @(136, 44, 80, 48, $false),
    @(40, 108, 116, 48, $false),
    @(40, 172, 52, 40, $false), @(104, 172, 52, 40, $false), @(168, 172, 48, 40, $false)
)

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($s / 256.0, $s / 256.0)

    $bg = New-RoundedPath 8 8 240 240 44
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 36, 44))), $bg)

    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 154, 255))
    $plain = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 150, 160, 176))
    foreach ($t in $tiles) {
        $path = New-RoundedPath $t[0] $t[1] $t[2] $t[3] 8
        if ($t[4]) { $g.FillPath($accent, $path) } else { $g.FillPath($plain, $path) }
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += , $ms.ToArray()
}

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out)).Path + '\' + (Split-Path $Out -Leaf))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Dispose()
Write-Output "Wrote $Out ($offset bytes, sizes $($sizes -join ','))"
