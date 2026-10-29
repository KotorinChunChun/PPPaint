[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$outputPath = Join-Path $ProjectRoot 'src\PPPaint\Assets\PPPaintIcon.ico'
$outputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function New-Color {
    param([byte]$Red, [byte]$Green, [byte]$Blue)
    return [Windows.Media.Color]::FromRgb($Red, $Green, $Blue)
}

function New-AppIconPng {
    param([Parameter(Mandatory)][int]$Size)

    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $start = [Windows.Media.GradientStop]::new((New-Color 117 104 232), 0)
        $end = [Windows.Media.GradientStop]::new((New-Color 75 67 184), 1)
        $stops = [Windows.Media.GradientStopCollection]::new()
        $stops.Add($start)
        $stops.Add($end)
        $gradient = [Windows.Media.LinearGradientBrush]::new()
        $gradient.GradientStops = $stops
        $gradient.StartPoint = [Windows.Point]::new(0, 0)
        $gradient.EndPoint = [Windows.Point]::new(1, 1)
        $white = [Windows.Media.SolidColorBrush]::new((New-Color 255 255 255))
        $accent = [Windows.Media.SolidColorBrush]::new((New-Color 217 213 255))
        $inkPen = [Windows.Media.Pen]::new($white, [Math]::Max(1, $Size * 0.065))
        $inkPen.StartLineCap = [Windows.Media.PenLineCap]::Round
        $inkPen.EndLineCap = [Windows.Media.PenLineCap]::Round
        $inkPen.LineJoin = [Windows.Media.PenLineJoin]::Round

        $margin = $Size * 0.03
        $corner = $Size * 0.23
        $drawing.DrawRoundedRectangle($gradient, $null, [Windows.Rect]::new($margin, $margin, $Size - ($margin * 2), $Size - ($margin * 2)), $corner, $corner)

        # 製品名の頭文字P。SVGと同じ形を各ICOサイズへ描画する。
        $letter = [Windows.Media.Geometry]::Parse('M16 15H31C41 15 46 20 46 28C46 36 41 41 31 41H25V51H16ZM25 23V33H31C35 33 37 31 37 28C37 25 35 23 31 23Z').Clone()
        $letter.Transform = [Windows.Media.ScaleTransform]::new($Size / 64.0, $Size / 64.0)
        $drawing.DrawGeometry($white, $null, $letter)
        $drawing.DrawLine($inkPen, [Windows.Point]::new($Size * 0.56, $Size * 0.68), [Windows.Point]::new($Size * 0.82, $Size * 0.42))
        $drawing.DrawLine([Windows.Media.Pen]::new($accent, [Math]::Max(1, $Size * 0.045)), [Windows.Point]::new($Size * 0.59, $Size * 0.70), [Windows.Point]::new($Size * 0.84, $Size * 0.45))
    }
    finally {
        $drawing.Close()
    }

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$sizes = @(16, 32, 48, 256)
$pngImages = @($sizes | ForEach-Object { New-AppIconPng $_ })
$headerSize = 6
$entrySize = 16
$imageOffset = $headerSize + ($entrySize * $sizes.Count)
$stream = [IO.File]::Create($outputPath)
try {
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $png = $pngImages[$index]
            $writer.Write([byte]($size -eq 256 ? 0 : $size))
            $writer.Write([byte]($size -eq 256 ? 0 : $size))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$png.Length)
            $writer.Write([uint32]$imageOffset)
            $imageOffset += $png.Length
        }
        foreach ($png in $pngImages) {
            $writer.Write($png)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Get-Item -LiteralPath $outputPath | Select-Object FullName, Length, LastWriteTime
