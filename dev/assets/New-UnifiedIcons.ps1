param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../src/ChunPaint/Assets/RibbonIcons/unified'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$culture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $culture
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$ink = [Windows.Media.BrushConverter]::new().ConvertFromString('#44536B')
$blue = [Windows.Media.BrushConverter]::new().ConvertFromString('#92D8EE')
$pink = [Windows.Media.BrushConverter]::new().ConvertFromString('#F0AEC5')
$mint = [Windows.Media.BrushConverter]::new().ConvertFromString('#A9DDCB')
$pen = [Windows.Media.Pen]::new($ink,1.65)
$pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
$pen.LineJoin = [Windows.Media.PenLineJoin]::Round
$paths = @{
 'D-Line'='M4,20 L20,4'; 'D-Curve'='M3,19 C3,2 21,22 21,5'; 'D-Ellipse'='M3,12 A9,7 0 1 0 21,12 A9,7 0 1 0 3,12';
 'D-Rectangle'='M3,5 H21 V19 H3 Z'; 'D-RoundedRectangle'='M7,4 H17 Q21,4 21,8 V16 Q21,20 17,20 H7 Q3,20 3,16 V8 Q3,4 7,4 Z';
 'D-Parallelogram'='M8,4 H22 L16,20 H2 Z'; 'D-Triangle'='M12,3 L22,21 H2 Z'; 'D-RightTriangle'='M3,3 V21 H21 Z';
 'D-Diamond'='M12,2 L22,12 L12,22 L2,12 Z'; 'D-Pentagon'='M12,2 L22,9 L18,21 H6 L2,9 Z'; 'D-Hexagon'='M7,3 H17 L23,12 L17,21 H7 L1,12 Z';
 'D-RightArrow'='M2,8 H13 V3 L22,12 L13,21 V16 H2 Z'; 'D-LeftArrow'='M22,8 H11 V3 L2,12 L11,21 V16 H22 Z';
 'D-UpArrow'='M8,22 V11 H3 L12,2 L21,11 H16 V22 Z'; 'D-DownArrow'='M8,2 V13 H3 L12,22 L21,13 H16 V2 Z';
 'D-FourPointStar'='M12,2 L15,9 L22,12 L15,15 L12,22 L9,15 L2,12 L9,9 Z';
 'D-Star'='M12,2 L15,8.5 L22,9.5 L17,15 L18.5,22 L12,18.5 L5.5,22 L7,15 L2,9.5 L9,8.5 Z';
 'D-Callout'='M3,3 H21 V16 H12 L6,22 V16 H3 Z'; 'D-Oval'='M7,12 A5,10 0 1 0 17,12 A5,10 0 1 0 7,12';
 'D-Cloud'='M6,19 C-1,19 0,10 5,10 C4,2 14,1 16,7 C23,4 26,15 20,18 Q14,22 6,19 Z';
 'D-Heart'='M12,21 C-7,9 5,-3 12,7 C19,-3 31,9 12,21 Z'; 'D-Lightning'='M13,2 L4,13 H11 L9,22 L21,9 H14 L18,2 Z';
 'A-save'='M3,3 H18 L21,6 V21 H3 Z M7,3 V9 H16 V3 M7,21 V14 H17 V21';
 'A-share'='M4,12 V21 H20 V12 M12,16 V2 M7,7 L12,2 L17,7';
 'A-undo'='M9,4 L3,10 L9,16 M3,10 H14 Q22,10 20,20'; 'A-redo'='M15,4 L21,10 L15,16 M21,10 H10 Q2,10 4,20';
 'A-display-mode'='M3,4 H9 V10 H3 Z M13,5 H21 M13,9 H21 M3,15 H21 M3,20 H17';
 'A-feedback'='M3,3 H21 V17 H10 L4,22 V17 H3 Z M7,8 H17 M7,12 H14';
 'A-account'='M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M3,22 C3,10 21,10 21,22 Z';
 'A-settings'='M9,2 H15 L16,6 L20,5 L23,10 L20,13 L21,17 L16,21 L12,19 L8,22 L3,18 L4,14 L1,10 L4,5 L8,6 Z M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12';
 'A-select-rectangle'='M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15';
 'A-select-free'='M6,19 C-2,14 4,0 12,4 C25,1 26,16 15,18 Q6,24 6,17 Q9,13 11,18 L8,23';
 'A-select-all'='M2,2 H22 V22 H2 Z M7,7 H17 V17 H7 Z'; 'A-cancel'='M5,5 L19,19 M19,5 L5,19';
 'A-select-transparent'='M3,3 H21 V21 H3 Z M3,12 H21 M12,3 V21 M3,3 L12,12 M12,12 L21,21';
 'A-delete'='M4,6 H20 M9,6 V2 H15 V6 M6,6 L7,22 H17 L18,6 M10,10 V18 M14,10 V18';
 'A-crop'='M6,2 V18 H22 M2,6 H18 V22'; 'A-resize'='M3,10 V21 H15 M3,3 H21 V21 M12,12 L21,3 M14,3 H21 V10';
 'A-import'='M3,4 H16 V21 H3 Z M6,17 L10,11 L14,17 M22,9 H12 M16,5 L12,9 L16,13';
 'A-background-remove'='M3,3 H21 V21 H3 Z M12,6 L18,17 H6 Z M1,23 L23,1';
 'A-rotate'='M4,11 A8,8 0 1 1 7,20 M4,4 V11 H11 M10,10 H17 V17 H10 Z';
 'A-pencil'='M3,21 L5,14 L17,2 L22,7 L10,19 Z M5,14 L10,19 M3,21 L7,20';
 'A-brush'='M10,13 L19,2 L22,5 L14,16 Z M10,13 Q3,12 3,21 Q13,23 14,16 Z';
 'A-bucket-fill'='M4,8 L12,2 L21,12 L12,21 L2,12 Z M5,13 H19 M20,16 Q15,23 20,23 Q24,23 20,16';
 'A-eraser'='M3,15 L13,3 Q15,1 17,3 L22,8 L11,21 H8 Z M7,11 L16,17';
 'A-eyedropper'='M4,20 L4,16 L15,5 L19,9 L8,20 Z M12,4 L16,8 M16,2 L22,8';
 'A-edit-colors'='M12,2 C-1,2 -1,22 10,22 Q15,22 13,17 Q12,13 18,14 C26,14 24,2 12,2 Z M6,8 L7,8 M12,6 L13,6 M18,8 L19,8 M6,14 L7,14';
 'A-layers'='M2,7 L12,2 L22,7 L12,12 Z M2,12 L12,17 L22,12 M2,17 L12,22 L22,17';
 'A-layer-add'='M2,4 H15 V18 H2 Z M12,17 H23 M17,12 V23';
 'A-layer-up'='M3,13 H12 V22 H3 Z M17,20 V3 M12,8 L17,3 L22,8';
 'A-layer-down'='M3,2 H12 V11 H3 Z M17,4 V21 M12,16 L17,21 L22,16';
 'A-layer-merge'='M2,2 H9 V9 H2 Z M15,2 H22 V9 H15 Z M6,12 L12,17 L18,12 M5,20 H19';
 'A-layer-rename'='M2,5 H15 V18 H2 Z M9,21 L11,15 L20,6 L23,9 L14,18 Z';
 'A-layer-visible'='M1,12 Q12,-1 23,12 Q12,25 1,12 Z M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12';
 'A-stroke-width'='M3,4 H21 M3,11 H21 M3,17 H21 V21 H3 Z';
 'A-opacity'='M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,2 V22';
 'A-pointer-position'='M12,2 V22 M2,12 H22 M8,8 H16 V16 H8 Z';
 'A-canvas-size'='M3,3 H18 V17 H3 Z M3,22 H18 M21,3 V17';
 'B-brush-oil'='M10,13 L17,2 H21 L15,15 M10,12 C2,11 1,23 8,22 Q18,23 15,15 Z';
 'B-brush-watercolor'='M10,14 L18,2 L21,4 L14,16 M10,14 Q1,16 4,22 Q14,23 14,16 M20,15 Q15,23 20,23 Q24,23 20,15';
 'B-brush-marker'='M5,16 L15,3 L22,8 L12,21 Z M5,16 L3,22 L12,21 M9,11 L16,16';
 'B-brush-airbrush'='M3,9 H14 V15 H3 Z M6,15 V22 H10 V15 M14,10 L18,8 V16 L14,14 M21,5 L22,5 M21,12 L22,12 M21,20 L22,20';
 'B-brush-calligraphy'='M4,21 L7,10 L16,2 L22,8 L14,17 Z M7,10 L14,17 M10,14 L18,6';
 'B-brush-crayon'='M3,21 L3,15 L14,4 L20,10 L9,21 Z M3,15 L9,21 M14,4 L18,2 L22,6 L20,10';
 'B-brush-flat'='M9,2 H15 V11 H21 V21 H3 V11 H9 Z M3,15 H21 M7,16 V21 M12,16 V21 M17,16 V21';
 'B-brush-pixel'='M3,3 H14 V14 H3 Z M14,14 H20 V20 H14 Z M20,20 H23 V23 H20 Z'
}
$paths['D-FilledStar']=$paths['D-Star']; $paths['A-layer-hidden']=$paths['A-layer-visible']+' M2,22 L22,2'; $paths['B-brush-natural-pencil']=$paths['A-pencil']
$magnifier='M2,10 A8,8 0 1 0 18,10 A8,8 0 1 0 2,10 M16,16 L23,23'
$paths['A-magnifier']=$magnifier; $paths['A-zoom-in']=$magnifier+' M6,10 H14 M10,6 V14'; $paths['A-zoom-out']=$magnifier+' M6,10 H14'; $paths['A-zoom-reset']=$magnifier+' M7,7 V13 M12,7 V13'
$paths['A-text']='M3,22 L12,2 L21,22 M7,15 H17'; $paths['A-text-italic']='M9,3 H19 M5,21 H15 M14,3 L10,21'
$paths['A-text-bold']='M5,2 V22 H13 C24,22 22,12 13,12 C23,12 22,2 13,2 Z M5,12 H13'
$paths['A-text-underline']='M5,2 V13 C5,22 19,22 19,13 V2 M3,23 H21'; $paths['A-text-strike']='M20,5 C5,-3 0,10 12,12 C27,14 17,29 3,20 M2,12 H22'
$ids = @(Get-Content (Join-Path $PSScriptRoot '../../docs/chunpaint-icon-inventory.md') | ForEach-Object { if ($_ -match '^\| ([ABDE]-[\w-]+) \|') { $Matches[1] } })
function Draw-Icon($dc,[string]$id) {
    $fill=$null; if ($id -eq 'D-FilledStar') {$fill=$pink}
    if ($id -eq 'A-opacity') {$dc.DrawGeometry($blue,$null,[Windows.Media.Geometry]::Parse('M12,2 A10,10 0 0 0 12,22 Z'))}
    if ($id.StartsWith('E-')) {
        $isFill=$id.StartsWith('E-fill-'); $kind=$id.Split('-')[-1]
        $brush=if($isFill){$blue}else{$null}; $localPen=$pen
        if($kind -eq 'watercolor') {$brush=[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(105,90,174,220))}
        if($kind -eq 'marker') {$localPen=[Windows.Media.Pen]::new($ink,3.5)}
        if($kind -eq 'pencil') {$localPen=[Windows.Media.Pen]::new($ink,1)}
        $dc.DrawRectangle($brush,$localPen,[Windows.Rect]::new(4,4,16,16))
        if($kind -eq 'none') {$dc.DrawLine([Windows.Media.Pen]::new($pink,2),[Windows.Point]::new(2,22),[Windows.Point]::new(22,2))}
        if($kind -in @('crayon','oil','pencil')) { for($n=6;$n -lt 20;$n+=4) {$dc.DrawLine([Windows.Media.Pen]::new($ink, $(if($kind -eq 'oil'){2.5}else{.9})),[Windows.Point]::new($n,18),[Windows.Point]::new([Math]::Min(19,$n+4),$(if($kind -eq 'crayon'){7}else{13})))} }
    } elseif($paths.ContainsKey($id)) {$dc.DrawGeometry($fill,$pen,[Windows.Media.Geometry]::Parse($paths[$id]))} else {throw "未定義アイコン: $id"}
}
foreach($id in $ids) { foreach($size in @(16,20,24,32,40,48,64)) {
    $visual=[Windows.Media.DrawingVisual]::new(); $dc=$visual.RenderOpen(); $dc.PushTransform([Windows.Media.ScaleTransform]::new($size/24.0,$size/24.0)); Draw-Icon $dc $id; $dc.Pop(); $dc.Close()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($visual)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap)); $stream=[IO.File]::Create((Join-Path $OutputDirectory "$id-$size.png")); try {$encoder.Save($stream)} finally {$stream.Dispose()}
} }
Write-Output "生成完了: $($ids.Count) ID / $($ids.Count * 7) PNG"
$sheet=[Windows.Media.DrawingVisual]::new(); $dc=$sheet.RenderOpen()
$rows=[int][Math]::Ceiling($ids.Count/8.0)
$dc.DrawRectangle([Windows.Media.Brushes]::White,$null,[Windows.Rect]::new(0,0,1280,$rows*86))
for($i=0;$i -lt $ids.Count;$i++) {
    $x=($i%8)*160; $y=[int][Math]::Floor($i/8)*86
    foreach($size in @(16,32)) {
        $dc.PushTransform([Windows.Media.TranslateTransform]::new($x+20+$(if($size -eq 32){40}else{0}),$y+8))
        $dc.PushTransform([Windows.Media.ScaleTransform]::new($size/24.0,$size/24.0)); Draw-Icon $dc $ids[$i]; $dc.Pop(); $dc.Pop()
    }
    $label=[Windows.Media.FormattedText]::new($ids[$i],$culture,[Windows.FlowDirection]::LeftToRight,[Windows.Media.Typeface]::new('Segoe UI'),10,$ink,1)
    $dc.DrawText($label,[Windows.Point]::new($x+4,$y+48))
}
$dc.Close(); $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1280,$rows*86,96,96,[Windows.Media.PixelFormats]::Pbgra32); $bitmap.Render($sheet)
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new(); $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap)); $stream=[IO.File]::Create((Join-Path $OutputDirectory 'contact-sheet.png')); try {$encoder.Save($stream)} finally {$stream.Dispose()}
