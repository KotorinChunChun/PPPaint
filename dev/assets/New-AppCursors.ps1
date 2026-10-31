[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$PreviewDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$assetDirectory = Join-Path $ProjectRoot 'src\PPPaint\Assets'
$resourcePath = Join-Path $assetDirectory 'PPPaintCursors.res'
$cursorModulePath = Join-Path $assetDirectory 'PPPaintCursors.dll'
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ("PPPaintCursors-{0}" -f [Guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null

function New-Color {
    param([byte]$Red, [byte]$Green, [byte]$Blue)
    return [Windows.Media.Color]::FromRgb($Red, $Green, $Blue)
}

function New-Pen {
    param([Windows.Media.Color]$Color, [double]$Thickness)
    $pen = [Windows.Media.Pen]::new([Windows.Media.SolidColorBrush]::new($Color), $Thickness)
    $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
    return $pen
}

function Draw-CursorIcon {
    param(
        [Windows.Media.DrawingContext]$Drawing,
        [string]$Kind
    )

    $black = New-Color 25 25 35
    $white = New-Color 255 255 255
    $orange = New-Color 245 158 44
    $blue = New-Color 54 149 214
    $pink = New-Color 230 105 145
    $green = New-Color 75 177 119
    $purple = New-Color 139 92 203
    $gray = New-Color 105 105 115
    $outline = New-Pen $white 6
    $edge = New-Pen $black 3

    switch ($Kind) {
        'Pencil' {
            $body = [Windows.Media.Geometry]::Parse('M3,28 L8,17 23,2 30,9 15,24 Z')
            $drawing.DrawGeometry([Windows.Media.SolidColorBrush]::new($orange), (New-Pen $white 3.5), $body)
            $drawing.DrawGeometry($null, (New-Pen $black 1.2), $body)
            $wood = [Windows.Media.Geometry]::Parse('M3,28 L8,17 15,24 Z')
            $drawing.DrawGeometry([Windows.Media.Brushes]::Wheat, (New-Pen $black 1), $wood)
            $drawing.DrawGeometry([Windows.Media.SolidColorBrush]::new($black), $null, [Windows.Media.Geometry]::Parse('M3,28 L5,23 8,26 Z'))
            $drawing.DrawLine((New-Pen $white 1), [Windows.Point]::new(11,18), [Windows.Point]::new(24,5))
        }
        'Oil' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(24, 25))
            $drawing.DrawLine((New-Pen $purple 5), [Windows.Point]::new(3, 3), [Windows.Point]::new(24, 25))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(24, 25))
            $drawing.DrawLine((New-Pen $white 2), [Windows.Point]::new(8, 7), [Windows.Point]::new(21, 20))
        }
        'Watercolor' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $blue 4), [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawEllipse([Windows.Media.SolidColorBrush]::new($blue), $null, [Windows.Point]::new(24, 25), 3, 3)
        }
        'Marker' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $pink 8), [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $pink 5), [Windows.Point]::new(4, 4), [Windows.Point]::new(24, 24))
        }
        'NaturalPencil' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $gray 4), [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $white 1), [Windows.Point]::new(8, 8), [Windows.Point]::new(22, 22))
        }
        'Airbrush' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(22, 22))
            $drawing.DrawLine((New-Pen $blue 6), [Windows.Point]::new(3, 3), [Windows.Point]::new(22, 22))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(22, 22))
            $drawing.DrawEllipse([Windows.Media.SolidColorBrush]::new($blue), $null, [Windows.Point]::new(25, 25), 4, 4)
        }
        'Calligraphy' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $green 7), [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(25, 25))
            $drawing.DrawLine((New-Pen $white 2), [Windows.Point]::new(7, 6), [Windows.Point]::new(24, 23))
        }
        'Eraser' {
            $drawing.DrawLine($outline, [Windows.Point]::new(5, 5), [Windows.Point]::new(24, 24))
            $drawing.DrawLine((New-Pen $pink 9), [Windows.Point]::new(5, 5), [Windows.Point]::new(24, 24))
            $drawing.DrawLine($edge, [Windows.Point]::new(5, 5), [Windows.Point]::new(24, 24))
            $drawing.DrawLine((New-Pen $white 2), [Windows.Point]::new(9, 9), [Windows.Point]::new(20, 20))
        }
        'Zoom' {
            $drawing.DrawEllipse([Windows.Media.SolidColorBrush]::new($white), $null, [Windows.Point]::new(12, 12), 8, 8)
            $drawing.DrawEllipse($null, $edge, [Windows.Point]::new(12, 12), 7, 7)
            $drawing.DrawLine($outline, [Windows.Point]::new(18, 18), [Windows.Point]::new(27, 27))
            $drawing.DrawLine((New-Pen $black 4), [Windows.Point]::new(18, 18), [Windows.Point]::new(27, 27))
        }
        'Picker' {
            $tube = [Windows.Media.Geometry]::Parse('M3,28 L6,20 20,6 26,12 12,26 Z')
            $drawing.DrawGeometry([Windows.Media.Brushes]::White, (New-Pen $white 3.5), $tube)
            $drawing.DrawGeometry([Windows.Media.Brushes]::LightBlue, (New-Pen $black 1.2), $tube)
            $drawing.DrawLine((New-Pen $blue 1.5), [Windows.Point]::new(7,24), [Windows.Point]::new(20,11))
            $drawing.DrawGeometry([Windows.Media.SolidColorBrush]::new($black), (New-Pen $white 1), [Windows.Media.Geometry]::Parse('M17,8 L24,1 Q31,0 31,7 L24,14 Z'))
            $drawing.DrawLine((New-Pen $black 2.5), [Windows.Point]::new(16,6), [Windows.Point]::new(27,17))
        }
        'Invisible' { }
        'Shape' {
            $drawing.DrawLine($outline, [Windows.Point]::new(3, 3), [Windows.Point]::new(26, 3))
            $drawing.DrawLine((New-Pen $blue 3), [Windows.Point]::new(3, 3), [Windows.Point]::new(26, 3))
            $drawing.DrawLine($edge, [Windows.Point]::new(3, 3), [Windows.Point]::new(26, 3))
            $drawing.DrawLine((New-Pen $blue 3), [Windows.Point]::new(3, 3), [Windows.Point]::new(3, 26))
            $drawing.DrawLine((New-Pen $blue 3), [Windows.Point]::new(3, 26), [Windows.Point]::new(26, 26))
            $drawing.DrawLine((New-Pen $blue 3), [Windows.Point]::new(26, 3), [Windows.Point]::new(26, 26))
        }
        'Fill' {
            $drawing.DrawLine($outline, [Windows.Point]::new(4, 4), [Windows.Point]::new(23, 23))
            $drawing.DrawLine((New-Pen $green 5), [Windows.Point]::new(4, 4), [Windows.Point]::new(23, 23))
            $drawing.DrawLine($edge, [Windows.Point]::new(4, 4), [Windows.Point]::new(23, 23))
            $drawing.DrawEllipse([Windows.Media.SolidColorBrush]::new($blue), $edge, [Windows.Point]::new(25, 25), 3, 3)
        }
    }
}

