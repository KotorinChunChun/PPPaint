[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$OutputDirectory)

# Isolated disposable process with the synthetic image; UIA only, no physical input.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
if(-not [IO.Path]::GetFullPath($OutputDirectory).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw '出力先はartifacts配下に限定してください。'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Find-Control([string]$Id) {Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
Select-TestTool Eraser
if((Find-Control WidthCaption).Current.Name -ne '直径 (px)' -or (Find-Control WidthBox).Current.Name -ne '消しゴムの直径 (px)'){throw '消去直径が表示されません。'}
(Find-Control WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(4)
foreach($zoom in @(50,200)){
    (Find-Control ZoomSlider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($zoom)
    if((Find-Control WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value -ne 4){throw '表示倍率で消去直径が変わりました。'}
}
Select-TestTool Pen
if((Find-Control WidthCaption).Current.Name -ne '太さ' -or (Find-Control WidthBox).Current.Name -ne '線の太さ'){throw 'ペンの設定表示が戻りません。'}
Select-TestTool Eraser
(Find-Control WidthBox).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(40)
Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
$saved=Join-Path $OutputDirectory ('eraser-ui-'+[guid]::NewGuid().ToString('N')+'.png')
Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($saved))
Invoke-TestAction DialogPrimaryButton
if(-not(Test-Path $saved)){throw 'PNG保存に失敗しました。'}
$bitmap=[Drawing.Bitmap]::new($saved)
try {if($bitmap.Width -ne 900 -or $bitmap.Height -ne 550){throw '表示倍率で出力寸法が変化しました。'}} finally {$bitmap.Dispose()}
[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
[pscustomobject]@{EraserDiameterCaption=$true;FourPixelSettingAccepted=$true;DiameterIndependentOfZoom=$true;PenCaptionRestored=$true;PngOriginalDimensions=$true;PhysicalInputVerified=$false;SavedPng=$saved} | ConvertTo-Json
