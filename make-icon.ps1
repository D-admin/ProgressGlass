param([string]$Source = (Join-Path $PSScriptRoot 'assets\mint-girl-head.png'), [string]$Output = (Join-Path $PSScriptRoot 'assets\mint-girl.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Format conversion only: package the generated head as standard Windows icon sizes.
$sourceBitmap = [Drawing.Bitmap]::new([IO.Path]::GetFullPath($Source))
$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($sourceBitmap,[Drawing.Rectangle]::new(0,0,$size,$size))
            $graphics.Dispose(); $graphics=$null
            for($y=0;$y -lt $size;$y++){for($x=0;$x -lt $size;$x++){if($bitmap.GetPixel($x,$y).A -le 8){$bitmap.SetPixel($x,$y,[Drawing.Color]::Transparent)}}}
            # Use classic 32-bit DIB icon frames. Older .NET Framework Icon.DrawIcon
            # misinterprets small PNG-compressed ICO frames on some Windows versions.
            $dib = [IO.BinaryWriter]::new($stream,[Text.Encoding]::UTF8,$true)
            $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
            $dib.Write([uint32]40);$dib.Write([int32]$size);$dib.Write([int32]($size*2))
            $dib.Write([uint16]1);$dib.Write([uint16]32);$dib.Write([uint32]0);$dib.Write([uint32]($size*$size*4+$maskStride*$size))
            $dib.Write([int32]0);$dib.Write([int32]0);$dib.Write([uint32]0);$dib.Write([uint32]0)
            for($y=$size-1;$y -ge 0;$y--){for($x=0;$x -lt $size;$x++){$pixel=$bitmap.GetPixel($x,$y);$dib.Write([byte]$pixel.B);$dib.Write([byte]$pixel.G);$dib.Write([byte]$pixel.R);$dib.Write([byte]$pixel.A)}}
            for($y=$size-1;$y -ge 0;$y--){
                $mask=[byte[]]::new($maskStride)
                for($x=0;$x -lt $size;$x++){if($bitmap.GetPixel($x,$y).A -eq 0){$index=[int][Math]::Floor($x/8);$mask[$index]=$mask[$index] -bor (128 -shr ($x%8))}}
                $dib.Write($mask)
            }
            $dib.Flush();$dib.Dispose()
            $frames.Add($stream.ToArray())
        } finally { if($graphics){$graphics.Dispose()}; $stream.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create([IO.Path]::GetFullPath($Output))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++) {
            $encodedSize = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
            $writer.Write([byte]$encodedSize);$writer.Write([byte]$encodedSize);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach($frame in $frames){$writer.Write($frame)}
    } finally {$writer.Dispose();$file.Dispose()}
} finally {$sourceBitmap.Dispose()}
"Created $Output (16, 20, 24, 32, 40, 48, 64, 128, 256 px)"
