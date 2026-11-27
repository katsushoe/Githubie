param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\assets'
$source = [Drawing.Bitmap]::new((Join-Path $assetDirectory 'githubie-source.png'))
try {
    # Trim transparent margins, keeping the approved artwork unchanged.
    $left = $source.Width
    $top = $source.Height
    $right = 0
    $bottom = 0
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 16) {
                $left = [Math]::Min($left, $x)
                $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x)
                $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    if ($right -le $left -or $bottom -le $top) { throw 'The source icon has no visible artwork.' }
    $bounds = [Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $margin = [Math]::Max(1, [Math]::Round($size / 32))
            $scale = ($size - 2 * $margin) / [Math]::Max($bounds.Width, $bounds.Height)
            $width = [int][Math]::Round($bounds.Width * $scale)
            $height = [int][Math]::Round($bounds.Height * $scale)
            $target = [Drawing.Rectangle]::new([int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $graphics.DrawImage($source, $target, $bounds, [Drawing.GraphicsUnit]::Pixel)
            # Keep white fills and black strokes opaque after resampling; retain antialiased edges.
            for ($y = 0; $y -lt $size; $y++) {
                for ($x = 0; $x -lt $size; $x++) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    if ($pixel.A -ge 250) {
                        $color = if ($pixel.R -gt 240 -and $pixel.G -gt 240 -and $pixel.B -gt 240) {
                            [Drawing.Color]::White
                        } else { [Drawing.Color]::FromArgb(255, $pixel.R, $pixel.G, $pixel.B) }
                        $bitmap.SetPixel($x, $y, $color)
                    }
                }
            }
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$stream.ToArray()
            if ($size -eq 256) {
                [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'githubie.png'), $stream.ToArray())
            }
        }
        finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $assetDirectory 'githubie.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose() }
    Write-Output "Created githubie.ico with sizes: $($sizes -join ', ')"
}
finally { $source.Dispose() }
