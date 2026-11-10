# Generates the Google Play store graphics into docs\google-play\graphics:
#   icon-512.png          the app icon (same crescent as Resources\AppIcon), 512 x 512
#   feature-1024x500.png  the feature graphic
#   phone-*.png           docs\screenshots padded with black to 16:9 (Play allows at most 2:1)
#
#   powershell -ExecutionPolicy Bypass -File tools\play\make-graphics.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$out = Join-Path $root 'docs\google-play\graphics'
New-Item -ItemType Directory -Force $out | Out-Null

$green = [System.Drawing.ColorTranslator]::FromHtml('#3FB58A')

# The crescent from appiconfg.svg (456-unit canvas): circle A minus circle B, plus a small star dot.
function Draw-Crescent([System.Drawing.Graphics]$g, [double]$x, [double]$y, [double]$size) {
    $s = $size / 456.0
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddEllipse([single]($x + (243.13 - 90) * $s), [single]($y + (228 - 90) * $s), [single](180 * $s), [single](180 * $s))
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddEllipse([single]($x + (262 - 88) * $s), [single]($y + (228 - 88) * $s), [single](176 * $s), [single](176 * $s))
    $region = New-Object System.Drawing.Region($outer)
    $region.Exclude($inner)
    $brush = New-Object System.Drawing.SolidBrush($green)
    $g.FillRegion($brush, $region)
    $g.FillEllipse($brush, [single]($x + (292 - 12) * $s), [single]($y + (190 - 12) * $s), [single](24 * $s), [single](24 * $s))
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Black)
    return @($bmp, $g)
}

# Icon
$bmp, $g = New-Canvas 512 512
# The launcher icon leaves a wide safe margin for Android's masks; the store icon can be fuller.
Draw-Crescent $g -128 -128 768
$bmp.Save((Join-Path $out 'icon-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# Feature graphic
$bmp, $g = New-Canvas 1024 500
Draw-Crescent $g 40 40 420
$title = New-Object System.Drawing.Font('Segoe UI Light', 64)
$sub = New-Object System.Drawing.Font('Segoe UI', 24)
$small = New-Object System.Drawing.Font('Segoe UI', 18)
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 235, 235))
$grey = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 150, 150))
$accent = New-Object System.Drawing.SolidBrush($green)
$g.DrawString('Salat Desk', $title, $white, 440, 130)
$g.DrawString('Always-on prayer clock for your desk', $sub, $grey, 446, 240)
$dot = [char]0x00B7   # kept out of the source text: Windows PowerShell 5.1 reads BOM-less files as ANSI
$g.DrawString("Jamaat countdown  $dot  Alerts  $dot  Quran & Hadith", $small, $accent, 448, 300)
$bmp.Save((Join-Path $out 'feature-1024x500.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# Phone screenshots: pad to 16:9 (landscape) on black.
$order = 'dashboard', 'ambient-quran', 'ambient-hadith', 'library', 'night', 'settings'
$n = 1
foreach ($name in $order) {
    $src = Join-Path $root "docs\screenshots\$name.png"
    if (-not (Test-Path $src)) { continue }
    $img = [System.Drawing.Image]::FromFile($src)
    $w = $img.Width
    $h = [int][Math]::Ceiling($w * 9 / 16)
    if ($h -lt $img.Height) { $h = $img.Height; $w = [int][Math]::Ceiling($h * 16 / 9) }
    $bmp, $g = New-Canvas $w $h
    $g.DrawImage($img, [int](($w - $img.Width) / 2), [int](($h - $img.Height) / 2), $img.Width, $img.Height)
    $bmp.Save((Join-Path $out ("phone-{0}-{1}.png" -f $n, $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $img.Dispose()
    $n++
}

Get-ChildItem $out | ForEach-Object { $i = [System.Drawing.Image]::FromFile($_.FullName); "{0}  {1}x{2}" -f $_.Name, $i.Width, $i.Height; $i.Dispose() }