function New-CursorFile {
    param(
        [string]$Path,
        [string]$Kind,
        [int]$HotspotX = 2,
        [int]$HotspotY = 2,
        [int]$Size = 32
    )

    $size = $Size
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size / 32.0, $size / 32.0))
        Draw-CursorIcon $drawing $Kind
        $drawing.Pop()
    }
    finally {
        $drawing.Close()
    }

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    if ($PreviewDirectory -and $size -eq 32) {
        New-Item -ItemType Directory -Force -Path $PreviewDirectory | Out-Null
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $preview = [IO.File]::Create((Join-Path $PreviewDirectory "$Kind.png"))
        try { $encoder.Save($preview) } finally { $preview.Dispose() }
    }
    $pixels = [byte[]]::new($size * $size * 4)
    $bitmap.CopyPixels($pixels, $size * 4, 0)

    $andStride = [int]([Math]::Ceiling($size / 8.0) / 4) * 4
    $dibLength = 40 + ($size * $size * 4) + ($andStride * $size)
    $stream = [IO.MemoryStream]::new()
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]2)
            $writer.Write([uint16]1)
            $writer.Write([byte]$size)
            $writer.Write([byte]$size)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16][Math]::Round($HotspotX * $size / 32.0))
            $writer.Write([uint16][Math]::Round($HotspotY * $size / 32.0))
            $writer.Write([uint32]$dibLength)
            $writer.Write([uint32]22)
            $writer.Write([uint32]40)
            $writer.Write([int32]$size)
            $writer.Write([int32]($size * 2))
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]0)
            $writer.Write([uint32]($size * $size * 4))
            $writer.Write([int32]0)
            $writer.Write([int32]0)
            $writer.Write([uint32]0)
            $writer.Write([uint32]0)

            for ($y = $size - 1; $y -ge 0; $y--) {
                $row = [byte[]]::new($size * 4)
                [Array]::Copy($pixels, $y * $size * 4, $row, 0, $row.Length)
                $writer.Write($row)
            }

            for ($y = $size - 1; $y -ge 0; $y--) {
                $mask = [byte[]]::new($andStride)
                for ($x = 0; $x -lt $size; $x++) {
                    if ($pixels[(($y * $size) + $x) * 4 + 3] -lt 128) {
                        $mask[$x -shr 3] = $mask[$x -shr 3] -bor [byte](1 -shl (7 - ($x % 8)))
                    }
                }
                $writer.Write($mask)
            }
        }
        finally {
            $writer.Dispose()
        }
        [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }
}

