[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$SourcePath, [Parameter(Mandatory)][string]$OutputDirectory)

# Synthetic 900x550 input, isolated process/settings. UIA only, no physical input or clipboard.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SourcePath,$OutputDirectory)) {
    if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) {throw '検証対象と出力はartifacts配下に限定してください。'}
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=[Drawing.Bitmap]::new($SourcePath)
$outputs=[Collections.Generic.List[string]]::new()
function Find-Control([string]$Id) { Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id }
function Set-Number([string]$Id,[double]$Value) { (Find-Control $Id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($Value) }
function Set-Check([string]$Id,[bool]$Value) {
    $element=Find-Control $Id; $pattern=$element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if(($pattern.Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -ne $Value) { Invoke-ElementAction -Element $element -Action Toggle }
}
function Assert-Dimensions([int]$Width,[int]$Height) { if((Find-Control DimensionsButton).Current.Name -notlike "$Width × $Height px*") {throw "表示寸法が一致しません: $Width,$Height"} }
function Save-Png([string]$Prefix) {
    $path=Join-Path $OutputDirectory ($Prefix+'-'+[guid]::NewGuid().ToString('N')+'.png')
    Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
    Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
    Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($path))
    Invoke-TestAction DialogPrimaryButton
    $deadline=(Get-Date).AddSeconds(8)
    while(-not(Test-Path $path) -and (Get-Date) -lt $deadline) {Start-Sleep -Milliseconds 100}
    if(-not(Test-Path $path)){throw 'PNG保存に失敗しました。'}
    $outputs.Add($path); return $path
}
function Assert-Expanded([string]$Path,[int]$OffsetX,[int]$OffsetY,[bool]$Transparent=$false) {
    $bitmap=[Drawing.Bitmap]::new($Path)
    try {
        foreach($x in @(0,9,100,300,899)) {foreach($y in @(0,20,100,250,549)) {
            if($bitmap.GetPixel($x+$OffsetX,$y+$OffsetY).ToArgb() -ne $source.GetPixel($x,$y).ToArgb()){throw "元画像の画素が変化しました: $x,$y"}
        }}
        $margin=$bitmap.GetPixel($bitmap.Width-1,$bitmap.Height-1)
        if($Transparent) {if($margin.A -ne 0){throw '追加余白が透明になりません。'}}
        elseif($margin.ToArgb() -ne [Drawing.Color]::White.ToArgb()){throw '追加余白の背景色が不正です。'}
    } finally {$bitmap.Dispose()}
}
try {
    $initialTitle=[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).Current.Name
    $initialUndo=(Find-Control UndoButton).Current.IsEnabled
    Set-Number ZoomSlider 125
    Invoke-TestAction DimensionsButton
    Set-Number DialogImageWidth 450
    Assert-Dimensions 450 275
    if((Find-Control PaintCanvas).Current.ItemStatus -notlike '*-225, -137*'){throw '中心基準のプレビュー移動が不正です。'}
    $thumbnail=Find-Control CanvasResizeThumbnail
    if($thumbnail.Current.IsOffscreen -or $thumbnail.Current.BoundingRectangle.Width -lt 100 -or $thumbnail.Current.BoundingRectangle.Height -lt 100){throw 'キャンバスプレビューが表示されていません。'}
    if([Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).Current.Name -ne $initialTitle -or (Find-Control UndoButton).Current.IsEnabled -ne $initialUndo){throw 'プレビューが編集状態またはUndoを変更しました。'}
    Invoke-TestAction DialogCloseButton
    Assert-Dimensions 900 550
    if((Find-Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 125){throw '取消で表示倍率が戻りません。'}
    if((Find-Control PaintCanvas).Current.ItemStatus -ne ''){throw '取消後にプレビュー状態が残っています。'}

    Invoke-TestAction DimensionsButton
    Set-Check DialogImageRatio $false; Set-Number DialogImageWidth 1.5
    if((Find-Control DialogPrimaryButton).Current.IsEnabled -or [string]::IsNullOrEmpty((Find-Control CanvasResizeError).Current.Name)){throw '整数でない寸法を適用できてしまいます。'}
    Invoke-TestAction DialogCloseButton; Assert-Dimensions 900 550

    Invoke-TestAction DimensionsButton
    Set-Check DialogImageRatio $false; Set-Number DialogImageWidth 1100; Set-Number DialogImageHeight 650
    if((Find-Control PaintCanvas).Current.ItemStatus -notlike '*100, 50*'){throw '拡張プレビューが中心基準ではありません。'}
    Invoke-TestAction DialogPrimaryButton; Assert-Dimensions 1100 650
    Assert-Expanded (Save-Png 'center-expand') 100 50
    Invoke-TestAction UndoButton; Assert-Dimensions 900 550
    Invoke-TestAction RedoButton; Assert-Dimensions 1100 650
    Invoke-TestAction UndoButton

    Invoke-TestAction DimensionsButton
    Set-Check DialogImageRatio $false; Set-Number DialogImageWidth 500; Set-Number DialogImageHeight 350
    Invoke-TestAction DialogPrimaryButton; Assert-Dimensions 500 350
    $crop=Save-Png 'center-trim'; $bitmap=[Drawing.Bitmap]::new($crop)
    try { foreach($x in @(0,10,100,499)) {foreach($y in @(0,20,100,349)) {
        if($bitmap.GetPixel($x,$y).ToArgb() -ne $source.GetPixel($x+200,$y+100).ToArgb()){throw '中央の切り詰めで画像が拡大縮小されました。'}
    }} } finally {$bitmap.Dispose()}
    Invoke-TestAction UndoButton; Assert-Dimensions 900 550

    Invoke-TestAction DimensionsButton
    Set-Check DialogImageRatio $false; Set-Check DialogCanvasCentered $false; Set-Number DialogImageWidth 1000; Set-Number DialogImageHeight 600
    if((Find-Control PaintCanvas).Current.ItemStatus -notlike '*0, 0*'){throw '左上固定のプレビューが不正です。'}
    Invoke-TestAction DialogPrimaryButton; Assert-Dimensions 1000 600
    Assert-Expanded (Save-Png 'left-top-expand') 0 0
    Invoke-TestAction UndoButton; Assert-Dimensions 900 550

    Invoke-TestAction CanvasBackgroundButton; Set-Check CanvasTransparentBox $true; Invoke-TestAction DialogPrimaryButton
    Invoke-TestAction DimensionsButton
    Set-Check DialogImageRatio $false; Set-Number DialogImageWidth 1100; Set-Number DialogImageHeight 650
    Invoke-TestAction DialogPrimaryButton
    Assert-Expanded (Save-Png 'transparent-expand') 100 50 $true
    [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
} finally {$source.Dispose()}
[pscustomobject]@{LivePreview=$true;ThumbnailVisible=$true;CancelRestoresCanvasAndZoom=$true;PreviewLeavesDirtyAndUndoUnchanged=$true;FractionalSizeRejected=$true;CenteredExpansionAndTrim=$true;OneStepUndoRedo=$true;LeftTopAnchor=$true;SourcePixelsNotResampled=$true;TransparentMargins=$true;Outputs=$outputs;PhysicalInputVerified=$false} | ConvertTo-Json -Depth 4
