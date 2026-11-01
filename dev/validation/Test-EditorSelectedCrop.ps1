param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$SettingsDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$artifacts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($p in @($SettingsDirectory,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($p).StartsWith($artifacts,[StringComparison]::OrdinalIgnoreCase)){throw '専用artifactsパスが必要です。'}}
if(Test-Path -LiteralPath $SettingsDirectory){throw '新規プロファイルが必要です。'}
New-Item -ItemType Directory -Path $SettingsDirectory,$OutputDirectory -Force|Out-Null
. "$env:USERPROFILE/.codex/skills/windows-ui-automation/scripts/ui-toolkit.ps1"
Add-Type -AssemblyName System.Drawing.Common
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class CropTestKey { [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e); }
"@
$fixture=Join-Path $OutputDirectory 'fixture.png';$bitmap=[Drawing.Bitmap]::new(160,100)
try{$g=[Drawing.Graphics]::FromImage($bitmap);try{$g.Clear([Drawing.Color]::LightBlue)}finally{$g.Dispose()};$bitmap.Save($fixture,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
$test=Start-Process -FilePath $AppPath -ArgumentList @('--settings-dir',('"'+$SettingsDirectory+'"'),('"'+$fixture+'"')) -PassThru
$report=[ordered]@{ProcessId=$test.Id;Status='running';Checks=@();ClipboardChanged=$false};$failure=$null
function El([string]$id,[switch]$Optional){Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $id -AllowMissing:$Optional}
function InvokeId([string]$id){Invoke-ElementAction -Element (El $id) -Action Invoke;Start-Sleep -Milliseconds 180}
function Number([string]$id,[double]$value){(El $id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($value);Start-Sleep -Milliseconds 150}
function Dims([int]$w,[int]$h){if((El DimensionsButton).Current.Name -notmatch ('^'+$w+' × '+$h+' px')){throw ('寸法不一致: '+(El DimensionsButton).Current.Name)}}
try{
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$test.Id);$end=[DateTime]::UtcNow.AddSeconds(20)
    do{$windows=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -like '* - PPPaint - v*'});if($windows.Count -ne 1){Start-Sleep -Milliseconds 100}}while($windows.Count -ne 1 -and [DateTime]::UtcNow -lt $end)
    . (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $test.Id
    Start-Sleep -Milliseconds 300;Dims 160 100
    InvokeId DimensionsButton
    Invoke-ElementAction -Element (El DialogImageRatio) -Action Toggle
    Invoke-ElementAction -Element (El DialogCanvasCentered) -Action Toggle
    Number DialogImageWidth 300;Number DialogImageHeight 200;InvokeId DialogPrimaryButton;Number ZoomSlider 100
    InvokeId MoreButton;InvokeId SelectAllMenuItem;Select-TestTool Select
    if(-not (El CropButton).Current.IsEnabled){throw '画像選択だけでは切り抜きが有効になりません。'}
    InvokeId CropButton;Dims 160 100;InvokeId UndoButton;Dims 300 200
    $report.Checks+='single-image-selection-and-undo'
    Select-TestTool Rectangle;Number WidthBox 2;Invoke-TestImageDrag 180 80 220 110
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw '前面化できません。'}
    Assert-ForegroundIs -WindowHandle $script:EditorTestHandle;[CropTestKey]::keybd_event(17,0,0,[UIntPtr]::Zero)
    try{Invoke-TestImageClick 20 20}finally{[CropTestKey]::keybd_event(17,0,2,[UIntPtr]::Zero)}
    if((El InteractionCanvas).Current.ItemStatus -ne '選択: 2個'){throw '複数選択を確認できません。'}
    InvokeId CropButton;Dims 221 111;InvokeId UndoButton;Dims 300 200
    $report.Checks+='multiple-selection-includes-stroke-and-undo'
    InvokeId MoreButton;InvokeId SelectAllMenuItem;Select-TestTool Select
    Invoke-TestImageClick 20 20
    if(-not(Set-ForegroundForced -WindowHandle $script:EditorTestHandle)){throw '前面化できません。'}
    Send-TextInput -WindowHandle $script:EditorTestHandle -Text '^+x' -Raw;Start-Sleep -Milliseconds 200
    Dims 221 111;InvokeId UndoButton;Dims 300 200
    $report.Checks+='ctrl-shift-x-selected-objects'
    Select-TestTool Region;Invoke-TestImageDrag 5 6 35 26;InvokeId CropButton;Dims 30 20;InvokeId UndoButton;Dims 300 200
    $report.Checks+='explicit-region-priority'
    $report.Status='verified';$report.PhysicalInputUsed=$true
} catch{$failure=$_;$report.Status='failed';$report.Error=$_.Exception.Message}
finally{
    if(-not $test.HasExited -and $script:EditorTestHandle){
        $cancel=El DialogCloseButton -Optional;if($cancel){Invoke-ElementAction -Element $cancel -Action Invoke}
        [Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();Start-Sleep -Milliseconds 200
        if(-not $test.HasExited){$discard=El DialogSecondaryButton -Optional;if($discard){Invoke-ElementAction -Element $discard -Action Invoke}}
        $report.Closed=$test.WaitForExit(5000)
    }
    $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'crop-validation.json') -Encoding utf8
}
$report|ConvertTo-Json -Depth 5
if($failure){throw $failure}
