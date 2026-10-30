[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$SourcePath, [Parameter(Mandatory)][string]$OutputDirectory)

# Disposable process, synthetic PNG only, isolated settings. UIA and clipboard APIs, no physical input.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$sourceFile = [IO.Path]::GetFullPath($SourcePath)
if (-not $outputRoot.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or -not $sourceFile.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw '検証対象と出力はartifacts配下に限定してください。' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if ([Threading.Thread]::CurrentThread.ApartmentState -ne [Threading.ApartmentState]::STA) { throw 'pwsh -STAで実行してください。' }
$sourceHandle = $script:EditorTestHandle
$sourceBitmap = [Drawing.Bitmap]::new($sourceFile)
$clipboardBackup = [Windows.Forms.Clipboard]::GetDataObject()
$clipboardRestored = $false
$evidence = [Collections.Generic.List[object]]::new()
function Find-TestControl([string]$Id) { Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id }
function Set-TestText([string]$Id, [string]$Value) { Set-ElementValue -Element (Find-TestControl $Id) -Value $Value }
function Save-TestPng([string]$Prefix) {
    $path = Join-Path $outputRoot ($Prefix + '-' + [guid]::NewGuid().ToString('N') + '.png')
    Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
    Set-TestText DialogFolder $outputRoot; Set-TestText DialogFileName ([IO.Path]::GetFileName($path)); Invoke-TestAction DialogPrimaryButton
    $deadline = (Get-Date).AddSeconds(8)
    while (-not (Test-Path -LiteralPath $path) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $path)) { throw "PNGが保存されません: $Prefix" }
    return $path
}
function Assert-SourcePixels([Drawing.Bitmap]$Bitmap) {
    if ($Bitmap.Width -ne $sourceBitmap.Width -or $Bitmap.Height -ne $sourceBitmap.Height) { throw 'キャンバスとコピーの寸法が違います。' }
    foreach ($x in @(0,9,100,300,($Bitmap.Width - 1))) { foreach ($y in @(0,20,100,250,($Bitmap.Height - 1))) {
        if ($Bitmap.GetPixel($x,$y).ToArgb() -ne $sourceBitmap.GetPixel($x,$y).ToArgb()) { throw "コピーで色が変化しました: $x,$y" }
    } }
}
function Assert-PngSource([string]$Path) { $bitmap=[Drawing.Bitmap]::new($Path); try { Assert-SourcePixels $bitmap } finally { $bitmap.Dispose() } }
function Set-CanvasTransparent([bool]$Value) {
    Invoke-TestAction CanvasBackgroundButton
    $box=Find-TestControl CanvasTransparentBox; $toggle=$box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $Value) { Invoke-ElementAction -Element $box -Action Toggle }
    Invoke-TestAction DialogPrimaryButton
}
function New-TestWindow {
    $old = $script:EditorTestHandle
    Invoke-TestAction NewButton
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$TargetProcessId)
    $deadline=(Get-Date).AddSeconds(8); $new=$null
    do {
        $new=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition) | Where-Object { $_.Current.Name -eq '無題 - PPPaint - v0.2.7' -and $_.Current.NativeWindowHandle -ne $old })
        if ($new.Count -eq 0) { Start-Sleep -Milliseconds 100 }
    } while ($new.Count -eq 0 -and (Get-Date) -lt $deadline)
    if ($new.Count -ne 1) { throw '新規の検証ウィンドウを一意に特定できません。' }
    $script:EditorTestHandle=[IntPtr]$new[0].Current.NativeWindowHandle
}
function Close-TestWindow { [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() }

try {
    foreach ($id in @('CopyButton','CutButton','CanvasBackgroundButton')) {
        $control=Find-TestControl $id
        if ([string]::IsNullOrWhiteSpace($control.Current.AcceleratorKey)) { throw "キー情報がありません: $id" }
    }
    Select-TestTool Number
    $number=Find-TestControl NumberStartBox
    $number.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(7)
    if ($number.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 7) { throw '開始番号を変更できません。' }
    Invoke-TestAction UndoButton
    if ((Find-TestControl NumberStartBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 1) { throw '番号開始値のUndoができません。' }
    Invoke-TestAction RedoButton
    if ((Find-TestControl NumberStartBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 7) { throw '番号開始値のRedoができません。' }
    Select-TestTool Select
    (Find-TestControl ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(150)
    if ((Find-TestControl DimensionsButton).Current.Name -notlike '900 × 550 px*') { throw '表示倍率で画像寸法が変わりました。' }
    Invoke-TestAction ZoomLabel

    Invoke-TestAction MoreButton; Invoke-TestAction SelectAllMenuItem
    if (-not (Find-TestControl CropButton).Current.IsEnabled -or -not (Find-TestControl CutButton).Current.IsEnabled) { throw '全体が範囲選択されていません。' }
    Invoke-TestAction CopyButton
    $clipboardImage=[Windows.Forms.Clipboard]::GetImage()
    if ($null -eq $clipboardImage) { throw 'ラスタ画像がクリップボードにありません。' }
    try { Assert-SourcePixels $clipboardImage } finally { $clipboardImage.Dispose() }
    Invoke-TestAction PasteButton
    if ((Find-TestControl StatusText).Current.Name -notmatch '\d+個を貼り付けました') { throw 'アプリ内コピーがオブジェクト保持で貼り付けられていません。' }
    $roundtrip=Save-TestPng 'copy-paste'; Assert-PngSource $roundtrip

    Invoke-TestAction MoreButton; Invoke-TestAction SelectAllMenuItem; Invoke-TestAction CutButton
    $cut=Save-TestPng 'cut-white'
    $cutBitmap=[Drawing.Bitmap]::new($cut)
    try { if ($cutBitmap.GetPixel(100,100).ToArgb() -ne [Drawing.Color]::White.ToArgb()) { throw '全体切取後に画素が残っています。' } } finally { $cutBitmap.Dispose() }
    Invoke-TestAction UndoButton
    $undo=Save-TestPng 'cut-undo'; Assert-PngSource $undo
    Invoke-TestAction RedoButton; Invoke-TestAction PasteButton
    if ((Find-TestControl StatusText).Current.Name -notmatch '\d+個を貼り付けました') { throw '切取後に編集情報が復元されません。' }
    $cutPaste=Save-TestPng 'cut-paste'; Assert-PngSource $cutPaste
    $evidence.Add([pscustomobject]@{ CopyRasterSameSizeAndColor=$true; InternalObjectPath=$true; CutUndoAndPaste=$true; Outputs=@($roundtrip,$cut,$undo,$cutPaste) })

    New-TestWindow
    Invoke-TestAction PasteButton
    if ((Find-TestControl StatusText).Current.Name -notmatch '\d+個を貼り付けました') { throw '別ウィンドウへのオブジェクト保持貼付ができません。' }
    $cross=Save-TestPng 'cross-window'; Assert-PngSource $cross
    Close-TestWindow; $script:EditorTestHandle=$sourceHandle
    New-TestWindow
    Set-CanvasTransparent $true
    Invoke-TestAction CopyButton; Invoke-TestAction PasteButton
    $transparent=Save-TestPng 'transparent'
    $alpha=[Drawing.Bitmap]::new($transparent)
    try { if ($alpha.GetPixel(0,0).A -ne 0 -or $alpha.GetPixel(500,300).A -ne 0) { throw '透明背景が黒/白で焼き込まれています。' } } finally { $alpha.Dispose() }
    Set-CanvasTransparent $false
    Invoke-TestAction CopyButton
    $white=[Windows.Forms.Clipboard]::GetImage()
    if ($null -eq $white) { throw '白背景のコピーがありません。' }
    try { if ($white.Width -ne 1000 -or $white.Height -ne 650 -or $white.GetPixel(0,0).ToArgb() -ne [Drawing.Color]::White.ToArgb()) { throw '白背景コピーの色/サイズが違います。' } } finally { $white.Dispose() }
    Invoke-TestAction PasteButton
    $opaque=Save-TestPng 'white'
    $whitePng=[Drawing.Bitmap]::new($opaque)
    try { if ($whitePng.GetPixel(0,0).ToArgb() -ne [Drawing.Color]::White.ToArgb()) { throw '白背景の貼付で色が変わりました。' } } finally { $whitePng.Dispose() }
    $evidence.Add([pscustomobject]@{ CrossWindowObjects=$true; TransparentCopyPasteAndPng=$true; WhiteCopyPasteAndPng=$true; Outputs=@($cross,$transparent,$opaque) })
    Close-TestWindow; $script:EditorTestHandle=$sourceHandle; Close-TestWindow
} finally {
    $sourceBitmap.Dispose()
    if ($null -eq $clipboardBackup) { [Windows.Forms.Clipboard]::Clear() } else { [Windows.Forms.Clipboard]::SetDataObject($clipboardBackup,$true,5,100) }
    $clipboardRestored=$true
}
[pscustomobject]@{ Checks=$evidence; StartNumberControlAndUndo=$true; ZoomDoesNotResizeImage=$true; ClipboardRestored=$clipboardRestored; PhysicalInputVerified=$false } | ConvertTo-Json -Depth 5
