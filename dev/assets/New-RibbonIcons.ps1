[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$outputRoot = Join-Path $ProjectRoot 'src\ChunPaint\Assets\RibbonIcons\final'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function New-Brush {
    param([byte]$Red, [byte]$Green, [byte]$Blue)
    $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb($Red, $Green, $Blue))
    $brush.Freeze()
    return $brush
}

function New-Pen {
    param([Windows.Media.Brush]$Brush, [double]$Thickness = 2)
    $pen = [Windows.Media.Pen]::new($Brush, $Thickness)
    $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
    $pen.Freeze()
    return $pen
}

function New-Polygon {
    param([Windows.Point[]]$Points)
    $geometry = [Windows.Media.StreamGeometry]::new()
    $context = $geometry.Open()
    try {
        $context.BeginFigure($Points[0], $true, $true)
        if ($Points.Length -gt 1) {
            $remaining = [Collections.Generic.List[Windows.Point]]::new()
            for ($index = 1; $index -lt $Points.Length; $index++) {
                $remaining.Add($Points[$index])
            }
            $context.PolyLineTo($remaining, $true, $true)
        }
    }
    finally {
        $context.Close()
    }
    $geometry.Freeze()
    return $geometry
}

function Save-Icon {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Draw
    )

    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        & $Draw $drawing
    }
    finally {
        $drawing.Close()
    }

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(32, 32, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path = Join-Path $outputRoot $Name
    $stream = [IO.File]::Create($path)
    try {
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
}

$ink = New-Brush 31 41 55
$blue = New-Brush 37 99 235
$cyan = New-Brush 56 189 248
$pink = New-Brush 244 114 182
$amber = New-Brush 245 158 11
$white = New-Brush 255 255 255
$inkPen = New-Pen $ink 2.2
$whitePen = New-Pen $white 1.5

Save-Icon 'resize.png' {
    param($drawing)
    $drawing.DrawRoundedRectangle($null, $inkPen, [Windows.Rect]::new(6, 6, 20, 20), 2, 2)
    $drawing.DrawLine((New-Pen $blue 2.4), [Windows.Point]::new(10, 22), [Windows.Point]::new(22, 10))
    $drawing.DrawLine($inkPen, [Windows.Point]::new(17, 10), [Windows.Point]::new(22, 10))
    $drawing.DrawLine($inkPen, [Windows.Point]::new(22, 10), [Windows.Point]::new(22, 15))
    $drawing.DrawLine($inkPen, [Windows.Point]::new(10, 17), [Windows.Point]::new(10, 22))
    $drawing.DrawLine($inkPen, [Windows.Point]::new(10, 22), [Windows.Point]::new(15, 22))
}

Save-Icon 'eraser.png' {
    param($drawing)
    $body = New-Polygon @([Windows.Point]::new(6, 21), [Windows.Point]::new(17, 6), [Windows.Point]::new(27, 14), [Windows.Point]::new(16, 27))
    $tip = New-Polygon @([Windows.Point]::new(6, 21), [Windows.Point]::new(11, 14), [Windows.Point]::new(21, 22), [Windows.Point]::new(16, 27))
    $drawing.DrawGeometry($pink, $inkPen, $body)
    $drawing.DrawGeometry($cyan, $null, $tip)
    $drawing.DrawLine($whitePen, [Windows.Point]::new(11, 14), [Windows.Point]::new(21, 22))
}

Save-Icon 'fill.png' {
    param($drawing)
    $bucket = New-Polygon @([Windows.Point]::new(7, 10), [Windows.Point]::new(18, 6), [Windows.Point]::new(26, 17), [Windows.Point]::new(15, 24), [Windows.Point]::new(7, 17))
    $drawing.DrawGeometry($white, $inkPen, $bucket)
    $paint = New-Polygon @([Windows.Point]::new(9, 15), [Windows.Point]::new(22, 13), [Windows.Point]::new(25, 18), [Windows.Point]::new(15, 23), [Windows.Point]::new(8, 18))
    $drawing.DrawGeometry($blue, $null, $paint)
    $drawing.DrawLine($inkPen, [Windows.Point]::new(12, 8), [Windows.Point]::new(20, 18))
    $drawing.DrawEllipse($cyan, $inkPen, [Windows.Point]::new(25, 26), 2.5, 3.5)
}

Save-Icon 'brush.png' {
    param($drawing)
    $drawing.PushTransform([Windows.Media.RotateTransform]::new(38, 16, 16))
    try {
        $drawing.DrawRoundedRectangle($amber, $inkPen, [Windows.Rect]::new(13, 3, 6, 16), 2, 2)
        $drawing.DrawRectangle($cyan, $inkPen, [Windows.Rect]::new(11, 17, 10, 5))
        $bristles = New-Polygon @([Windows.Point]::new(11, 22), [Windows.Point]::new(21, 22), [Windows.Point]::new(19, 29), [Windows.Point]::new(16, 26), [Windows.Point]::new(13, 29))
        $drawing.DrawGeometry($ink, $inkPen, $bristles)
    }
    finally {
        $drawing.Pop()
    }
}

Get-ChildItem -LiteralPath $outputRoot -Filter '*.png' -File |
    Sort-Object Name |
    Select-Object Name, Length, LastWriteTime
