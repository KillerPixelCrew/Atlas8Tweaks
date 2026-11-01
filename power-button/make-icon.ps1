# Regenerates src\app.ico: a white power glyph with a green dot on a dark rounded tile.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    $radius = [Math]::Max(2, $size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $tile = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x22, 0x26, 0x2B))
    $g.FillPath($tile, $path)

    $glyph = $size * 0.74
    $offset = ($size - $glyph) / 2
    $stroke = [Math]::Max(1.5, $glyph / 8)
    $inset = $stroke / 2 + $glyph * 0.08
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $ring = New-Object System.Drawing.RectangleF ($offset + $inset), ($offset + $inset + $glyph * 0.04), ($glyph - 2 * $inset), ($glyph - 2 * $inset)
    $g.DrawArc($pen, $ring, 300, 300)
    $center = $size / 2
    $g.DrawLine($pen, $center, ($offset + $inset - $glyph * 0.02), $center, ($offset + $glyph * 0.46))

    $dot = $size * 0.3
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x2E, 0xD3, 0x6B))
    $g.FillEllipse($green, ($size - $dot - $size * 0.06), ($size - $dot - $size * 0.06), $dot, $dot)
    $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write($frame.Bytes) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'src\app.ico'), $out.ToArray())
Write-Host "Wrote src\app.ico"
