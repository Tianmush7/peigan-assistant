[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root 'src/YingBan.App/Assets/app-icon.png'
$target = Join-Path $root 'src/YingBan.App/Assets/app-icon.ico'
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
$original = [Drawing.Image]::FromFile($source)
try {
    if ($original.Width -ne $original.Height) { throw 'The icon source must be square.' }
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $buffer = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($original,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$original.Width,$original.Height,[Drawing.GraphicsUnit]::Pixel,$attributes)
            $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($buffer.ToArray())
        } finally { $buffer.Dispose(); $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $original.Dispose() }
$writer = [IO.BinaryWriter]::new([IO.File]::Create($target))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
Write-Output "Created $target with sizes $($sizes -join ', ')."
