[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetProcessId,[Parameter(Mandatory)][string]$SourcePath,[Parameter(Mandatory)][string]$OutputDirectory)

# Synthetic fixed-render PNG and isolated process. No physical pointer input.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Screenshot-UiHelpers.ps1') -TargetProcessId $TargetProcessId
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/'))
foreach($path in @($SourcePath,$OutputDirectory)){if(-not [IO.Path]::GetFullPath($path).StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'artifacts配下に限定してください。'}}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Find-Control([string]$Id) {Find-ByAutomationId -WindowHandle $script:EditorTestHandle -AutomationId $Id}
Select-TestTool Blur
$strength=Find-Control BlurRadiusBox
if(-not $strength.Current.IsEnabled){throw 'ぼかし強度を変更できません。'}
$strength.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(4)
Select-TestTool Select
Invoke-TestAction MoreButton; Invoke-TestAction SaveAsMenuItem
$saved=Join-Path $OutputDirectory ('blur-fixed-'+[guid]::NewGuid().ToString('N')+'.png')
Set-ElementValue -Element (Find-Control DialogFolder) -Value $OutputDirectory
Set-ElementValue -Element (Find-Control DialogFileName) -Value ([IO.Path]::GetFileName($saved))
Invoke-TestAction DialogPrimaryButton
if(-not(Test-Path $saved)){throw 'PNG保存に失敗しました。'}
$source=[Drawing.Bitmap]::new($SourcePath);$result=[Drawing.Bitmap]::new($saved)
try {
    if($source.Width -ne $result.Width -or $source.Height -ne $result.Height){throw '画像寸法が変化しました。'}
    for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
        if($source.GetPixel($x,$y).ToArgb() -ne $result.GetPixel($x,$y).ToArgb()){throw "PNG読込/保存でRGBAが変化しました: $x,$y"}
    }}
} finally {$source.Dispose();$result.Dispose()}
[Windows.Automation.AutomationElement]::FromHandle($script:EditorTestHandle).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
[pscustomobject]@{BlurToolAndStrength=$true;FixedPngLoadSaveAllRgba=$true;SavedPng=$saved;PhysicalBlurGestureVerified=$false} | ConvertTo-Json