function Join-CursorSizes([string[]]$Paths, [string]$Destination) {
    $images = @($Paths | ForEach-Object { ,([IO.File]::ReadAllBytes($_)) })
    $stream = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]2); $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        foreach ($bytes in $images) {
            $writer.Write($bytes, 6, 12); $writer.Write([uint32]$offset)
            $offset += $bytes.Length - 22
        }
        foreach ($bytes in $images) { $writer.Write($bytes, 22, $bytes.Length - 22) }
        [IO.File]::WriteAllBytes($Destination, $stream.ToArray())
    } finally { $writer.Dispose(); $stream.Dispose() }
}

$cursorDefinitions = [ordered]@{
    PPPaintCursorPencil = 'Pencil'
    PPPaintCursorOil = 'Oil'
    PPPaintCursorWatercolor = 'Watercolor'
    PPPaintCursorMarker = 'Marker'
    PPPaintCursorNaturalPencil = 'NaturalPencil'
    PPPaintCursorAirbrush = 'Airbrush'
    PPPaintCursorCalligraphy = 'Calligraphy'
    PPPaintCursorEraser = 'Eraser'
    PPPaintCursorZoom = 'Zoom'
    PPPaintCursorPicker = 'Picker'
    PPPaintCursorShape = 'Shape'
    PPPaintCursorFill = 'Fill'
    PPPaintCursorInvisible = 'Invisible'
}

try {
    foreach ($name in $cursorDefinitions.Keys) {
        $kind = $cursorDefinitions[$name]
        $hotspot = if ($kind -in @('Pencil','Picker')) { @(3,28) } elseif ($kind -eq 'Invisible') { @(0,0) } else { @(2,2) }
        $paths = @()
        foreach ($size in @(32,48,64)) {
            $path = Join-Path $tempDirectory "$name-$size.cur"
            New-CursorFile $path $kind $hotspot[0] $hotspot[1] $size
            $paths += $path
        }
        Join-CursorSizes $paths (Join-Path $tempDirectory "$name.cur")
    }

    $resourceLines = @('/* PPPaint独自のカーソルリソース */', '')
    foreach ($name in $cursorDefinitions.Keys) {
        $id = switch ($name) { 'PPPaintCursorPencil' { 1001 } 'PPPaintCursorPicker' { 1002 } 'PPPaintCursorInvisible' { 1003 } default { $name } }
        $resourceLines += "$id CURSOR DISCARDABLE `"$name.cur`""
    }
    $resourceScript = Join-Path $tempDirectory 'PPPaintCursors.rc'
    [IO.File]::WriteAllLines($resourceScript, $resourceLines, [Text.UTF8Encoding]::new($false))

    $resourceCompilerPath = $null
    $resourceCompiler = Get-Command 'rc.exe' -ErrorAction SilentlyContinue
    if ($null -ne $resourceCompiler) {
        $resourceCompilerPath = $resourceCompiler.Source
    }
    if ($null -eq $resourceCompilerPath) {
        $resourceCompilerPath = Get-ChildItem 'C:\Program Files (x86)\Windows Kits' -Recurse -Filter 'rc.exe' -ErrorAction SilentlyContinue |
            Where-Object FullName -match '\\x64\\rc\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ($null -eq $resourceCompilerPath) {
        throw 'Windows SDKのrc.exeが見つかりません。'
    }

    $linkerPath = $null
    $linker = Get-Command 'link.exe' -ErrorAction SilentlyContinue
    if ($null -ne $linker) {
        $linkerPath = $linker.Source
    }
    if ($null -eq $linkerPath) {
        $linkerPath = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio' -Recurse -Filter 'link.exe' -ErrorAction SilentlyContinue |
            Where-Object FullName -match '\\Hostx64\\x64\\link\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ($null -eq $linkerPath) {
        throw 'Visual Studioのlink.exeが見つかりません。'
    }

    Push-Location $tempDirectory
    try {
        & $resourceCompilerPath /nologo /fo $resourcePath $resourceScript
        if ($LASTEXITCODE -ne 0) {
            throw "rc.exeが終了コード$LASTEXITCODEで失敗しました。"
        }
        & $linkerPath /nologo /dll /noentry /machine:x64 /out:$cursorModulePath $resourcePath
        if ($LASTEXITCODE -ne 0) {
            throw "link.exeが終了コード$LASTEXITCODEで失敗しました。"
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        $resolved = [IO.Path]::GetFullPath($tempDirectory)
        $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
        if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or -not [IO.Path]::GetFileName($resolved).StartsWith('PPPaintCursors-')) { throw '一時資産の削除先が一致しません。' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

Get-Item -LiteralPath $resourcePath | Select-Object FullName, Length, LastWriteTime
