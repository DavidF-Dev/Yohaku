# Generates src\Yohaku\Yohaku.ico (multi-resolution) from the "inset frame" design,
# drawn with GDI so there's no dependency on ImageMagick/Inkscape.
# Design is authored in a 48x48 coordinate space (matching tools\yohaku-icon.svg).

Add-Type -AssemblyName System.Drawing

$OutPath = Join-Path $PSScriptRoot "..\src\Yohaku\Yohaku.ico"
$Sizes   = 16, 24, 32, 48, 64, 256
$Color   = [System.Drawing.Color]::FromArgb(0xFF, 0x4C, 0x8D, 0xFF)

function Add-RoundedRect([System.Drawing.Drawing2D.GraphicsPath]$path, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $d = $r * 2
  $path.AddArc($x,        $y,        $d, $d, 180, 90)
  $path.AddArc($x + $w-$d,$y,        $d, $d, 270, 90)
  $path.AddArc($x + $w-$d,$y + $h-$d,$d, $d,   0, 90)
  $path.AddArc($x,        $y + $h-$d,$d, $d,  90, 90)
  $path.CloseFigure()
}

function New-IconPng([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $scale = $size / 48.0
  $g.ScaleTransform($scale, $scale)

  $pen = New-Object System.Drawing.Pen($Color, 3)
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $brush = New-Object System.Drawing.SolidBrush($Color)

  $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
  Add-RoundedRect $outer 4 4 40 40 9
  $g.DrawPath($pen, $outer)

  $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
  Add-RoundedRect $inner 14 14 20 20 5
  $g.FillPath($brush, $inner)

  $pen.Dispose(); $brush.Dispose(); $outer.Dispose(); $inner.Dispose(); $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return ,$ms.ToArray()
}

# Build the .ico container (PNG-compressed entries; supported on Windows Vista+).
$pngs = @{}
foreach ($s in $Sizes) { $pngs[$s] = New-IconPng $s }

$out = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type = icon
$bw.Write([uint16]$Sizes.Count)   # image count

$offset = 6 + 16 * $Sizes.Count
foreach ($s in $Sizes) {
  $data = $pngs[$s]
  $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the directory entry
  $bw.Write([byte]$dim)            # width
  $bw.Write([byte]$dim)            # height
  $bw.Write([byte]0)               # palette count
  $bw.Write([byte]0)               # reserved
  $bw.Write([uint16]1)             # planes
  $bw.Write([uint16]32)            # bit depth
  $bw.Write([uint32]$data.Length)  # bytes in resource
  $bw.Write([uint32]$offset)       # offset
  $offset += $data.Length
}
foreach ($s in $Sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()

$full = (Resolve-Path (Split-Path $OutPath)).Path
[System.IO.File]::WriteAllBytes((Join-Path $full (Split-Path $OutPath -Leaf)), $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Host "Wrote $OutPath ($($Sizes.Count) sizes, $([math]::Round((Get-Item (Join-Path $full (Split-Path $OutPath -Leaf))).Length/1kb,1)) KB)"
# Sanity: load it back as an Icon to confirm it's valid.
$ico = New-Object System.Drawing.Icon((Join-Path $full (Split-Path $OutPath -Leaf)))
Write-Host "Loaded OK, default size $($ico.Width)x$($ico.Height)"
$ico.Dispose()
