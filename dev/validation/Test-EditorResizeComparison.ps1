param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$UiOnly,[switch]$CaptureWindows)
$ErrorActionPreference='Stop'
$artifacts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($p in @($SettingsDirectory,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($p).StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)){throw '専用artifactsパスが必要です。'}}
if(Test-Path -LiteralPath $SettingsDirectory){throw '新規プロファイルが必要です。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force|Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Drawing.Common
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class PreviewTestKey { [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e); }
"@
$fixture=Join-Path $OutputDirectory 'fixture.png';$bitmap=[Drawing.Bitmap]::new(160,100)
try{$g=[Drawing.Graphics]::FromImage($bitmap);try{$g.Clear([Drawing.Color]::LightBlue);$g.FillRectangle([Drawing.Brushes]::Red,0,0,160,4);$g.FillRectangle([Drawing.Brushes]::Black,0,0,4,100);$g.FillRectangle([Drawing.Brushes]::Blue,156,0,4,100)}finally{$g.Dispose()};$bitmap.Save($fixture,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
$test=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"'),('"'+$fixture+'"')) -PassThru
$report=[ordered]@{ProcessId=$test.Id;Status='running';Checks=@();Drags=@();ClipboardChanged=$false;PhysicalInputUsed=$false};$failure=$null
function El([string]$id,[switch]$Optional){Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $id -AllowMissing:$Optional}
function InvokeId([string]$id){Invoke-ElementAction -Element (El $id) -Action Invoke;Start-Sleep -Milliseconds 180}
function Number([string]$id,[double]$value){(El $id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($value);Start-Sleep -Milliseconds 150}
function Dims([int]$w,[int]$h){if((El DimensionsButton).Current.Name -notmatch ('^'+$w+' × '+$h+' px')){throw ('寸法不一致: '+(El DimensionsButton).Current.Name)}}
$directions=@('TopLeft','Top','TopRight','Left','Right','BottomLeft','Bottom','BottomRight')
function OriginalViews {
    $views=@{}
    foreach($direction in $directions){
        $status=(El ('CanvasResizeDetail'+$direction)).Current.ItemStatus
        if($status -notmatch '固定元範囲 (.+?) px'){throw ('固定元範囲が取得できません: '+$direction)}
        $views[$direction]=$Matches[1]
    }
    return $views
}
function AssertFrozen($baseline){$current=OriginalViews;foreach($direction in $directions){if($baseline[$direction] -ne $current[$direction]){throw ('元画像の表示範囲が移動しました: '+$direction)}}}
function MovePointer([int]$X,[int]$Y){
    $desktop=[Windows.Forms.SystemInformation]::VirtualScreen
    $nx=[int][Math]::Round(($X-$desktop.Left)*65535/($desktop.Width-1));$ny=[int][Math]::Round(($Y-$desktop.Top)*65535/($desktop.Height-1))
    [WUA.Win]::mouse_event(0xC001,$nx,$ny,0,[UIntPtr]::Zero)
}
function DragView([string]$Id,[double]$X1,[double]$Y1,[double]$X2,[double]$Y2){
    $bounds=(El $Id).Current.BoundingRectangle;$width=100
    $sx=[int][Math]::Round($bounds.X+$X1*$bounds.Width/$width);$sy=[int][Math]::Round($bounds.Y+$Y1*$bounds.Height/100)
    $ex=$sx+[int][Math]::Round(($X2-$X1)*$bounds.Width/$width);$ey=$sy+[int][Math]::Round(($Y2-$Y1)*$bounds.Height/100)
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw '前面化できません。'}
    Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $sx -Y $sy
    Assert-PointInWindow -WindowHandle $script:EditorTestHandle -X $ex -Y $ey
    Assert-ForegroundIs -WindowHandle $script:EditorTestHandle
    $midWidth=$null;$report.PhysicalInputUsed=$true
    [void][WUA.Win]::SetCursorPos($sx,$sy);[WUA.Win]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    try{Start-Sleep -Milliseconds 100;for($step=1;$step -le 10;$step++){Assert-ForegroundIs -WindowHandle $script:EditorTestHandle;MovePointer ([int]($sx+($ex-$sx)*$step/10)) ([int]($sy+($ey-$sy)*$step/10));Start-Sleep -Milliseconds 30;if($step -eq 5){$midWidth=(El DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value}}}
    finally{[WUA.Win]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
    Start-Sleep -Milliseconds 220
    $report.Drags+=@{Id=$Id;Bounds=$bounds.ToString();Start=@($sx,$sy);End=@($ex,$ey);MidWidth=$midWidth;Width=(El DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value;Height=(El DialogImageHeight).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value;Status=(El $Id).Current.ItemStatus}
}
try{
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$test.Id);$end=[DateTime]::UtcNow.AddSeconds(20)
    do{$windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -like '* - PPPaint - v*'});if($windows.Count -ne 1){Start-Sleep -Milliseconds 100}}while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $end)
    . (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $test.Id
    Start-Sleep -Milliseconds 300;Dims 160 100
    InvokeId DimensionsButton
    if(El CanvasResizeThumbnail -Optional){throw '中央の全体図が残っています。'}
    $originalViews=OriginalViews
    $report.Checks+='eight-detail-views-without-overview'
    Invoke-ElementAction -Element (El DialogImageRatio) -Action Toggle
    Number DialogImageWidth 156;Number DialogImageHeight 96
    AssertFrozen $originalViews
    if((El CanvasResizeWidthDelta).Current.Name -notmatch '元 160 px.*-4 px' -or (El CanvasResizeHeightDelta).Current.Name -notmatch '元 100 px.*-4 px'){throw '元寸法/差分が表示されません。'}
    Dims 160 100
    if($CaptureWindows){Save-TestWindow (Join-Path $OutputDirectory 'trim-preview.png')}
    Number CanvasResizeMagnification 2
    if((El CanvasResizeVisiblePixels).Current.Name -ne '50 × 50 px'){throw '倍率で表示範囲が更新されません。'}
    $viewSize=(El CanvasResizeDetailRight).Current.BoundingRectangle.Width
    Number CanvasResizeMagnification .5
    if((El CanvasResizeVisiblePixels).Current.Name -ne '200 × 200 px' -or (El CanvasResizeDetailRight).Current.BoundingRectangle.Width -ne $viewSize){throw '枠固定の表示範囲変更が一致しません。'}
    Number CanvasResizeMagnification 4
    AssertFrozen $originalViews
    $report.Checks+='original-values-deltas-and-fixed-size-magnification'
    $report.Checks+='all-eight-original-viewports-stay-fixed-after-numeric-changes'
    $dialog=El EditorDialog
    $texts=@($dialog.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text))|ForEach-Object {$_.Current.Name})
    foreach($label in @('左上','上中央','右上','左中央','全体','右中央','左下','下中央','右下')){if($texts -contains $label){throw '位置ラベルが残っています。'}}
    $report.Checks+='position-labels-removed'
    if(-not $UiOnly){
    Invoke-ElementAction -Element (El DialogCanvasCentered) -Action Toggle
    Number DialogImageWidth 160;Number DialogImageHeight 100
    DragView CanvasResizeDetailRight 80 50 72 50
    if((El DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 158){if($CaptureWindows){Save-TestWindow (Join-Path $OutputDirectory 'failed-right-drag.png')};throw '右辺ドラッグが2px縮小になりません。'}
    if($report.Drags[-1].MidWidth -ge 160){throw '押したままの途中位置がプレビューに反映されません。'}
    DragView CanvasResizeDetailLeft 20 50 24 50
    if((El DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 157){throw '左辺ドラッグが1px縮小になりません。'}
    if((El CanvasResizeDetailLeft).Current.ItemStatus -notlike '*画像の移動 -1, 0 px*'){throw '左辺ドラッグで原点が変更されません。'}
    Dims 160 100
    AssertFrozen $originalViews
    $report.Checks+='detail-one-two-pixel-drag-with-original-canvas-unchanged'
    Invoke-ElementAction -Element (El DialogCanvasCentered) -Action Toggle
    Number DialogImageWidth 160;Number DialogImageHeight 100
    Invoke-ElementAction -Element (El DialogCanvasCentered) -Action Toggle
    DragView CanvasResizeDetailRight 80 50 88 50
    if((El DialogImageWidth).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 162){throw '右側の固定プレビューから外側へ2px変更できません。'}
    AssertFrozen $originalViews
    Invoke-ElementAction -Element (El DialogCanvasCentered) -Action Toggle
    $report.Checks+='fixed-detail-outward-drag-and-release'
    }
    Number DialogImageWidth 164;Number DialogImageHeight 104
    if((El CanvasResizeWidthDelta).Current.Name -notlike '*+4 px*'){throw '増加差分が表示されません。'}
    if($CaptureWindows){Save-TestWindow (Join-Path $OutputDirectory 'expanded-preview.png')}
    Number DialogImageWidth 156;Number DialogImageHeight 96
    InvokeId DialogPrimaryButton;Dims 156 96;InvokeId UndoButton;Dims 160 100
    $report.Checks+='candidate-expansion-and-apply-undo'
    InvokeId DimensionsButton;Number DialogImageWidth 170;InvokeId DialogCloseButton;Dims 160 100
    $report.Checks+='cancel-preserves-document'
    $report.Status='verified';$report.PhysicalInputUsed=(-not $UiOnly)
} catch{$failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message}
finally{
    if(-not $test.HasExited -and $script:EditorTestHandle){
        $cancel=El DialogCloseButton -Optional;if($cancel){Invoke-ElementAction -Element $cancel -Action Invoke}
        [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();Start-Sleep -Milliseconds 200
        if(-not $test.HasExited){$discard=El DialogSecondaryButton -Optional;if($discard){Invoke-ElementAction -Element $discard -Action Invoke}}
        $report.Closed=$test.WaitForExit(5000)
    }
    $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'preview-validation.json') -Encoding utf8
}
$report|ConvertTo-Json -Depth 5
if($failure){throw $failure}
